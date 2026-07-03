// SPDX-License-Identifier: Apache-2.0
// THROWAWAY investigation harness: characterizes how pocketken.H3 4.0.0's NTS-based
// polyfill diverges from this native binding (= libh3 4.5.0) across shapes/resolutions.
// Native path uses the SHIPPED H3Polygon.ToCells (center containment); pocketken uses
// Polyfill.Fill(..., VertexTestMode.Center) over NTS geometry, matching the benchmarks.

using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NetTopologySuite.Geometries;
using PocketkenPolyfill = H3.Algorithms.Polyfill;
using PocketkenVtm = H3.Algorithms.VertexTestMode;
using BLatLng = H3.NET.Native.LatLng;
using BIndex = H3.NET.Native.H3Index;
using BGeoPolygon = H3.NET.Native.GeoPolygon;
using BRegion = H3.NET.Native.H3Polygon;

namespace DivergenceHarness;

// A single ring-with-holes polygon. Coordinates are [lat, lng] degrees.
internal sealed record Poly(double[][] Shell, double[][][] Holes);

// A case is one or more polygons (multi = disjoint union) filled at a set of resolutions.
internal sealed record CaseDef(string Id, Poly[] Polys, int[] Res, bool MultiApi);

internal static partial class NativeSize
{
    [StructLayout(LayoutKind.Sequential)]
    public struct RLatLng { public double Lat; public double Lng; } // radians

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct RGeoLoop { public int NumVerts; public RLatLng* Verts; }

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct RGeoPolygon { public RGeoLoop GeoLoop; public int NumHoles; public RGeoLoop* Holes; }

    [LibraryImport("h3", EntryPoint = "maxPolygonToCellsSize")]
    public static unsafe partial uint MaxPolygonToCellsSize(RGeoPolygon* poly, int res, uint flags, out long size);

    private const double D2R = Math.PI / 180.0;

    // Native maxPolygonToCellsSize estimate for one polygon (radians ABI). Returns -1 on error.
    public static unsafe long Estimate(Poly p, int res)
    {
        var pins = new List<GCHandle>();
        try
        {
            var sv = new RLatLng[p.Shell.Length];
            for (int i = 0; i < p.Shell.Length; i++)
                sv[i] = new RLatLng { Lat = p.Shell[i][0] * D2R, Lng = p.Shell[i][1] * D2R };
            var sp = GCHandle.Alloc(sv, GCHandleType.Pinned);
            pins.Add(sp);

            var poly = new RGeoPolygon
            {
                GeoLoop = new RGeoLoop { NumVerts = sv.Length, Verts = (RLatLng*)sp.AddrOfPinnedObject() },
                NumHoles = p.Holes.Length,
            };

            if (p.Holes.Length > 0)
            {
                var hl = new RGeoLoop[p.Holes.Length];
                for (int h = 0; h < p.Holes.Length; h++)
                {
                    var hv = new RLatLng[p.Holes[h].Length];
                    for (int j = 0; j < p.Holes[h].Length; j++)
                        hv[j] = new RLatLng { Lat = p.Holes[h][j][0] * D2R, Lng = p.Holes[h][j][1] * D2R };
                    var hp = GCHandle.Alloc(hv, GCHandleType.Pinned);
                    pins.Add(hp);
                    hl[h] = new RGeoLoop { NumVerts = hv.Length, Verts = (RLatLng*)hp.AddrOfPinnedObject() };
                }
                var hlp = GCHandle.Alloc(hl, GCHandleType.Pinned);
                pins.Add(hlp);
                poly.Holes = (RGeoLoop*)hlp.AddrOfPinnedObject();
            }

            uint err = MaxPolygonToCellsSize(&poly, res, 0, out long size);
            return err != 0 ? -1 : size;
        }
        finally
        {
            foreach (var pin in pins)
                pin.Free();
        }
    }
}

internal static class Program
{
    private const long SkipThreshold = 500_000;
    private const int CellsInlineMax = 25_000;   // omit 'cells' field above this
    private const int DiffListCap = 25_000;      // cap only_* ID lists
    private const int BoundarySampleCap = 20_000; // cap per-cell boundary computation
    private const double EarthR = 6_371_008.8;    // mean Earth radius, meters
    private const double D2R = Math.PI / 180.0;

    private static int Main(string[] args)
    {
        string commit = args.Length > 0 ? args[0] : "unknown";
        string outDir = args.Length > 1
            ? args[1]
            : ".";
        Directory.CreateDirectory(outDir);
        string outPath = Path.Combine(outDir, "native-vs-pocketken.json");

        var cases = BuildCases();

        // Sanity-check that the native path reproduces the shipped benchmark counts.
        VerifyNativeBaseline();

        var results = new List<Dictionary<string, object?>>();
        var diffs = new List<Dictionary<string, object?>>();
        var skips = new List<Dictionary<string, object?>>();
        var multiApiCmp = new List<Dictionary<string, object?>>();
        var casesMeta = new Dictionary<string, object?>();

        foreach (var c in cases)
        {
            casesMeta[c.Id] = CaseCoords(c);

            foreach (int res in c.Res)
            {
                // ---- maxSize guard (sum over sub-polygons of the native estimate) ----
                long maxSize = 0;
                foreach (var p in c.Polys)
                {
                    long e = NativeSize.Estimate(p, res);
                    if (e < 0) { maxSize = -1; break; }
                    maxSize += e;
                }
                if (maxSize < 0 || maxSize > SkipThreshold)
                {
                    Console.WriteLine($"[SKIP] {c.Id} res={res} maxSize={maxSize} (> {SkipThreshold})");
                    skips.Add(new() { ["case_id"] = c.Id, ["res"] = res, ["max_size"] = maxSize, ["reason"] = maxSize < 0 ? "size-estimate-error" : "exceeds-500k" });
                    continue;
                }

                // ---- native (shipped path) ----
                var native = NativeCells(c.Polys, res);

                // ---- pocketken (union of per-polygon Fill, center containment) ----
                var (pocketken, pkErr) = RunGuarded(() => PocketkenUnion(c.Polys, res));

                Console.WriteLine($"[RUN ] {c.Id} res={res} maxSize={maxSize} native={native.Count} pocketken={(pkErr is null ? pocketken!.Count.ToString(CultureInfo.InvariantCulture) : "ERR:" + pkErr)}");

                results.Add(ResultEntry(c.Id, res, "native", native));
                if (pkErr is null)
                    results.Add(ResultEntry(c.Id, res, "pocketken", pocketken!));
                else
                    results.Add(new() { ["case_id"] = c.Id, ["res"] = res, ["impl"] = "pocketken", ["count"] = null, ["error"] = pkErr });

                diffs.Add(DiffEntry(c, res, native, pocketken, pkErr, apiTag: null));

                // ---- multi-2box:multiapi (pocketken native MultiPolygon fill) ----
                if (c.MultiApi)
                {
                    var (pkMulti, pkMErr) = RunGuarded(() => PocketkenMulti(c.Polys, res));
                    string mid = c.Id + ":multiapi";
                    // Native has no MultiPolygon fill; multiapi native == disjoint union.
                    results.Add(ResultEntry(mid, res, "native", native));
                    if (pkMErr is null)
                        results.Add(ResultEntry(mid, res, "pocketken", pkMulti!));
                    else
                        results.Add(new() { ["case_id"] = mid, ["res"] = res, ["impl"] = "pocketken", ["count"] = null, ["error"] = pkMErr });

                    diffs.Add(DiffEntry(c, res, native, pkMulti, pkMErr, apiTag: "multiapi"));

                    // pocketken multiapi vs union parity (mirrors the oracle's field).
                    if (pkErr is null && pkMErr is null)
                    {
                        bool eq = pocketken!.SetEquals(pkMulti!);
                        multiApiCmp.Add(new()
                        {
                            ["res"] = res,
                            ["union_count"] = pocketken.Count,
                            ["multiapi_count"] = pkMulti!.Count,
                            ["equal_to_union"] = eq,
                            ["union_sha256"] = Sha256(SortedHex(pocketken)),
                            ["multiapi_sha256"] = Sha256(SortedHex(pkMulti)),
                        });
                    }
                }
            }

            // Flush after every case so a late crash (e.g. transmeridian) still preserves data.
            WriteJson(outPath, commit, casesMeta, results, diffs, skips, multiApiCmp);
        }

        WriteJson(outPath, commit, casesMeta, results, diffs, skips, multiApiCmp);
        Console.WriteLine($"[DONE] wrote {outPath} ({results.Count} result rows, {diffs.Count} diffs, {skips.Count} skips)");
        return 0;
    }

    // ------------------------------------------------------------------ cases

    private static CaseDef[] BuildCases()
    {
        var sfTriangle = new Poly(
            new[]
            {
                new[] { 37.813318999983238, -122.4089866999972145 },
                new[] { 37.7866302000007224, -122.3805436999997056 },
                new[] { 37.7198061999978478, -122.3544736999993603 },
            },
            Array.Empty<double[][]>());

        const double minLat = 37.525, maxLat = 38.025, minLng = -122.668, maxLng = -122.168;
        var sweepBox = new Poly(
            new[]
            {
                new[] { minLat, minLng },
                new[] { minLat, maxLng },
                new[] { maxLat, maxLng },
                new[] { maxLat, minLng },
            },
            Array.Empty<double[][]>());

        var lshape = new Poly(
            new[]
            {
                new[] { 37.80, -122.46 },
                new[] { 37.80, -122.40 },
                new[] { 37.76, -122.40 },
                new[] { 37.76, -122.43 },
                new[] { 37.72, -122.43 },
                new[] { 37.72, -122.46 },
            },
            Array.Empty<double[][]>());

        var holeBox = new Poly(
            new[]
            {
                new[] { 37.81, -122.47 },
                new[] { 37.81, -122.37 },
                new[] { 37.71, -122.37 },
                new[] { 37.71, -122.47 },
            },
            new[]
            {
                new[]
                {
                    new[] { 37.78, -122.44 },
                    new[] { 37.78, -122.40 },
                    new[] { 37.74, -122.40 },
                    new[] { 37.74, -122.44 },
                },
            });

        var transmeridian = new Poly(
            new[]
            {
                new[] { 5.0, 179.8 },
                new[] { 5.0, -179.8 },
                new[] { 4.6, -179.8 },
                new[] { 4.6, 179.8 },
            },
            Array.Empty<double[][]>());

        var boxA = new Poly(
            new[]
            {
                new[] { 37.80, -122.46 },
                new[] { 37.80, -122.43 },
                new[] { 37.77, -122.43 },
                new[] { 37.77, -122.46 },
            },
            Array.Empty<double[][]>());

        var boxB = new Poly(
            new[]
            {
                new[] { 37.75, -122.41 },
                new[] { 37.75, -122.38 },
                new[] { 37.72, -122.38 },
                new[] { 37.72, -122.41 },
            },
            Array.Empty<double[][]>());

        return new[]
        {
            new CaseDef("sf-triangle", new[] { sfTriangle }, new[] { 7, 8, 9, 10, 11 }, false),
            new CaseDef("sweep-box", new[] { sweepBox }, new[] { 4, 5, 6, 7, 8, 9, 10 }, false),
            new CaseDef("lshape", new[] { lshape }, new[] { 7, 8, 9, 10 }, false),
            new CaseDef("hole-box", new[] { holeBox }, new[] { 7, 8, 9 }, false),
            new CaseDef("multi-2box", new[] { boxA, boxB }, new[] { 8, 9, 10 }, true),
            // transmeridian LAST: if pocketken/NTS mishandles the antimeridian and blows
            // up, every earlier case is already flushed to disk.
            new CaseDef("transmeridian", new[] { transmeridian }, new[] { 5, 6, 7 }, false),
        };
    }

    private static object CaseCoords(CaseDef c)
    {
        if (c.Polys.Length == 1)
            return new Dictionary<string, object?> { ["shell"] = c.Polys[0].Shell, ["holes"] = c.Polys[0].Holes };

        var d = new Dictionary<string, object?>();
        for (int i = 0; i < c.Polys.Length; i++)
            d["box" + (char)('A' + i)] = c.Polys[i].Shell;
        return d;
    }

    // ------------------------------------------------------------------ fills

    private static HashSet<ulong> NativeCells(Poly[] polys, int res)
    {
        var set = new HashSet<ulong>();
        foreach (var p in polys)
        {
            var shell = p.Shell.Select(pt => new BLatLng(pt[0], pt[1])).ToList();
            var holes = p.Holes
                .Select(h => (IReadOnlyList<BLatLng>)h.Select(pt => new BLatLng(pt[0], pt[1])).ToList())
                .ToList();
            var gp = new BGeoPolygon(shell, holes);
            foreach (var idx in BRegion.ToCells(gp, res))
                set.Add(idx.Value);
        }
        return set;
    }

    private static LinearRing ToRing(double[][] coords)
    {
        var pts = new Coordinate[coords.Length + 1];
        for (int i = 0; i < coords.Length; i++)
            pts[i] = new Coordinate(coords[i][1], coords[i][0]); // X=lng, Y=lat
        pts[coords.Length] = new Coordinate(coords[0][1], coords[0][0]); // close ring
        return new LinearRing(pts);
    }

    private static Polygon NtsPoly(Poly p)
    {
        var shell = ToRing(p.Shell);
        var holes = p.Holes.Select(ToRing).ToArray();
        return holes.Length == 0 ? new Polygon(shell) : new Polygon(shell, holes);
    }

    private static HashSet<ulong> PocketkenUnion(Poly[] polys, int res)
    {
        var set = new HashSet<ulong>();
        foreach (var p in polys)
            foreach (var idx in PocketkenPolyfill.Fill(NtsPoly(p), res, PocketkenVtm.Center))
                set.Add((ulong)idx);
        return set;
    }

    private static HashSet<ulong> PocketkenMulti(Poly[] polys, int res)
    {
        var mp = new MultiPolygon(polys.Select(NtsPoly).ToArray());
        var set = new HashSet<ulong>();
        foreach (var idx in PocketkenPolyfill.Fill(mp, res, PocketkenVtm.Center))
            set.Add((ulong)idx);
        return set;
    }

    // Runs a fill under a hard timeout so a pathological (e.g. antimeridian) input cannot
    // hang the whole run. Returns (result, null) or (null, errorMessage).
    private static (HashSet<ulong>?, string?) RunGuarded(Func<HashSet<ulong>> fn)
    {
        try
        {
            var task = System.Threading.Tasks.Task.Run(fn);
            if (!task.Wait(TimeSpan.FromSeconds(240)))
                return (null, "TIMEOUT after 240s (likely unbounded fill)");
            return (task.Result, null);
        }
        catch (Exception ex)
        {
            var e = ex is AggregateException ae && ae.InnerException is not null ? ae.InnerException : ex;
            return (null, e.GetType().Name + ": " + e.Message);
        }
    }

    // ------------------------------------------------------------------ output rows

    private static Dictionary<string, object?> ResultEntry(string caseId, int res, string impl, HashSet<ulong> cells)
    {
        var sorted = SortedHex(cells);
        var entry = new Dictionary<string, object?>
        {
            ["case_id"] = caseId,
            ["res"] = res,
            ["impl"] = impl,
            ["count"] = cells.Count,
            ["sha256"] = Sha256(sorted),
        };
        if (cells.Count <= CellsInlineMax)
            entry["cells"] = sorted;
        return entry;
    }

    private static Dictionary<string, object?> DiffEntry(
        CaseDef c, int res, HashSet<ulong> native, HashSet<ulong>? pocketken, string? pkErr, string? apiTag)
    {
        string caseId = apiTag is null ? c.Id : c.Id + ":" + apiTag;
        if (pkErr is not null || pocketken is null)
        {
            return new()
            {
                ["case_id"] = caseId,
                ["res"] = res,
                ["native_count"] = native.Count,
                ["pocketken_count"] = null,
                ["only_native"] = Array.Empty<string>(),
                ["only_pocketken"] = Array.Empty<string>(),
                ["symmetric"] = false,
                ["boundary_note"] = "pocketken fill failed: " + pkErr,
            };
        }

        var onlyN = new HashSet<ulong>(native); onlyN.ExceptWith(pocketken);
        var onlyP = new HashSet<ulong>(pocketken); onlyP.ExceptWith(native);

        // one-sided (subset) vs symmetric: symmetric == both sides have exclusives.
        bool symmetric = onlyN.Count > 0 && onlyP.Count > 0;

        var onlyNhex = SortedHex(onlyN);
        var onlyPhex = SortedHex(onlyP);

        string note = BoundaryNote(c, onlyN, onlyP);

        var entry = new Dictionary<string, object?>
        {
            ["case_id"] = caseId,
            ["res"] = res,
            ["native_count"] = native.Count,
            ["pocketken_count"] = pocketken.Count,
            ["only_native_count"] = onlyN.Count,
            ["only_pocketken_count"] = onlyP.Count,
            ["direction"] = onlyN.Count == 0 && onlyP.Count == 0 ? "identical"
                : onlyP.Count == 0 ? "pocketken-subset-of-native"
                : onlyN.Count == 0 ? "native-subset-of-pocketken"
                : "symmetric",
            ["only_native"] = Cap(onlyNhex, out bool tN),
            ["only_native_truncated"] = tN,
            ["only_pocketken"] = Cap(onlyPhex, out bool tP),
            ["only_pocketken_truncated"] = tP,
            ["symmetric"] = symmetric,
            ["boundary_note"] = note,
        };
        return entry;
    }

    private static List<string> Cap(List<string> xs, out bool truncated)
    {
        truncated = xs.Count > DiffListCap;
        return truncated ? xs.GetRange(0, DiffListCap) : xs;
    }

    // ------------------------------------------------------------------ boundary analysis

    // For each differing cell, min great-circle distance (m) from its center to the
    // nearest polygon-ring edge, and whether that is within the cell's own circumradius
    // (i.e. the polygon boundary passes through/near the cell). Tests the hypothesis
    // that divergent cells hug the ring boundary.
    private static string BoundaryNote(CaseDef c, HashSet<ulong> onlyN, HashSet<ulong> onlyP)
    {
        var diffCells = new List<ulong>();
        diffCells.AddRange(onlyN);
        diffCells.AddRange(onlyP);
        if (diffCells.Count == 0)
            return "no differing cells";

        // All ring segments (exterior + holes) across every sub-polygon of the case.
        var segs = new List<(double aLat, double aLng, double bLat, double bLng)>();
        foreach (var p in c.Polys)
        {
            AddRingSegs(segs, p.Shell);
            foreach (var h in p.Holes)
                AddRingSegs(segs, h);
        }

        int sampled = Math.Min(diffCells.Count, BoundarySampleCap);
        var dists = new List<double>(sampled);
        int within = 0;
        for (int i = 0; i < sampled; i++)
        {
            var cell = new BIndex(diffCells[i]);
            var center = cell.ToLatLng();
            double best = double.MaxValue;
            foreach (var s in segs)
            {
                double d = PointToSegMeters(center.LatitudeDegrees, center.LongitudeDegrees, s.aLat, s.aLng, s.bLat, s.bLng);
                if (d < best) best = d;
            }
            dists.Add(best);

            // circumradius = max center-to-boundary-vertex distance (meters).
            double circum = 0;
            foreach (var v in cell.GetBoundary())
            {
                double d = AngM(center.LatitudeDegrees, center.LongitudeDegrees, v.LatitudeDegrees, v.LongitudeDegrees);
                if (d > circum) circum = d;
            }
            if (best <= circum) within++;
        }

        dists.Sort();
        double min = dists[0], max = dists[^1];
        double median = dists[dists.Count / 2];
        double mean = dists.Average();
        double pctWithin = 100.0 * within / sampled;
        string samp = sampled < diffCells.Count ? $" (sampled {sampled}/{diffCells.Count})" : "";
        return string.Format(CultureInfo.InvariantCulture,
            "diff cells={0}{7}; center-to-nearest-edge (m): min={1:F1} median={2:F1} mean={3:F1} max={4:F1}; {5}/{6} ({8:F0}%) within one cell circumradius of an edge (boundary-hugging)",
            diffCells.Count, min, median, mean, max, within, sampled, samp, pctWithin);
    }

    private static void AddRingSegs(List<(double, double, double, double)> segs, double[][] ring)
    {
        for (int i = 0; i < ring.Length; i++)
        {
            var a = ring[i];
            var b = ring[(i + 1) % ring.Length];
            segs.Add((a[0], a[1], b[0], b[1]));
        }
    }

    // Central angle (radians) between two lat/lng degree points, haversine.
    private static double Ang(double lat1, double lng1, double lat2, double lng2)
    {
        double p1 = lat1 * D2R, p2 = lat2 * D2R;
        double dp = (lat2 - lat1) * D2R, dl = (lng2 - lng1) * D2R;
        double a = Math.Sin(dp / 2) * Math.Sin(dp / 2) + Math.Cos(p1) * Math.Cos(p2) * Math.Sin(dl / 2) * Math.Sin(dl / 2);
        return 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
    }

    private static double AngM(double lat1, double lng1, double lat2, double lng2) => Ang(lat1, lng1, lat2, lng2) * EarthR;

    private static double Bearing(double lat1, double lng1, double lat2, double lng2)
    {
        double p1 = lat1 * D2R, p2 = lat2 * D2R, dl = (lng2 - lng1) * D2R;
        double y = Math.Sin(dl) * Math.Cos(p2);
        double x = Math.Cos(p1) * Math.Sin(p2) - Math.Sin(p1) * Math.Cos(p2) * Math.Cos(dl);
        return Math.Atan2(y, x);
    }

    // Great-circle distance (m) from point P to segment A-B, clamped to the segment ends.
    private static double PointToSegMeters(double pLat, double pLng, double aLat, double aLng, double bLat, double bLng)
    {
        double d13 = Ang(aLat, aLng, pLat, pLng);
        double d12 = Ang(aLat, aLng, bLat, bLng);
        if (d12 == 0) return d13 * EarthR;

        double th13 = Bearing(aLat, aLng, pLat, pLng);
        double th12 = Bearing(aLat, aLng, bLat, bLng);
        double dxt = Math.Asin(Math.Clamp(Math.Sin(d13) * Math.Sin(th13 - th12), -1.0, 1.0));
        double dat = Math.Acos(Math.Clamp(Math.Cos(d13) / Math.Cos(dxt), -1.0, 1.0));

        if (dat <= 0) return d13 * EarthR;                 // nearest endpoint is A
        if (dat >= d12) return Ang(bLat, bLng, pLat, pLng) * EarthR; // nearest endpoint is B
        return Math.Abs(dxt) * EarthR;
    }

    // ------------------------------------------------------------------ hashing / hex

    private static List<string> SortedHex(HashSet<ulong> cells)
    {
        var list = new List<string>(cells.Count);
        foreach (var v in cells)
            list.Add(v.ToString("x", CultureInfo.InvariantCulture)); // lowercase, no leading zeros
        list.Sort(StringComparer.Ordinal);
        return list;
    }

    private static string Sha256(List<string> sortedHex)
    {
        string joined = string.Join("\n", sortedHex);
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(joined));
        var sb = new StringBuilder(hash.Length * 2);
        foreach (byte b in hash)
            sb.Append(b.ToString("x2", CultureInfo.InvariantCulture));
        return sb.ToString();
    }

    // ------------------------------------------------------------------ baseline check

    private static void VerifyNativeBaseline()
    {
        // sf-triangle res 9 must be 55 (shipped benchmark / oracle baseline).
        var tri = new BGeoPolygon(new List<BLatLng>
        {
            new(37.813318999983238, -122.4089866999972145),
            new(37.7866302000007224, -122.3805436999997056),
            new(37.7198061999978478, -122.3544736999993603),
        });
        int n9 = BRegion.ToCells(tri, 9).Length;
        Console.WriteLine($"[BASE] native sf-triangle res9 = {n9} (expected 55)");

        var box = new BGeoPolygon(new List<BLatLng>
        {
            new(37.525, -122.668),
            new(37.525, -122.168),
            new(38.025, -122.168),
            new(38.025, -122.668),
        });
        Console.WriteLine($"[BASE] native sweep-box res8 = {BRegion.ToCells(box, 8).Length} (expected 3189), res10 = {BRegion.ToCells(box, 10).Length} (expected 156334)");
    }

    // ------------------------------------------------------------------ json

    private static void WriteJson(
        string path, string commit, Dictionary<string, object?> casesMeta,
        List<Dictionary<string, object?>> results, List<Dictionary<string, object?>> diffs,
        List<Dictionary<string, object?>> skips, List<Dictionary<string, object?>> multiApiCmp)
    {
        var doc = new Dictionary<string, object?>
        {
            ["meta"] = new Dictionary<string, object?>
            {
                ["generated_utc"] = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
                ["impl_versions"] = new Dictionary<string, object?>
                {
                    ["binding_commit"] = commit,
                    ["pocketken_h3"] = "4.0.0",
                    ["libh3"] = "4.5.0",
                    ["nettopologysuite"] = "2.5.0",
                    ["dotnet_sdk"] = "10.0.301",
                },
                ["method"] = "native = H3Polygon.ToCells (shipped, center containment); pocketken = Polyfill.Fill(geometry, res, VertexTestMode.Center) over NTS geometry (union of per-polygon fills for multi). Cell IDs are lowercase hex via ulong.ToString(\"x\") (no leading zeros), sorted ordinal ascending; sha256 over sorted IDs joined with \\n (UTF-8). 'cells' omitted when count > 25000; (case,res) skipped when native maxPolygonToCellsSize estimate > 500000.",
                ["cases"] = casesMeta,
                ["skips"] = skips,
                ["multi_2box_multiapi_vs_union"] = multiApiCmp,
            },
            ["results"] = results,
            ["diffs"] = diffs,
        };

        var opts = new JsonSerializerOptions { WriteIndented = true };
        File.WriteAllText(path, JsonSerializer.Serialize(doc, opts));
    }
}

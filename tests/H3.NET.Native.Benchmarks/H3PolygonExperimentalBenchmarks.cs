// SPDX-License-Identifier: Apache-2.0

using System;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Order;

namespace H3.NET.Native.Benchmarks;

/// <summary>
/// Stable vs experimental polygonToCells for this native binding: the same fixed
/// ~0.5 degree SF box (identical to <see cref="H3PolygonSweepBenchmarks"/>) is filled
/// across a resolution sweep by the stable <see cref="H3Polygon.ToCells(GeoPolygon, int)"/>
/// and by the experimental <see cref="H3Polygon.ToCellsExperimental"/> in
/// <see cref="ContainmentMode.Center"/> mode. Center mode is the containment predicate
/// the stable API always uses, so the two must emit the identical cell count at every
/// resolution; <see cref="Setup"/> prints and cross-checks the counts to pin that.
///
/// The resolution sweep stops at res 8 to bound DefaultJob runtime (the higher res 9/10
/// fills are covered by <see cref="H3PolygonSweepBenchmarks"/>). The stable overload is
/// the per-category baseline, matching the other polygon benchmarks.
/// </summary>
// Like H3Benchmarks, this relies on BenchmarkDotNet's default config, which already
// emits the GitHub-flavored Markdown report; adding exporters explicitly only
// duplicates the default and triggers a config warning.
[MemoryDiagnoser]
[CategoriesColumn]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[Orderer(SummaryOrderPolicy.Declared)]
public class H3PolygonExperimentalBenchmarks
{
    // A fixed ~0.5 degree box centered on the SF test point (degrees), identical to the
    // one H3PolygonSweepBenchmarks fills so the two sweeps are directly comparable.
    private const double MinLat = 37.525;
    private const double MaxLat = 38.025;
    private const double MinLng = -122.668;
    private const double MaxLng = -122.168;

    // Box exterior for the binding (degrees). An H3 GeoLoop is implicitly closed, so the
    // four distinct corners are supplied without repeating the first.
    private static readonly LatLng[] BoxExterior =
    [
        new LatLng(MinLat, MinLng),
        new LatLng(MinLat, MaxLng),
        new LatLng(MaxLat, MaxLng),
        new LatLng(MaxLat, MinLng),
    ];

    // Bounded at res 8 to keep DefaultJob runtime reasonable (the same box already emits
    // ~1e4 cells at res 8; higher resolutions add cost without new signal for this A/B).
    [Params(4, 5, 6, 7, 8)]
    public int Resolution { get; set; }

    private GeoPolygon _box = null!;

    [GlobalSetup]
    public void Setup()
    {
        _box = new GeoPolygon(BoxExterior);

        // GlobalSetup runs once per parameter value, so this records the stable count and
        // confirms the experimental Center fill matches it exactly for this resolution.
        var stableCount = H3Polygon.ToCells(_box, Resolution).Length;

#pragma warning disable H3NET0001 // ContainmentMode and ToCellsExperimental are experimental (H3NET0001).
        var experimentalCount = H3Polygon.ToCellsExperimental(_box, Resolution, ContainmentMode.Center).Length;
#pragma warning restore H3NET0001

        var countsMatch = stableCount == experimentalCount;
        Console.WriteLine(
            $"[PolygonExperimental] res={Resolution} stable={stableCount} experimental={experimentalCount} match={countsMatch}");
    }

    [BenchmarkCategory("PolygonExperimental")]
    [Benchmark(Baseline = true, Description = "H3.NET.Native ToCells (stable)")]
    public int NativeToCells() => H3Polygon.ToCells(_box, Resolution).Length;

    [BenchmarkCategory("PolygonExperimental")]
    [Benchmark(Description = "H3.NET.Native ToCellsExperimental (Center)")]
    public int NativeToCellsExperimental()
    {
#pragma warning disable H3NET0001 // Experimental polygonToCells API under benchmark (H3NET0001).
        return H3Polygon.ToCellsExperimental(_box, Resolution, ContainmentMode.Center).Length;
#pragma warning restore H3NET0001
    }
}

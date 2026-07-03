// SPDX-License-Identifier: Apache-2.0

using System.Collections.Generic;
using System.Linq;
using CsCheck;
using Xunit;

namespace H3.NET.Native.Tests.Properties;

/// <summary>
/// Grid-traversal invariants for the PR3 surface: ring size and disjointness, path
/// endpoint-inclusiveness and length, distance reflexivity/symmetry, the localIJ
/// round trip, and the gridDiskDistances parallel-array lockstep invariant. Resolutions
/// and k are kept modest for CI speed.
///
/// GridDistance, GridPathCells, and CellToLocalIJ are PARTIAL operations: across
/// pentagon distortion (even from an origin to a cell in its own disk) the native
/// library legitimately fails with a typed <see cref="H3Exception"/>. Those properties
/// therefore guard each partial call and only assert the invariant when the call
/// succeeds; the exact failing-vs-passing boundary is pinned by the differential and
/// unit suites, not sampled here. The set-shaped invariants (ring == disk shell, disk
/// distances cells == grid disk, Into-vs-array equivalence) hold unconditionally because
/// GridRing / GridDisk / GridDiskDistances do not fail on pentagon distortion in 4.5.0.
/// </summary>
public sealed class GridTraversalPropertyTests
{
    private const long Iterations = 120;

    /// <summary>
    /// High-entropy invalid-index generator, mirrored from <see cref="HierarchyPropertyTests"/>
    /// and the inspection suite: almost every random 64-bit value fails IsValidCell, with a
    /// small low-magnitude slice retained.
    /// </summary>
    private static readonly Gen<ulong> InvalidIndexGen =
        Gen.Frequency((9, Gen.ULong), (1, Gen.ULong[1UL, 0xFUL]))
            .Where(v => !new H3Index(v).IsValidCell());

    /// <summary>Returns the grid distance, or null if the native call failed (pentagon distortion).</summary>
    private static long? TryGridDistance(H3Index origin, H3Index other)
    {
        try
        {
            return origin.GridDistance(other);
        }
        catch (H3Exception)
        {
            return null;
        }
    }

    [Fact]
    public void GridRing_K_IsDisjointFromInnerDisk_AndYieldsValidCells()
    {
        var gen = Generators.LatLngGen
            .Select(Generators.Resolution, Gen.Int[0, 4], (p, res, k) => (p, res, k));

        gen.Sample(
            input =>
            {
                var (point, res, k) = input;
                var origin = H3Index.FromLatLng(point, res);
                var ring = origin.GridRing(k);

                Assert.All(ring, c =>
                {
                    Assert.True(c.IsValidCell());
                    Assert.False(c.IsNull);
                });

                // The ring is exactly the shell of the disk: GridRing(k) and GridDisk(k-1)
                // partition GridDisk(k) -- they are disjoint AND their union is the full disk.
                if (k > 0)
                {
                    var ringSet = ring.Select(c => c.Value).ToHashSet();
                    var inner = origin.GridDisk(k - 1).Select(c => c.Value).ToHashSet();
                    var disk = origin.GridDisk(k).Select(c => c.Value).ToHashSet();

                    // Disjoint: no ring cell lies inside the strictly-smaller disk.
                    Assert.All(ringSet, v => Assert.DoesNotContain(v, inner));

                    // Complete: ring united with the inner disk reconstructs the full disk.
                    var union = new HashSet<ulong>(ringSet);
                    union.UnionWith(inner);
                    Assert.Equal(disk, union);
                }
            },
            iter: Iterations);
    }

    [Fact]
    public void GridRingInto_MatchesArrayOverload()
    {
        var gen = Generators.LatLngGen
            .Select(Generators.Resolution, Gen.Int[0, 4], (p, res, k) => (p, res, k));

        gen.Sample(
            input =>
            {
                var (point, res, k) = input;
                var origin = H3Index.FromLatLng(point, res);
                var expected = origin.GridRing(k).Select(c => c.Value).ToHashSet();

                int maxSize = k == 0 ? 1 : 6 * k;
                var destination = new H3Index[maxSize];
                int count = origin.GridRingInto(k, destination);

                var actual = destination.Take(count).Select(c => c.Value).ToHashSet();
                Assert.Equal(expected, actual);
            },
            iter: Iterations);
    }

    [Fact]
    public void GridDistance_IsReflexive_AndSymmetric_WhereDefined()
    {
        var gen = Generators.LatLngGen
            .Select(Generators.Resolution, Gen.Int[1, 4], (p, res, k) => (p, res, k));

        gen.Sample(
            input =>
            {
                var (point, res, k) = input;
                var origin = H3Index.FromLatLng(point, res);

                // Reflexivity always holds: a cell's distance to itself is 0.
                Assert.Equal(0L, origin.GridDistance(origin));

                // Symmetry where defined: across pentagons the distance is undefined and
                // BOTH directions fail, so guarding one side is sufficient.
                foreach (var cell in origin.GridRing(k))
                {
                    long? forward = TryGridDistance(origin, cell);
                    if (forward is null)
                    {
                        continue; // pentagon distortion: distance undefined.
                    }

                    Assert.Equal(forward, cell.GridDistance(origin));
                    Assert.Equal(k, forward);
                }
            },
            iter: Iterations);
    }

    [Fact]
    public void GridPathCells_IsEndpointInclusive_AndLengthMatchesDistance_WhereDefined()
    {
        var gen = Generators.LatLngGen
            .Select(Generators.Resolution, Gen.Int[0, 4], (p, res, k) => (p, res, k));

        gen.Sample(
            input =>
            {
                var (point, res, k) = input;
                var origin = H3Index.FromLatLng(point, res);
                var end = origin.GridRing(k)[0];

                H3Index[] path;
                try
                {
                    path = origin.GridPathCells(end);
                }
                catch (H3Exception)
                {
                    return; // pentagon / antimeridian: no clean path.
                }

                Assert.Equal(origin.Value, path[0].Value);
                Assert.Equal(end.Value, path[^1].Value);

                // Path length is exactly distance + 1 (endpoint-inclusive) where distance
                // is defined; gridPathCellsSize and gridDistance agree on this geometry.
                long? distance = TryGridDistance(origin, end);
                if (distance is not null)
                {
                    Assert.Equal(distance + 1, path.Length);
                }
            },
            iter: Iterations);
    }

    [Fact]
    public void CellToLocalIJ_RoundTrips_WhereDefined()
    {
        var gen = Generators.LatLngGen
            .Select(Generators.Resolution, Gen.Int[0, 3], (p, res, k) => (p, res, k));

        gen.Sample(
            input =>
            {
                var (point, res, k) = input;
                var origin = H3Index.FromLatLng(point, res);

                foreach (var target in origin.GridDisk(k))
                {
                    CoordIJ ij;
                    try
                    {
                        ij = origin.CellToLocalIJ(target);
                    }
                    catch (H3Exception)
                    {
                        continue; // pentagon distortion: local IJ undefined.
                    }

                    // Where the forward map is defined, the inverse must reproduce target.
                    var back = origin.LocalIJToCell(ij);
                    Assert.Equal(target.Value, back.Value);
                }
            },
            iter: Iterations);
    }

    [Fact]
    public void GridDiskDistances_CellsMatchGridDisk_AndDistancesAreInRange()
    {
        var gen = Generators.LatLngGen
            .Select(Generators.Resolution, Gen.Int[0, 4], (p, res, k) => (p, res, k));

        gen.Sample(
            input =>
            {
                var (point, res, k) = input;
                var origin = H3Index.FromLatLng(point, res);

                var (cells, distances) = origin.GridDiskDistances(k);
                Assert.Equal(cells.Length, distances.Length);

                // The cells channel matches GridDisk as a set (both are total operations).
                var diskSet = origin.GridDisk(k).Select(c => c.Value).ToHashSet();
                Assert.Equal(diskSet, cells.Select(c => c.Value).ToHashSet());

                // Every reported distance is in [0, k], and the origin sits at distance 0.
                Assert.All(distances, d => Assert.InRange(d, 0, k));
                for (int i = 0; i < cells.Length; i++)
                {
                    if (cells[i].Value == origin.Value)
                    {
                        Assert.Equal(0, distances[i]);
                    }
                }
            },
            iter: Iterations);
    }

    [Fact]
    public void GridDiskDistancesInto_MatchesArrayOverload()
    {
        var gen = Generators.LatLngGen
            .Select(Generators.Resolution, Gen.Int[0, 4], (p, res, k) => (p, res, k));

        gen.Sample(
            input =>
            {
                var (point, res, k) = input;
                var origin = H3Index.FromLatLng(point, res);

                var (expectedCells, expectedDistances) = origin.GridDiskDistances(k);
                var expected = expectedCells
                    .Zip(expectedDistances, (c, d) => (c.Value, d))
                    .ToHashSet();

                int maxSize = (3 * k * (k + 1)) + 1;
                var cells = new H3Index[maxSize];
                var distances = new int[maxSize];
                int count = origin.GridDiskDistancesInto(k, cells, distances);

                var actual = cells.Take(count)
                    .Zip(distances.Take(count), (c, d) => (c.Value, d))
                    .ToHashSet();
                Assert.Equal(expected, actual);
            },
            iter: Iterations);
    }

    // ---- Junk-origin robustness (no crash on invalid indices) --------------
    //
    // Every member must either throw a typed H3Exception or return gracefully when fed a
    // high-entropy invalid origin -- never a process crash. Confirmed against the libh3
    // 4.5.0 C ABI: saturated indices surface E_CELL_INVALID, while low-digit junk is
    // treated as a res-0 cell and completes. Both arms are acceptable.

    [Fact]
    public void GridRing_OnInvalidIndex_ThrowsTyped_OrReturns()
    {
        InvalidIndexGen.Sample(
            raw =>
            {
                var origin = new H3Index(raw);
                try
                {
                    _ = origin.GridRing(1);
                }
                catch (H3Exception)
                {
                    // Typed, graceful failure is acceptable.
                }
            },
            iter: Iterations);
    }

    [Fact]
    public void GridDiskDistances_OnInvalidIndex_ThrowsTyped_OrReturns()
    {
        InvalidIndexGen.Sample(
            raw =>
            {
                var origin = new H3Index(raw);
                try
                {
                    _ = origin.GridDiskDistances(1);
                }
                catch (H3Exception)
                {
                    // Typed, graceful failure is acceptable.
                }
            },
            iter: Iterations);
    }

    [Fact]
    public void GridDistance_OnInvalidIndex_ThrowsTyped_OrReturns()
    {
        InvalidIndexGen.Sample(
            raw =>
            {
                var origin = new H3Index(raw);
                try
                {
                    _ = origin.GridDistance(origin);
                }
                catch (H3Exception)
                {
                    // Typed, graceful failure is acceptable.
                }
            },
            iter: Iterations);
    }

    [Fact]
    public void GridPathCells_OnInvalidIndex_ThrowsTyped_OrReturns()
    {
        InvalidIndexGen.Sample(
            raw =>
            {
                var origin = new H3Index(raw);
                try
                {
                    _ = origin.GridPathCells(origin);
                }
                catch (H3Exception)
                {
                    // Typed, graceful failure is acceptable.
                }
            },
            iter: Iterations);
    }

    [Fact]
    public void CellToLocalIJ_OnInvalidIndex_ThrowsTyped_OrReturns()
    {
        InvalidIndexGen.Sample(
            raw =>
            {
                var origin = new H3Index(raw);
                try
                {
                    _ = origin.CellToLocalIJ(origin);
                }
                catch (H3Exception)
                {
                    // Typed, graceful failure is acceptable.
                }
            },
            iter: Iterations);
    }
}

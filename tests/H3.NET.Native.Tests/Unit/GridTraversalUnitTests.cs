// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace H3.NET.Native.Tests.Unit;

/// <summary>
/// Per-member unit tests for the PR3 grid-traversal / localIJ surface (GridRing /
/// GridRingInto, GridPathCells / GridPathCellsInto, GridDistance, CellToLocalIJ /
/// LocalIJToCell, GridDiskDistances / GridDiskDistancesInto). Covers the
/// undersized-span ArgumentOutOfRangeException for every *Into overload, the reserved
/// mode=0 hiding on the localIJ round trip, and the typed error channel.
///
/// Error-code notes pinned against libh3 4.5.0 (verified directly against the C ABI):
///   * gridDistance / gridPathCells / cellToLocalIj across DIFFERENT resolutions
///     -&gt; E_RES_MISMATCH (12), which maps to the base H3Exception.
///   * gridDistance / cellToLocalIj across cells too FAR apart (or pentagon-separated)
///     -&gt; E_FAILED (1), which maps to the base H3Exception.
///   * gridRing is the SAFE dispatch in 4.5.0: it never raises on pentagon distortion
///     (verified: 0/4608 pentagon-area cells raise). The XML contract still lists
///     H3PentagonException because the API permits it; this version simply never
///     exercises that arm, so no "throws on pentagon" assertion is made.
/// </summary>
public sealed class GridTraversalUnitTests
{
    private static readonly LatLng SamplePoint = new(37.775938728915946, -122.41795063018799);

    private static H3Index SampleCell(int res) => H3Index.FromLatLng(SamplePoint, res);

    // ---- GridRing ----------------------------------------------------------

    [Fact]
    public void GridRing_K0_IsExactlyTheOrigin()
    {
        var origin = SampleCell(9);
        var ring = origin.GridRing(0);
        Assert.Single(ring);
        Assert.Equal(origin.Value, ring[0].Value);
    }

    [Fact]
    public void GridRing_NegativeK_ThrowsArgumentOutOfRange()
    {
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() => SampleCell(9).GridRing(-1));
        Assert.Equal("k", ex.ParamName);
    }

    [Fact]
    public void GridRingInto_TooSmallDestination_ThrowsArgumentOutOfRange()
    {
        var origin = SampleCell(9);
        // maxGridRingSize(2) == 12; an 11-slot span is one short.
        var destination = new H3Index[11];
        var ex = Assert.Throws<ArgumentOutOfRangeException>(
            () => origin.GridRingInto(2, destination));
        Assert.Equal("destination", ex.ParamName);
    }

    [Fact]
    public void GridRingInto_PreClearsStaleData_StripsToRealCells()
    {
        var origin = SampleCell(9);
        const int k = 2;
        var expected = origin.GridRing(k).Select(c => c.Value).ToHashSet();

        // Oversized span seeded with stale non-zero data: the pre-clear + strip must
        // ignore the stale tail and report only the real ring cells.
        var destination = new H3Index[64];
        Array.Fill(destination, new H3Index(0xdeadbeefUL));
        int count = origin.GridRingInto(k, destination);

        var actual = destination.Take(count).Select(c => c.Value).ToHashSet();
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void GridRing_KExceedingArrayMaxLength_ThrowsArgumentOutOfRange()
    {
        // maxGridRingSize(k) == 6k returns E_SUCCESS for any k >= 0; at k this large the
        // 6k size (~2.4e9) exceeds Array.MaxLength, so the managed guard rejects it before
        // allocating rather than overflowing.
        const int k = 400_000_000;
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() => SampleCell(9).GridRing(k));
        Assert.Equal("k", ex.ParamName);
    }

    [Fact]
    public void GridRingInto_KExceedingArrayMaxLength_ThrowsArgumentOutOfRange()
    {
        // The k-overflow guard precedes the destination-length guard, so an empty
        // destination still surfaces the k overflow.
        const int k = 400_000_000;
        var ex = Assert.Throws<ArgumentOutOfRangeException>(
            () => SampleCell(9).GridRingInto(k, Span<H3Index>.Empty));
        Assert.Equal("k", ex.ParamName);
    }

    // ---- GridPathCells -----------------------------------------------------

    [Fact]
    public void GridPathCells_SameCell_IsSingletonPath()
    {
        var cell = SampleCell(9);
        var path = cell.GridPathCells(cell);
        Assert.Single(path);
        Assert.Equal(cell.Value, path[0].Value);
    }

    [Fact]
    public void GridPathCells_EndpointInclusive_Ordered()
    {
        var start = SampleCell(9);
        var end = start.GridRing(3)[0];
        var path = start.GridPathCells(end);

        Assert.Equal(start.Value, path[0].Value);
        Assert.Equal(end.Value, path[^1].Value);
    }

    [Fact]
    public void GridPathCells_DifferentResolutions_ThrowsH3Exception_ResMismatch()
    {
        var a = SampleCell(5);
        var b = SampleCell(7);
        var ex = Assert.Throws<H3Exception>(() => a.GridPathCells(b));
        Assert.Equal(12u, ex.ErrorCode); // E_RES_MISMATCH
    }

    [Fact]
    public void GridPathCellsInto_TooSmallDestination_ThrowsArgumentOutOfRange()
    {
        var start = SampleCell(9);
        var end = start.GridRing(2)[0];
        long size = H3Index.GridPathCellsSize(start, end);
        var destination = new H3Index[size - 1];

        var ex = Assert.Throws<ArgumentOutOfRangeException>(
            () => start.GridPathCellsInto(end, destination));
        Assert.Equal("result", ex.ParamName);
    }

    [Fact]
    public void GridPathCellsInto_MatchesArrayOverload_ExactSize()
    {
        var start = SampleCell(9);
        var end = start.GridRing(3)[0];
        var expected = start.GridPathCells(end).Select(c => c.Value).ToArray();

        var destination = new H3Index[expected.Length];
        int count = start.GridPathCellsInto(end, destination);

        Assert.Equal(expected.Length, count);
        Assert.Equal(expected, destination.Take(count).Select(c => c.Value).ToArray());
    }

    // ---- GridDistance ------------------------------------------------------

    [Fact]
    public void GridDistance_ToSelf_IsZero()
    {
        var cell = SampleCell(9);
        Assert.Equal(0L, cell.GridDistance(cell));
    }

    [Fact]
    public void GridDistance_IsSymmetric()
    {
        var a = SampleCell(9);
        var b = a.GridRing(3)[0];
        Assert.Equal(a.GridDistance(b), b.GridDistance(a));
    }

    [Fact]
    public void GridDistance_OnRing_EqualsRingRadius()
    {
        var origin = SampleCell(9);
        const int k = 3;
        foreach (var cell in origin.GridRing(k))
        {
            Assert.Equal(k, origin.GridDistance(cell));
        }
    }

    [Fact]
    public void GridDistance_DifferentResolutions_ThrowsH3Exception_ResMismatch()
    {
        var a = SampleCell(5);
        var b = SampleCell(7);
        var ex = Assert.Throws<H3Exception>(() => a.GridDistance(b));
        Assert.Equal(12u, ex.ErrorCode); // E_RES_MISMATCH
    }

    [Fact]
    public void GridDistance_FarApart_ThrowsH3Exception_Failed()
    {
        // Two res-9 cells on opposite sides of the globe exceed the local-IJ window
        // the distance algorithm can resolve -> E_FAILED (1).
        var a = H3Index.FromLatLng(new LatLng(0.0, 0.0), 9);
        var b = H3Index.FromLatLng(new LatLng(40.0, 40.0), 9);
        var ex = Assert.Throws<H3Exception>(() => a.GridDistance(b));
        Assert.Equal(1u, ex.ErrorCode); // E_FAILED
    }

    // ---- CellToLocalIJ / LocalIJToCell -------------------------------------

    [Fact]
    public void CellToLocalIJ_Self_RoundTrips_ThroughHiddenMode0()
    {
        // The reserved mode arg is hidden and always passed as 0; the self round trip
        // must reproduce the origin.
        var origin = SampleCell(9);
        var ij = origin.CellToLocalIJ(origin);
        var back = origin.LocalIJToCell(ij);
        Assert.Equal(origin.Value, back.Value);
    }

    [Fact]
    public void CellToLocalIJ_Neighbors_RoundTrip()
    {
        var origin = SampleCell(9);
        foreach (var target in origin.GridRing(2))
        {
            var ij = origin.CellToLocalIJ(target);
            var back = origin.LocalIJToCell(ij);
            Assert.Equal(target.Value, back.Value);
        }
    }

    [Fact]
    public void CellToLocalIJ_DifferentResolutions_ThrowsH3Exception_ResMismatch()
    {
        var a = SampleCell(5);
        var b = SampleCell(7);
        var ex = Assert.Throws<H3Exception>(() => a.CellToLocalIJ(b));
        Assert.Equal(12u, ex.ErrorCode); // E_RES_MISMATCH
    }

    [Fact]
    public void CellToLocalIJ_FarApart_ThrowsH3Exception_Failed()
    {
        var a = H3Index.FromLatLng(new LatLng(0.0, 0.0), 9);
        var b = H3Index.FromLatLng(new LatLng(40.0, 40.0), 9);
        var ex = Assert.Throws<H3Exception>(() => a.CellToLocalIJ(b));
        Assert.Equal(1u, ex.ErrorCode); // E_FAILED
    }

    // ---- GridDiskDistances -------------------------------------------------

    [Fact]
    public void GridDiskDistances_K0_IsOriginAtDistanceZero()
    {
        var origin = SampleCell(9);
        var (cells, distances) = origin.GridDiskDistances(0);
        Assert.Single(cells);
        Assert.Equal(origin.Value, cells[0].Value);
        Assert.Single(distances);
        Assert.Equal(0, distances[0]);
    }

    [Fact]
    public void GridDiskDistances_NegativeK_ThrowsArgumentOutOfRange()
    {
        var ex = Assert.Throws<ArgumentOutOfRangeException>(
            () => SampleCell(9).GridDiskDistances(-1));
        Assert.Equal("k", ex.ParamName);
    }

    [Fact]
    public void GridDiskDistances_KExceedingArrayMaxLength_ThrowsArgumentOutOfRange()
    {
        // maxGridDiskSize(30000) == 2_700_090_001 exceeds Array.MaxLength; the managed
        // guard rejects it before allocating the parallel cell/distance buffers.
        const int k = 30_000;
        var ex = Assert.Throws<ArgumentOutOfRangeException>(
            () => SampleCell(9).GridDiskDistances(k));
        Assert.Equal("k", ex.ParamName);
    }

    [Fact]
    public void GridDiskDistances_ParallelArrays_AreLockstep()
    {
        // Invariant: cells[i]'s grid distance from the origin equals distances[i].
        var origin = SampleCell(9);
        var (cells, distances) = origin.GridDiskDistances(3);
        Assert.Equal(cells.Length, distances.Length);
        for (int i = 0; i < cells.Length; i++)
        {
            Assert.Equal(distances[i], (int)origin.GridDistance(cells[i]));
        }
    }

    [Fact]
    public void GridDiskDistancesInto_TooSmallCells_ThrowsArgumentOutOfRange()
    {
        var origin = SampleCell(9);
        const int k = 2;
        int maxSize = (3 * k * (k + 1)) + 1;
        var cells = new H3Index[maxSize - 1];
        var distances = new int[maxSize];

        var ex = Assert.Throws<ArgumentOutOfRangeException>(
            () => origin.GridDiskDistancesInto(k, cells, distances));
        Assert.Equal("cells", ex.ParamName);
    }

    [Fact]
    public void GridDiskDistancesInto_TooSmallDistances_ThrowsArgumentOutOfRange()
    {
        var origin = SampleCell(9);
        const int k = 2;
        int maxSize = (3 * k * (k + 1)) + 1;
        var cells = new H3Index[maxSize];
        var distances = new int[maxSize - 1];

        var ex = Assert.Throws<ArgumentOutOfRangeException>(
            () => origin.GridDiskDistancesInto(k, cells, distances));
        Assert.Equal("distances", ex.ParamName);
    }

    [Fact]
    public void GridDiskDistancesInto_KExceedingArrayMaxLength_ThrowsArgumentOutOfRange()
    {
        // The k-overflow guard precedes both span-length guards, so empty spans still
        // surface the k overflow rather than a cells/distances length error.
        const int k = 30_000;
        var ex = Assert.Throws<ArgumentOutOfRangeException>(
            () => SampleCell(9).GridDiskDistancesInto(k, Span<H3Index>.Empty, Span<int>.Empty));
        Assert.Equal("k", ex.ParamName);
    }

    [Fact]
    public void GridDiskDistancesInto_PreClearsStaleData_StripsLockstep()
    {
        var origin = SampleCell(9);
        const int k = 2;
        var (expectedCells, expectedDistances) = origin.GridDiskDistances(k);
        var expected = expectedCells
            .Zip(expectedDistances, (c, d) => (c.Value, d))
            .ToHashSet();

        // Oversized spans seeded with stale data: the cells pre-clear + lockstep strip
        // must ignore the stale tail and pair each surviving cell with its distance.
        var cells = new H3Index[64];
        var distances = new int[64];
        Array.Fill(cells, new H3Index(0xdeadbeefUL));
        Array.Fill(distances, -999);
        int count = origin.GridDiskDistancesInto(k, cells, distances);

        var actual = cells.Take(count)
            .Zip(distances.Take(count), (c, d) => (c.Value, d))
            .ToHashSet();
        Assert.Equal(expected, actual);
    }

    // ---- Null sentinel + junk origins --------------------------------------
    //
    // None of these members may segfault on a bogus origin: the binding must either
    // surface a typed H3Exception or return gracefully. Verified directly against the
    // libh3 4.5.0 C ABI (see scratchpad probe):
    //   * H3_NULL(0), 0x1, 0xdeadbeef -> E_SUCCESS (0): libh3 treats them as low-digit
    //     res-0 cells, so the call completes without throwing (self-distance 0, etc.).
    //   * 0xffffffffffffffff, 0x7fffffffffffffff -> E_CELL_INVALID (5): typed H3Exception.
    // Either arm is acceptable; the load-bearing contract is "no crash, typed-or-graceful".

    // A small set of hand-picked invalid raw indices (mirrors IndexInspectionUnitTests).
    private static readonly ulong[] JunkOrigins =
    [
        0x0UL,                  // H3_NULL sentinel.
        0xffffffffffffffffUL,   // all bits set -> E_CELL_INVALID.
        0x1UL,                  // tiny non-cell.
        0xdeadbeefUL,           // arbitrary junk.
        0x7fffffffffffffffUL,   // high bit clear, otherwise saturated -> E_CELL_INVALID.
    ];

    public static IEnumerable<object[]> JunkOriginCases() =>
        JunkOrigins.Select(v => new object[] { v });

    [Theory]
    [MemberData(nameof(JunkOriginCases))]
    public void GridRing_OnJunkOrigin_ThrowsTyped_OrReturns(ulong raw)
    {
        var origin = new H3Index(raw);
        try
        {
            _ = origin.GridRing(1);
        }
        catch (H3Exception)
        {
            // Typed, graceful failure is acceptable (E_CELL_INVALID for saturated indices).
        }
    }

    [Theory]
    [MemberData(nameof(JunkOriginCases))]
    public void GridDiskDistances_OnJunkOrigin_ThrowsTyped_OrReturns(ulong raw)
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
    }

    [Theory]
    [MemberData(nameof(JunkOriginCases))]
    public void GridDistance_OnJunkOrigin_ThrowsTyped_OrReturns(ulong raw)
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
    }

    [Theory]
    [MemberData(nameof(JunkOriginCases))]
    public void GridPathCells_OnJunkOrigin_ThrowsTyped_OrReturns(ulong raw)
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
    }

    [Theory]
    [MemberData(nameof(JunkOriginCases))]
    public void CellToLocalIJ_OnJunkOrigin_ThrowsTyped_OrReturns(ulong raw)
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
    }

    [Theory]
    [MemberData(nameof(JunkOriginCases))]
    public void LocalIJToCell_OnJunkOrigin_ThrowsTyped_OrReturns(ulong raw)
    {
        var origin = new H3Index(raw);
        try
        {
            _ = origin.LocalIJToCell(new CoordIJ(0, 0));
        }
        catch (H3Exception)
        {
            // Typed, graceful failure is acceptable.
        }
    }
}

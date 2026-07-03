// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.Linq;
using H3.NET.Native.Interop;
using H3.NET.Native.Tests.Fixtures;
using Xunit;

namespace H3.NET.Native.Tests.Unit;

/// <summary>
/// Per-member unit tests for the PR6 measures surface, mirroring VertexUnitTests. Covers
/// the H3Index cell-area trio (CellAreaRads2 / Km2 / M2), the static average-hexagon
/// measures (GetHexagonAreaAvgKm2 / M2, GetHexagonEdgeLengthAvgKm / M), GetNumCells, the
/// constant-count collection fills (GetRes0Cells / Into, GetPentagons / Into), the
/// H3DirectedEdge edge-length trio (EdgeLengthRads / Km / M), and the LatLng static
/// great-circle distances plus the public DegsToRads / RadsToDegs helpers.
///
/// Each member is exercised on its happy path plus every documented guard / error arm.
/// Error codes pinned against libh3 4.5.0 (the binding faithfully surfaces the raw native
/// channel, or the validate-first guard pins the code itself): an invalid cell handed to a
/// cell-area method -&gt; H3InvalidIndexException with E_CELL_INVALID (5) raised by the
/// EnsureValidCell guard; an out-of-range resolution -&gt; H3DomainException with
/// E_RES_DOMAIN (4); an invalid directed edge handed to an edge-length method -&gt;
/// H3InvalidIndexException with E_DIR_EDGE_INVALID (6) raised by the EnsureValid guard.
/// The bare-double GreatCircleDistance* and DegsToRads / RadsToDegs never throw.
/// </summary>
public sealed class MeasuresUnitTests
{
    // Pinned libh3 4.5.0 error codes.
    private const uint CellInvalidErrorCode = 5;     // E_CELL_INVALID.
    private const uint ResDomainErrorCode = 4;       // E_RES_DOMAIN.
    private const uint DirEdgeInvalidErrorCode = 6;  // E_DIR_EDGE_INVALID.

    private const int Res0CellCount = 122;
    private const int PentagonCount = 12;
    private const ulong InvalidCell = 0xffffffffffffffffUL;

    private static readonly LatLng SamplePoint = new(37.775938728915946, -122.41795063018799);
    private static readonly LatLng SanFrancisco = new(37.7749, -122.4194);
    private static readonly LatLng NewYork = new(40.7128, -74.0060);

    private static H3Index SampleCell(int res) => H3Index.FromLatLng(SamplePoint, res);

    // A concrete res-9 San Francisco cell and one of its real directed edges.
    private static H3Index SfCell => H3Index.FromLatLng(SanFrancisco, 9);

    private static H3Index PentagonCell =>
        H3Index.Parse(FixtureLoader.LoadPentagons().First(p => p.Res == 9).Cell);

    private static H3DirectedEdge RealEdge
    {
        get
        {
            var cell = SfCell;
            var neighbor = cell.GridRing(1).First();
            return cell.DirectedEdgeTo(neighbor);
        }
    }

    // ---- (1) Cell area -----------------------------------------------------

    [Fact]
    public void CellAreaRads2_OnKnownCell_IsFinitePositive()
    {
        double area = SfCell.CellAreaRads2();
        Assert.True(double.IsFinite(area));
        Assert.True(area > 0.0);
    }

    [Fact]
    public void CellAreaKm2_OnKnownCell_IsFinitePositive()
    {
        double area = SfCell.CellAreaKm2();
        Assert.True(double.IsFinite(area));
        Assert.True(area > 0.0);
    }

    [Fact]
    public void CellAreaM2_OnKnownCell_IsFinitePositive()
    {
        double area = SfCell.CellAreaM2();
        Assert.True(double.IsFinite(area));
        Assert.True(area > 0.0);
    }

    [Fact]
    public void CellAreaM2_ApproximatelyKm2TimesMillion()
    {
        var cell = SfCell;
        double km2 = cell.CellAreaKm2();
        double m2 = cell.CellAreaM2();

        // 1 km^2 == 1e6 m^2. Relative tolerance absorbs floating-point noise.
        Assert.Equal(km2 * 1e6, m2, m2 * 1e-9);
    }

    [Fact]
    public void CellArea_OnPentagon_Succeeds()
    {
        // Pentagons are valid cells; area must be finite and positive, not throw.
        var pentagon = PentagonCell;
        Assert.True(pentagon.IsPentagon);
        Assert.True(pentagon.CellAreaRads2() > 0.0);
        Assert.True(pentagon.CellAreaKm2() > 0.0);
        Assert.True(pentagon.CellAreaM2() > 0.0);
    }

    [Fact]
    public void CellAreaRads2_OnInvalidCell_ThrowsH3InvalidCell_CellInvalid()
    {
        var ex = Assert.Throws<H3InvalidIndexException>(() => new H3Index(InvalidCell).CellAreaRads2());
        Assert.Equal(CellInvalidErrorCode, ex.ErrorCode);
    }

    [Fact]
    public void CellAreaKm2_OnInvalidCell_ThrowsH3InvalidCell_CellInvalid()
    {
        var ex = Assert.Throws<H3InvalidIndexException>(() => new H3Index(InvalidCell).CellAreaKm2());
        Assert.Equal(CellInvalidErrorCode, ex.ErrorCode);
    }

    [Fact]
    public void CellAreaM2_OnInvalidCell_ThrowsH3InvalidCell_CellInvalid()
    {
        var ex = Assert.Throws<H3InvalidIndexException>(() => new H3Index(InvalidCell).CellAreaM2());
        Assert.Equal(CellInvalidErrorCode, ex.ErrorCode);
    }

    [Fact]
    public void CellArea_OnNull_ThrowsH3InvalidCell()
    {
        Assert.Throws<H3InvalidIndexException>(() => H3Index.Null.CellAreaRads2());
        Assert.Throws<H3InvalidIndexException>(() => H3Index.Null.CellAreaKm2());
        Assert.Throws<H3InvalidIndexException>(() => H3Index.Null.CellAreaM2());
    }

    // ---- (2) Average hexagon area / edge length ----------------------------

    public static IEnumerable<object[]> AllResolutions() =>
        Enumerable.Range(0, 16).Select(r => new object[] { r });

    [Theory]
    [MemberData(nameof(AllResolutions))]
    public void GetHexagonAreaAvg_Resolution_IsFinitePositive(int resolution)
    {
        Assert.True(H3Index.GetHexagonAreaAvgKm2(resolution) > 0.0);
        Assert.True(H3Index.GetHexagonAreaAvgM2(resolution) > 0.0);
    }

    [Theory]
    [MemberData(nameof(AllResolutions))]
    public void GetHexagonEdgeLengthAvg_Resolution_IsFinitePositive(int resolution)
    {
        Assert.True(H3Index.GetHexagonEdgeLengthAvgKm(resolution) > 0.0);
        Assert.True(H3Index.GetHexagonEdgeLengthAvgM(resolution) > 0.0);
    }

    [Fact]
    public void GetHexagonAreaAvgKm2_IsMonotonicDecreasing()
    {
        for (int res = 1; res < 16; res++)
        {
            Assert.True(
                H3Index.GetHexagonAreaAvgKm2(res) < H3Index.GetHexagonAreaAvgKm2(res - 1),
                $"area at res {res} must be smaller than at res {res - 1}.");
        }
    }

    [Fact]
    public void GetHexagonEdgeLengthAvgKm_IsMonotonicDecreasing()
    {
        for (int res = 1; res < 16; res++)
        {
            Assert.True(
                H3Index.GetHexagonEdgeLengthAvgKm(res) < H3Index.GetHexagonEdgeLengthAvgKm(res - 1),
                $"edge length at res {res} must be smaller than at res {res - 1}.");
        }
    }

    [Theory]
    [InlineData(16)]
    [InlineData(-1)]
    public void GetHexagonAreaAvgKm2_OutOfRangeResolution_ThrowsH3Domain(int resolution)
    {
        var ex = Assert.Throws<H3DomainException>(() => H3Index.GetHexagonAreaAvgKm2(resolution));
        Assert.Equal(ResDomainErrorCode, ex.ErrorCode);
    }

    [Theory]
    [InlineData(16)]
    [InlineData(-1)]
    public void GetHexagonAreaAvgM2_OutOfRangeResolution_ThrowsH3Domain(int resolution)
    {
        var ex = Assert.Throws<H3DomainException>(() => H3Index.GetHexagonAreaAvgM2(resolution));
        Assert.Equal(ResDomainErrorCode, ex.ErrorCode);
    }

    [Theory]
    [InlineData(16)]
    [InlineData(-1)]
    public void GetHexagonEdgeLengthAvgKm_OutOfRangeResolution_ThrowsH3Domain(int resolution)
    {
        var ex = Assert.Throws<H3DomainException>(() => H3Index.GetHexagonEdgeLengthAvgKm(resolution));
        Assert.Equal(ResDomainErrorCode, ex.ErrorCode);
    }

    [Theory]
    [InlineData(16)]
    [InlineData(-1)]
    public void GetHexagonEdgeLengthAvgM_OutOfRangeResolution_ThrowsH3Domain(int resolution)
    {
        var ex = Assert.Throws<H3DomainException>(() => H3Index.GetHexagonEdgeLengthAvgM(resolution));
        Assert.Equal(ResDomainErrorCode, ex.ErrorCode);
    }

    // ---- (3) GetNumCells ---------------------------------------------------

    [Fact]
    public void GetNumCells_Res0_Is122()
    {
        Assert.Equal(122L, H3Index.GetNumCells(0));
    }

    [Fact]
    public void GetNumCells_Res15_IsPinnedValue()
    {
        // 2 + 120 * 7^15: the total cell count at the finest resolution.
        Assert.Equal(569707381193162L, H3Index.GetNumCells(15));
    }

    [Theory]
    [MemberData(nameof(AllResolutions))]
    public void GetNumCells_AllResolutions_IsStrictlyIncreasing(int resolution)
    {
        long count = H3Index.GetNumCells(resolution);
        Assert.True(count >= 122L);
        if (resolution > 0)
        {
            Assert.True(count > H3Index.GetNumCells(resolution - 1));
        }
    }

    [Theory]
    [InlineData(16)]
    [InlineData(-1)]
    public void GetNumCells_OutOfRangeResolution_ThrowsH3Domain(int resolution)
    {
        var ex = Assert.Throws<H3DomainException>(() => H3Index.GetNumCells(resolution));
        Assert.Equal(ResDomainErrorCode, ex.ErrorCode);
    }

    // ---- (4) Edge length ---------------------------------------------------

    [Fact]
    public void EdgeLengthRads_OnRealEdge_IsFinitePositive()
    {
        double length = RealEdge.EdgeLengthRads();
        Assert.True(double.IsFinite(length));
        Assert.True(length > 0.0);
    }

    [Fact]
    public void EdgeLengthKm_OnRealEdge_IsFinitePositive()
    {
        double length = RealEdge.EdgeLengthKm();
        Assert.True(double.IsFinite(length));
        Assert.True(length > 0.0);
    }

    [Fact]
    public void EdgeLengthM_OnRealEdge_IsFinitePositive()
    {
        double length = RealEdge.EdgeLengthM();
        Assert.True(double.IsFinite(length));
        Assert.True(length > 0.0);
    }

    [Fact]
    public void EdgeLengthM_ApproximatelyKmTimesThousand()
    {
        var edge = RealEdge;
        double km = edge.EdgeLengthKm();
        double m = edge.EdgeLengthM();

        // 1 km == 1000 m.
        Assert.Equal(km * 1000.0, m, m * 1e-9);
    }

    [Fact]
    public void EdgeLengthRads_OnJunkEdge_ThrowsH3InvalidCell_DirEdgeInvalid()
    {
        // 0xdeadbeef is not a directed edge; the EnsureValid guard pins E_DIR_EDGE_INVALID.
        var ex = Assert.Throws<H3InvalidIndexException>(() => new H3DirectedEdge(0xdeadbeefUL).EdgeLengthRads());
        Assert.Equal(DirEdgeInvalidErrorCode, ex.ErrorCode);
    }

    [Fact]
    public void EdgeLengthKm_OnCellValueAsEdge_ThrowsH3InvalidCell_DirEdgeInvalid()
    {
        // A valid CELL value is not a valid directed-edge value (wrong mode bits).
        var ex = Assert.Throws<H3InvalidIndexException>(() => new H3DirectedEdge(SfCell.Value).EdgeLengthKm());
        Assert.Equal(DirEdgeInvalidErrorCode, ex.ErrorCode);
    }

    [Fact]
    public void EdgeLengthM_OnCellValueAsEdge_ThrowsH3InvalidCell_DirEdgeInvalid()
    {
        var ex = Assert.Throws<H3InvalidIndexException>(() => new H3DirectedEdge(SfCell.Value).EdgeLengthM());
        Assert.Equal(DirEdgeInvalidErrorCode, ex.ErrorCode);
    }

    [Fact]
    public void EdgeLength_OnNull_ThrowsH3InvalidCell()
    {
        Assert.Throws<H3InvalidIndexException>(() => H3DirectedEdge.Null.EdgeLengthRads());
        Assert.Throws<H3InvalidIndexException>(() => H3DirectedEdge.Null.EdgeLengthKm());
        Assert.Throws<H3InvalidIndexException>(() => H3DirectedEdge.Null.EdgeLengthM());
    }

    private static readonly ulong[] JunkEdges =
    [
        0x0UL,
        0x1UL,
        0xffffffffffffffffUL,
        0x7fffffffffffffffUL,
        0xdeadbeefUL,
    ];

    public static IEnumerable<object[]> JunkEdgeCases() =>
        JunkEdges.Select(v => new object[] { v });

    [Theory]
    [MemberData(nameof(JunkEdgeCases))]
    public void EdgeLength_OnJunkValue_ThrowsH3Exception_NeverCrashes(ulong raw)
    {
        var edge = new H3DirectedEdge(raw);
        Assert.False(edge.IsValid());

        TryThrowOrReturn(() => edge.EdgeLengthRads());
        TryThrowOrReturn(() => edge.EdgeLengthKm());
        TryThrowOrReturn(() => edge.EdgeLengthM());
    }

    // ---- (5) Great-circle distance -----------------------------------------

    [Fact]
    public void GreatCircleDistance_IdenticalPoints_IsZero()
    {
        Assert.Equal(0.0, LatLng.GreatCircleDistanceRads(SanFrancisco, SanFrancisco), 1e-12);
        Assert.Equal(0.0, LatLng.GreatCircleDistanceKm(SanFrancisco, SanFrancisco), 1e-9);
        Assert.Equal(0.0, LatLng.GreatCircleDistanceM(SanFrancisco, SanFrancisco), 1e-6);
    }

    [Fact]
    public void GreatCircleDistanceKm_SfToNyc_IsApproximately4129Km()
    {
        // Catches the degrees->radians staging bug: if the public degrees were passed to
        // native (which expects radians) without conversion, this would be wildly wrong.
        double km = LatLng.GreatCircleDistanceKm(SanFrancisco, NewYork);
        Assert.Equal(4129.0, km, 1.0);
    }

    [Fact]
    public void GreatCircleDistance_IsSymmetric()
    {
        Assert.Equal(
            LatLng.GreatCircleDistanceKm(SanFrancisco, NewYork),
            LatLng.GreatCircleDistanceKm(NewYork, SanFrancisco),
            1e-9);
        Assert.Equal(
            LatLng.GreatCircleDistanceRads(SanFrancisco, NewYork),
            LatLng.GreatCircleDistanceRads(NewYork, SanFrancisco),
            1e-12);
    }

    [Fact]
    public void GreatCircleDistanceM_ApproximatelyKmTimesThousand()
    {
        double km = LatLng.GreatCircleDistanceKm(SanFrancisco, NewYork);
        double m = LatLng.GreatCircleDistanceM(SanFrancisco, NewYork);
        Assert.Equal(km * 1000.0, m, m * 1e-9);
    }

    [Fact]
    public void GreatCircleDistance_OutOfRangeLatLng_DoesNotThrow()
    {
        // The bare-double distance methods never validate or throw, even for nonsensical
        // coordinates outside the canonical degree ranges.
        var a = new LatLng(1000.0, 5000.0);
        var b = new LatLng(-9999.0, 12345.0);

        var ex = Record.Exception(() => LatLng.GreatCircleDistanceKm(a, b));
        Assert.Null(ex);
    }

    [Fact]
    public void GreatCircleDistanceRads_QuarterCircle_IsApproximatelyHalfPi()
    {
        // Two points 90 degrees apart on the equator subtend a quarter of the great
        // circle: pi/2 radians. This is the load-bearing degrees-input check -- a missing
        // degrees->radians conversion would yield a nonsensical value.
        var equatorOrigin = new LatLng(0.0, 0.0);
        var equatorNinety = new LatLng(0.0, 90.0);

        double rads = LatLng.GreatCircleDistanceRads(equatorOrigin, equatorNinety);
        Assert.Equal(Math.PI / 2.0, rads, 1e-9);
    }

    // ---- (6) DegsToRads / RadsToDegs (public) ------------------------------

    [Theory]
    [InlineData(0.0)]
    [InlineData(1.0)]
    [InlineData(-37.7749)]
    [InlineData(122.4194)]
    [InlineData(180.0)]
    public void RadsToDegs_DegsToRads_RoundTrips(double degrees)
    {
        double roundTripped = LatLng.RadsToDegs(LatLng.DegsToRads(degrees));
        Assert.Equal(degrees, roundTripped, 1e-9);
    }

    [Fact]
    public void DegsToRads_180_IsPi()
    {
        Assert.Equal(Math.PI, LatLng.DegsToRads(180.0), 1e-12);
    }

    [Fact]
    public void DegsToRads_90_IsHalfPi()
    {
        Assert.Equal(Math.PI / 2.0, LatLng.DegsToRads(90.0), 1e-12);
    }

    [Fact]
    public void RadsToDegs_Pi_Is180()
    {
        Assert.Equal(180.0, LatLng.RadsToDegs(Math.PI), 1e-12);
    }

    // The public helpers forward straight to native; parity pins that no managed
    // transformation slips in between the wrapper and libh3's degsToRads / radsToDegs.
    [Theory]
    [InlineData(0.0)]
    [InlineData(45.0)]
    [InlineData(-90.0)]
    [InlineData(180.0)]
    [InlineData(360.0)]
    public void DegsToRads_MatchesNative(double degrees)
    {
        Assert.Equal(NativeMethods.DegsToRads(degrees), LatLng.DegsToRads(degrees));
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(1.0)]
    [InlineData(-3.14159)]
    [InlineData(6.28318)]
    public void RadsToDegs_MatchesNative(double radians)
    {
        Assert.Equal(NativeMethods.RadsToDegs(radians), LatLng.RadsToDegs(radians));
    }

    // ---- (7) GetRes0Cells / GetRes0CellsInto -------------------------------

    [Fact]
    public void GetRes0Cells_Returns122ValidRes0Cells()
    {
        var cells = H3Index.GetRes0Cells();
        Assert.Equal(Res0CellCount, cells.Length);
        Assert.All(cells, c =>
        {
            Assert.True(c.IsValidCell());
            Assert.Equal(0, c.Resolution);
        });
    }

    [Fact]
    public void GetRes0CellsInto_ExactSpan_Writes122_MatchesArray()
    {
        var expected = H3Index.GetRes0Cells();

        var destination = new H3Index[Res0CellCount];
        int count = H3Index.GetRes0CellsInto(destination);

        Assert.Equal(Res0CellCount, count);
        Assert.Equal(expected, destination);
    }

    [Fact]
    public void GetRes0CellsInto_TooSmallSpan_ThrowsArgumentException_Destination()
    {
        var destination = new H3Index[Res0CellCount - 1];
        var ex = Assert.Throws<ArgumentException>(() => H3Index.GetRes0CellsInto(destination));
        Assert.Equal("destination", ex.ParamName);
    }

    [Fact]
    public void GetRes0CellsInto_OversizedJunkSeeded_StillWrites122Correct()
    {
        var expected = H3Index.GetRes0Cells();

        // Oversize the span and seed it with junk; the first 122 entries must be the
        // correct res-0 cells regardless of the trailing stale data.
        var destination = new H3Index[Res0CellCount + 8];
        Array.Fill(destination, new H3Index(0xdeadbeefUL));

        int count = H3Index.GetRes0CellsInto(destination);

        Assert.Equal(Res0CellCount, count);
        Assert.Equal(expected, destination[..count]);
    }

    // ---- (8) GetPentagons / GetPentagonsInto -------------------------------

    [Theory]
    [MemberData(nameof(AllResolutions))]
    public void GetPentagons_ReturnsTwelvePentagonsAtResolution(int resolution)
    {
        var pentagons = H3Index.GetPentagons(resolution);
        Assert.Equal(PentagonCount, pentagons.Length);
        Assert.All(pentagons, p =>
        {
            Assert.True(p.IsPentagon);
            Assert.Equal(resolution, p.Resolution);
        });
    }

    [Theory]
    [MemberData(nameof(AllResolutions))]
    public void GetPentagonsInto_ExactSpan_Writes12_MatchesArray(int resolution)
    {
        var expected = H3Index.GetPentagons(resolution);

        var destination = new H3Index[PentagonCount];
        int count = H3Index.GetPentagonsInto(resolution, destination);

        Assert.Equal(PentagonCount, count);
        Assert.Equal(expected, destination);
    }

    [Theory]
    [InlineData(16)]
    [InlineData(-1)]
    public void GetPentagons_OutOfRangeResolution_ThrowsH3Domain(int resolution)
    {
        var ex = Assert.Throws<H3DomainException>(() => H3Index.GetPentagons(resolution));
        Assert.Equal(ResDomainErrorCode, ex.ErrorCode);
    }

    [Fact]
    public void GetPentagonsInto_TooSmallSpan_ThrowsArgumentException_Destination()
    {
        var destination = new H3Index[PentagonCount - 1];
        var ex = Assert.Throws<ArgumentException>(() => H3Index.GetPentagonsInto(0, destination));
        Assert.Equal("destination", ex.ParamName);
    }

    [Fact]
    public void GetPentagonsInto_OutOfRangeResolution_ThrowsH3Domain_AfterArgCheck()
    {
        // A valid-length span plus an out-of-range resolution still surfaces the domain
        // error (the arg check passes, the native resolution guard fires).
        var ex = Assert.Throws<H3DomainException>(
            () => H3Index.GetPentagonsInto(16, new H3Index[PentagonCount]));
        Assert.Equal(ResDomainErrorCode, ex.ErrorCode);
    }

    private static void TryThrowOrReturn(Action action)
    {
        try
        {
            action();
        }
        catch (H3Exception)
        {
            // Typed, graceful failure is acceptable; a crash is not.
        }
    }
}

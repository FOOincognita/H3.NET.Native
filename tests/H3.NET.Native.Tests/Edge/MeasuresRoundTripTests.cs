// SPDX-License-Identifier: Apache-2.0

using System.Collections.Generic;
using System.Linq;
using CsCheck;
using H3.NET.Native.Interop;
using H3.NET.Native.Tests.Fixtures;
using Xunit;

namespace H3.NET.Native.Tests.Edge;

/// <summary>
/// End-to-end measures round-trips and invariants over the committed corpus and the San
/// Francisco sample point: the km/m and km2/m2 unit ratios hold for every cell area and
/// edge length; a point's distance to itself is exactly zero and an adjacent cell center
/// is on the order of twice the average hexagon edge length; the children areas of a
/// parent sum back to the parent's area (area additivity); the public DegsToRads /
/// RadsToDegs are mutual inverses over finite doubles and forward bit-for-bit to their
/// native counterparts (CsCheck); and a junk-value sweep over CellArea* / EdgeLength*
/// never segfaults.
/// </summary>
public sealed class MeasuresRoundTripTests
{
    private const long Iterations = 500;
    private const double UnitRatioTolerance = 1e-9;

    private static readonly LatLng SamplePoint = new(37.775938728915946, -122.41795063018799);

    public static IEnumerable<object[]> AllResolutions() =>
        Enumerable.Range(0, 16).Select(r => new object[] { r });

    public static IEnumerable<object[]> CorpusCells() =>
        FixtureLoader.LoadCellArea().Select(c => new object[] { c.Cell });

    public static IEnumerable<object[]> CorpusEdges() =>
        FixtureLoader.LoadEdgeLength().Select(c => new object[] { c.Edge });

    // ---- unit ratios over the corpus ---------------------------------------

    [Theory]
    [MemberData(nameof(CorpusCells))]
    public void CellArea_M2OverKm2_IsOneMillion(string hex)
    {
        var cell = H3Index.Parse(hex);
        double km2 = cell.CellAreaKm2();
        double m2 = cell.CellAreaM2();
        Assert.Equal(km2 * 1e6, m2, m2 * UnitRatioTolerance);
    }

    [Theory]
    [MemberData(nameof(CorpusEdges))]
    public void EdgeLength_MOverKm_IsOneThousand(string hex)
    {
        var edge = new H3DirectedEdge(H3Index.Parse(hex).Value);
        double km = edge.EdgeLengthKm();
        double m = edge.EdgeLengthM();
        Assert.Equal(km * 1000.0, m, m * UnitRatioTolerance);
    }

    // ---- great-circle distance round-trips ---------------------------------

    [Theory]
    [MemberData(nameof(AllResolutions))]
    public void GreatCircleDistance_CenterToItself_IsZero(int resolution)
    {
        var center = H3Index.FromLatLng(SamplePoint, resolution).ToLatLng();
        Assert.Equal(0.0, LatLng.GreatCircleDistanceM(center, center), 1e-6);
    }

    [Theory]
    [MemberData(nameof(AllResolutions))]
    public void GreatCircleDistance_AdjacentCenters_AreOnOrderOfTwiceAvgEdgeLength(int resolution)
    {
        var cell = H3Index.FromLatLng(SamplePoint, resolution);
        var center = cell.ToLatLng();
        double avgEdgeKm = H3Index.GetHexagonEdgeLengthAvgKm(resolution);

        foreach (var neighbor in cell.GridRing(1))
        {
            double distKm = LatLng.GreatCircleDistanceKm(center, neighbor.ToLatLng());

            // Center-to-adjacent-center spacing is roughly 2x the cell's edge length.
            // A generous band (0.5x .. 4x of 2*edge) keeps the test robust across the
            // hexagon distortion and pentagon neighbors while still catching a missing
            // degrees->radians conversion (which would be orders of magnitude off).
            Assert.InRange(distKm, avgEdgeKm, 8.0 * avgEdgeKm);
        }
    }

    // ---- area additivity ---------------------------------------------------

    [Theory]
    [MemberData(nameof(AllResolutions))]
    public void CellArea_SumOfChildrenAreas_ApproximatesParentArea(int resolution)
    {
        if (resolution >= 15)
        {
            return; // no finer children resolution.
        }

        var parent = H3Index.FromLatLng(SamplePoint, resolution);
        double parentArea = parent.CellAreaKm2();

        double childrenArea = parent.CellToChildren(resolution + 1)
            .Sum(child => child.CellAreaKm2());

        // The 7 (or 6 for a pentagon) children tile the parent, but H3 cell areas are
        // computed per-cell from spherical geometry, so the children's spherical excess
        // does NOT sum exactly to the parent's: the discrepancy is a real geometric
        // property on the order of 1e-4 relative (largest at coarse resolutions), not a
        // binding bug. A 1e-3 relative band still catches gross errors (e.g. a missing
        // unit conversion would be orders of magnitude off) while accepting the inherent
        // spherical-excess mismatch.
        Assert.Equal(parentArea, childrenArea, parentArea * 1e-3);
    }

    // ---- DegsToRads / RadsToDegs inverse identity (CsCheck) -----------------

    [Fact]
    public void DegsToRads_RadsToDegs_AreMutualInverses()
    {
        Gen.Double[-720.0, 720.0].Sample(
            degrees =>
            {
                double roundTripped = LatLng.RadsToDegs(LatLng.DegsToRads(degrees));
                Assert.Equal(degrees, roundTripped, 1e-9);
            },
            iter: Iterations);
    }

    [Fact]
    public void RadsToDegs_DegsToRads_AreMutualInverses()
    {
        Gen.Double[-12.0, 12.0].Sample(
            radians =>
            {
                double roundTripped = LatLng.DegsToRads(LatLng.RadsToDegs(radians));
                Assert.Equal(radians, roundTripped, 1e-9);
            },
            iter: Iterations);
    }

    // The public wrappers must forward straight to native with no managed reimplementation:
    // over the sampled domain the results are bit-for-bit identical to libh3's degsToRads /
    // radsToDegs.
    [Fact]
    public void DegsToRads_MatchesNative()
    {
        Gen.Double[-720.0, 720.0].Sample(
            degrees => Assert.Equal(NativeMethods.DegsToRads(degrees), LatLng.DegsToRads(degrees)),
            iter: Iterations);
    }

    [Fact]
    public void RadsToDegs_MatchesNative()
    {
        Gen.Double[-12.0, 12.0].Sample(
            radians => Assert.Equal(NativeMethods.RadsToDegs(radians), LatLng.RadsToDegs(radians)),
            iter: Iterations);
    }

    // ---- junk-value no-segfault sweep --------------------------------------

    private static readonly ulong[] JunkValues =
    [
        0x0UL,
        0x1UL,
        0xffffffffffffffffUL,
        0x7fffffffffffffffUL,
        0xdeadbeefUL,
        0x123456789abcdef0UL,
    ];

    public static IEnumerable<object[]> JunkValueCases() =>
        JunkValues.Select(v => new object[] { v });

    [Theory]
    [MemberData(nameof(JunkValueCases))]
    public void CellAreaAndEdgeLength_OnJunkValue_ThrowH3Exception_OrReturn_NeverCrash(ulong raw)
    {
        var cell = new H3Index(raw);
        var edge = new H3DirectedEdge(raw);

        TryThrowOrReturn(() => cell.CellAreaRads2());
        TryThrowOrReturn(() => cell.CellAreaKm2());
        TryThrowOrReturn(() => cell.CellAreaM2());
        TryThrowOrReturn(() => edge.EdgeLengthRads());
        TryThrowOrReturn(() => edge.EdgeLengthKm());
        TryThrowOrReturn(() => edge.EdgeLengthM());
    }

    private static void TryThrowOrReturn(System.Action action)
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

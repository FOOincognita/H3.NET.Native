// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.Linq;
using H3.NET.Native.Tests.Fixtures;
using Xunit;

namespace H3.NET.Native.Tests.Unit;

/// <summary>
/// Per-member unit tests for the PR4 directed-edge surface: the H3Index pair
/// (IsNeighbor / DirectedEdgeTo / GetDirectedEdges / GetDirectedEdgesInto) and the
/// H3DirectedEdge value type (IsValid / Origin / Destination / ToCells / Deconstruct /
/// ToBoundary / Reverse). Each member is exercised on its happy path plus every
/// documented guard / error arm: the E_RES_MISMATCH (12) and E_NOT_NEIGHBORS (11)
/// H3Exception channels, the H3InvalidIndexException (DirEdgeInvalid=6) raised when a
/// projection runs on a bogus edge, the bare-int IsValid never-throws contract, the
/// M4 fixed-capacity-6 strip (hexagon = 6, pentagon = 5 with the H3_NULL hole
/// stripped), the *Into destination-length guard, the pre-clear of stale caller data,
/// and a junk-value no-segfault theory.
///
/// Error codes pinned against libh3 4.5.0 (the binding faithfully surfaces the raw
/// native channel): areNeighborCells / cellsToDirectedEdge across DIFFERENT resolutions
/// -&gt; E_RES_MISMATCH (12); cellsToDirectedEdge on non-adjacent same-res cells
/// -&gt; E_NOT_NEIGHBORS (11). Both map to the base H3Exception. The edge projections
/// (Origin / Destination / ToCells / ToBoundary / Reverse) on an invalid edge surface
/// E_DIR_EDGE_INVALID (6) -&gt; H3InvalidIndexException.
/// </summary>
public sealed class DirectedEdgeUnitTests
{
    private const int MaxEdgeCount = 6;

    // A junk raw value that is neither a valid cell nor a valid directed edge; used to
    // force the invalid-edge / invalid-cell error arms.
    private const ulong JunkEdge = 0xdeadbeefUL;

    private static readonly LatLng SamplePoint = new(37.775938728915946, -122.41795063018799);

    private static H3Index SampleCell(int res) => H3Index.FromLatLng(SamplePoint, res);

    // A real res-9 cell and one of its adjacent neighbors (grid ring at distance 1).
    private static H3Index Origin => SampleCell(9);

    private static H3Index Neighbor => Origin.GridRing(1)[0];

    // First pentagon from the corpus, plus a sample non-pentagon hexagon.
    private static H3Index PentagonCell =>
        H3Index.Parse(FixtureLoader.LoadDirectedEdge().First(c => c.IsPentagon).Origin);

    private static H3Index HexagonCell =>
        H3Index.Parse(FixtureLoader.LoadDirectedEdge().First(c => !c.IsPentagon).Origin);

    public static IEnumerable<object[]> HexagonOrigins() =>
        FixtureLoader.LoadDirectedEdge()
            .Where(c => !c.IsPentagon)
            .Select(c => new object[] { c.Origin });

    public static IEnumerable<object[]> PentagonOrigins() =>
        FixtureLoader.LoadDirectedEdge()
            .Where(c => c.IsPentagon)
            .Select(c => new object[] { c.Origin });

    public static IEnumerable<object[]> AllEdges() =>
        FixtureLoader.LoadDirectedEdge()
            .SelectMany(c => c.Edges)
            .Select(e => new object[] { e.Edge, e.Origin, e.Destination, e.Reverse, e.Cells.ToArray() });

    // ---- (1) IsNeighbor ----------------------------------------------------

    [Fact]
    public void IsNeighbor_AdjacentCells_ReturnsTrue()
    {
        Assert.True(Origin.IsNeighbor(Neighbor));
    }

    [Fact]
    public void IsNeighbor_SameCell_ReturnsFalse()
    {
        var a = Origin;
        Assert.False(a.IsNeighbor(a));
    }

    [Fact]
    public void IsNeighbor_DifferentResolutions_ThrowsH3Exception_ResMismatch()
    {
        var a = SampleCell(5);
        var b = SampleCell(7);
        var ex = Assert.Throws<H3Exception>(() => a.IsNeighbor(b));
        Assert.Equal(12u, ex.ErrorCode); // E_RES_MISMATCH
    }

    // ---- (2) DirectedEdgeTo ------------------------------------------------

    [Fact]
    public void DirectedEdgeTo_Neighbors_ReturnsValidEdge()
    {
        var edge = Origin.DirectedEdgeTo(Neighbor);
        Assert.True(edge.IsValid());
    }

    [Fact]
    public void DirectedEdgeTo_NonNeighbors_ThrowsH3Exception_NotNeighbors()
    {
        var origin = Origin;
        var far = origin.GridRing(3)[0];
        var ex = Assert.Throws<H3Exception>(() => origin.DirectedEdgeTo(far));
        Assert.Equal(11u, ex.ErrorCode); // E_NOT_NEIGHBORS
    }

    [Fact]
    public void DirectedEdgeTo_SelfIsNotNeighbor_ThrowsH3Exception_NotNeighbors()
    {
        var a = Origin;
        var ex = Assert.Throws<H3Exception>(() => a.DirectedEdgeTo(a));
        Assert.Equal(11u, ex.ErrorCode); // E_NOT_NEIGHBORS
    }

    [Fact]
    public void DirectedEdgeTo_DifferentResolutions_ThrowsH3Exception_NotNeighbors()
    {
        // libh3 4.5.0 cellsToDirectedEdge checks adjacency before resolution: cells at
        // different resolutions can never be neighbors, so the native channel reports
        // E_NOT_NEIGHBORS (11), NOT E_RES_MISMATCH (12). (Verified against the C ABI;
        // areNeighborCells, by contrast, validates resolution first and returns 12.) The
        // binding faithfully surfaces whichever code the native function emits.
        var a = SampleCell(5);
        var b = SampleCell(7);
        var ex = Assert.Throws<H3Exception>(() => a.DirectedEdgeTo(b));
        Assert.Equal(11u, ex.ErrorCode); // E_NOT_NEIGHBORS
    }

    // ---- (3) IsValid -------------------------------------------------------

    [Fact]
    public void IsValid_RealEdge_ReturnsTrue()
    {
        var edge = Origin.DirectedEdgeTo(Neighbor);
        Assert.True(edge.IsValid());
        Assert.True(H3DirectedEdge.IsValid(edge.Value)); // static matches the instance.
    }

    [Theory]
    [InlineData(0UL)]
    [InlineData(0x1UL)]
    [InlineData(JunkEdge)]
    public void IsValid_OnGarbage_ReturnsFalse_NeverThrows(ulong raw)
    {
        var edge = new H3DirectedEdge(raw);
        Assert.False(edge.IsValid());
        Assert.False(H3DirectedEdge.IsValid(raw)); // static and instance agree.
    }

    // ---- (4) Origin / Destination ------------------------------------------

    [Fact]
    public void OriginAndDestination_OnRealEdge_EqualOriginatingCellPair()
    {
        var origin = Origin;
        var dest = Neighbor;
        var edge = origin.DirectedEdgeTo(dest);

        Assert.Equal(origin, edge.GetOrigin());
        Assert.Equal(dest, edge.GetDestination());
    }

    [Fact]
    public void Origin_OnInvalidEdge_ThrowsH3InvalidCell()
    {
        var ex = Assert.Throws<H3InvalidIndexException>(() => _ = new H3DirectedEdge(JunkEdge).GetOrigin());
        Assert.Equal(6u, ex.ErrorCode); // E_DIR_EDGE_INVALID
    }

    [Fact]
    public void Destination_OnInvalidEdge_ThrowsH3InvalidCell()
    {
        var ex = Assert.Throws<H3InvalidIndexException>(() => _ = new H3DirectedEdge(JunkEdge).GetDestination());
        Assert.Equal(6u, ex.ErrorCode); // E_DIR_EDGE_INVALID
    }

    // ---- (5) ToCells -------------------------------------------------------

    [Fact]
    public void ToCells_OnRealEdge_ReturnsDirectedEdgeToInputs()
    {
        var origin = Origin;
        var dest = Neighbor;
        var edge = origin.DirectedEdgeTo(dest);

        var (o, d) = edge.ToCells();
        Assert.Equal(origin, o);
        Assert.Equal(dest, d);
    }

    [Fact]
    public void ToCells_OnInvalidEdge_ThrowsH3InvalidCell()
    {
        Assert.Throws<H3InvalidIndexException>(() => new H3DirectedEdge(JunkEdge).ToCells());
    }

    // ---- (6) Deconstruct ---------------------------------------------------

    [Fact]
    public void Deconstruct_EqualsOriginDestination()
    {
        var edge = Origin.DirectedEdgeTo(Neighbor);

        var (o, d) = edge; // record-struct Deconstruct, backed by ToCells.
        Assert.Equal(edge.GetOrigin(), o);
        Assert.Equal(edge.GetDestination(), d);
    }

    // ---- (7) GetDirectedEdges ----------------------------------------------

    [Theory]
    [MemberData(nameof(HexagonOrigins))]
    public void GetDirectedEdges_Hexagon_ReturnsSixValidEdges_AllOriginatingHere(string hex)
    {
        var cell = H3Index.Parse(hex);
        var edges = cell.GetDirectedEdges();

        Assert.Equal(6, edges.Length);
        Assert.All(edges, e => Assert.True(e.IsValid()));
        Assert.All(edges, e => Assert.Equal(cell, e.GetOrigin()));
        Assert.DoesNotContain(H3DirectedEdge.Null, edges);
    }

    [Theory]
    [MemberData(nameof(PentagonOrigins))]
    public void GetDirectedEdges_Pentagon_ReturnsFiveValidEdges_NullStripped(string hex)
    {
        var cell = H3Index.Parse(hex);
        var edges = cell.GetDirectedEdges();

        Assert.Equal(5, edges.Length);
        Assert.All(edges, e => Assert.True(e.IsValid()));
        Assert.All(edges, e => Assert.Equal(cell, e.GetOrigin()));
        Assert.DoesNotContain(H3DirectedEdge.Null, edges);
    }

    // ---- (8) GetDirectedEdgesInto ------------------------------------------

    [Fact]
    public void GetDirectedEdgesInto_WithTooSmallSpan_ThrowsArgumentOutOfRange()
    {
        var destination = new H3DirectedEdge[MaxEdgeCount - 1];
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() => HexagonCell.GetDirectedEdgesInto(destination));
        Assert.Equal("destination", ex.ParamName);
    }

    [Theory]
    [MemberData(nameof(HexagonOrigins))]
    public void GetDirectedEdgesInto_Hexagon_MatchesArrayOverload(string hex)
    {
        var cell = H3Index.Parse(hex);
        var expected = cell.GetDirectedEdges();

        var destination = new H3DirectedEdge[MaxEdgeCount];
        int count = cell.GetDirectedEdgesInto(destination);

        Assert.Equal(6, count);
        Assert.Equal(expected, destination[..count]);
    }

    [Theory]
    [MemberData(nameof(PentagonOrigins))]
    public void GetDirectedEdgesInto_Pentagon_OversizedPreSeeded_PreClearsStaleData_StripsToFive(string hex)
    {
        var cell = H3Index.Parse(hex);
        var expected = cell.GetDirectedEdges();

        // Seed every slot with a non-null sentinel. The pre-clear wipes these before
        // the native fill, so the strip never mistakes leftover caller data for a real
        // edge: the front `count` entries are exactly the 5 valid pentagon edges.
        var destination = new H3DirectedEdge[MaxEdgeCount];
        Array.Fill(destination, new H3DirectedEdge(JunkEdge));

        int count = cell.GetDirectedEdgesInto(destination);

        Assert.Equal(5, count);
        Assert.Equal(expected, destination[..count]);
        Assert.DoesNotContain(new H3DirectedEdge(JunkEdge), destination[..count]);
        Assert.DoesNotContain(H3DirectedEdge.Null, destination[..count]);
    }

    // ---- (9) ToBoundary ----------------------------------------------------

    [Fact]
    public void ToBoundary_RealEdge_YieldsFiniteVerticesInDegrees()
    {
        var edge = Origin.DirectedEdgeTo(Neighbor);
        var boundary = edge.ToBoundary();

        // A directed edge spans a single hex side: 2 verts normally, more when the edge
        // crosses an icosahedron face (bounded by the CellBoundary capacity of 10).
        Assert.InRange(boundary.Count, 2, 10);
        foreach (var v in boundary)
        {
            Assert.True(double.IsFinite(v.LatitudeDegrees), FormattableString.Invariant($"lat not finite: {v.LatitudeDegrees}"));
            Assert.True(double.IsFinite(v.LongitudeDegrees), FormattableString.Invariant($"lng not finite: {v.LongitudeDegrees}"));
            Assert.InRange(v.LatitudeDegrees, -90.0, 90.0);
            Assert.InRange(v.LongitudeDegrees, -180.0, 180.0);
        }
    }

    [Fact]
    public void ToBoundary_OnInvalidEdge_ThrowsH3InvalidCell()
    {
        Assert.Throws<H3InvalidIndexException>(() => new H3DirectedEdge(JunkEdge).ToBoundary());
    }

    // ---- (10) Reverse ------------------------------------------------------

    [Fact]
    public void Reverse_Twice_IsIdentity()
    {
        var edge = Origin.DirectedEdgeTo(Neighbor);
        Assert.Equal(edge, edge.Reverse().Reverse());
    }

    [Fact]
    public void Reverse_SwapsOriginAndDestination()
    {
        var edge = Origin.DirectedEdgeTo(Neighbor);
        var reversed = edge.Reverse();

        Assert.Equal(edge.GetDestination(), reversed.GetOrigin());
        Assert.Equal(edge.GetOrigin(), reversed.GetDestination());
    }

    [Fact]
    public void Reverse_OnInvalidEdge_ThrowsH3InvalidCell()
    {
        Assert.Throws<H3InvalidIndexException>(() => new H3DirectedEdge(JunkEdge).Reverse());
    }

    // ---- Receiver-validation guards on the H3Index *array members ----------

    [Fact]
    public void GetDirectedEdges_OnNull_Throws()
    {
        Assert.Throws<H3InvalidIndexException>(() => H3Index.Null.GetDirectedEdges());
    }

    [Fact]
    public void GetDirectedEdges_OnInvalidIndex_Throws()
    {
        Assert.Throws<H3InvalidIndexException>(() => new H3Index(0xffffffffffffffffUL).GetDirectedEdges());
    }

    [Fact]
    public void GetDirectedEdgesInto_OnInvalidIndex_Throws_AfterArgCheck()
    {
        // A valid-length span plus an invalid origin must still surface the cell
        // exception (the arg check passes, the receiver guard fires).
        Assert.Throws<H3InvalidIndexException>(
            () => new H3Index(0xffffffffffffffffUL).GetDirectedEdgesInto(new H3DirectedEdge[MaxEdgeCount]));
    }

    // ---- (11) Junk-value no-segfault theory --------------------------------
    //
    // None of the edge projections may segfault on a bogus raw value: each must either
    // surface a typed H3Exception (E_DIR_EDGE_INVALID for saturated/garbage edges) or
    // return gracefully. Mirrors GridTraversalUnitTests.JunkOrigins. Either arm is
    // acceptable; the load-bearing contract is "no crash, typed-or-graceful".

    private static readonly ulong[] JunkEdges =
    [
        0x0UL,                  // H3_NULL sentinel.
        0xffffffffffffffffUL,   // all bits set.
        0x1UL,                  // tiny non-edge.
        JunkEdge,               // arbitrary junk.
        0x7fffffffffffffffUL,   // high bit clear, otherwise saturated.
    ];

    public static IEnumerable<object[]> JunkEdgeCases() =>
        JunkEdges.Select(v => new object[] { v });

    [Theory]
    [MemberData(nameof(JunkEdgeCases))]
    public void EdgeProjections_OnJunkValue_ThrowH3Exception_OrReturn_NeverCrash(ulong raw)
    {
        var edge = new H3DirectedEdge(raw);

        // IsValid never throws and must report these as invalid.
        Assert.False(edge.IsValid());

        TryThrowOrReturn(() => _ = edge.GetOrigin());
        TryThrowOrReturn(() => _ = edge.GetDestination());
        TryThrowOrReturn(() => edge.ToCells());
        TryThrowOrReturn(() => edge.ToBoundary());
        TryThrowOrReturn(() => edge.Reverse());
    }

    private static void TryThrowOrReturn(Action action)
    {
        try
        {
            action();
        }
        catch (H3Exception)
        {
            // Typed, graceful failure is acceptable.
        }
    }

    // ---- Cross-member round trip (fixture-driven) --------------------------

    [Theory]
    [MemberData(nameof(AllEdges))]
    public void Edge_RoundTrips_Origin_Destination_Cells_Reverse(
        string edgeHex, string originHex, string destHex, string reverseHex, string[] cellsHex)
    {
        var edge = new H3DirectedEdge(H3Index.Parse(edgeHex).Value);

        Assert.True(edge.IsValid());
        Assert.Equal(H3Index.Parse(originHex), edge.GetOrigin());
        Assert.Equal(H3Index.Parse(destHex), edge.GetDestination());
        Assert.Equal(new H3DirectedEdge(H3Index.Parse(reverseHex).Value), edge.Reverse());

        var (origin, destination) = edge.ToCells();
        Assert.Equal(H3Index.Parse(cellsHex[0]), origin);
        Assert.Equal(H3Index.Parse(cellsHex[1]), destination);

        // Deconstruct is backed by ToCells and must agree.
        var (dOrigin, dDest) = edge;
        Assert.Equal(origin, dOrigin);
        Assert.Equal(destination, dDest);

        // The two endpoints must be neighbors, and DirectedEdgeTo must rebuild the edge.
        Assert.True(origin.IsNeighbor(destination));
        Assert.Equal(edge, origin.DirectedEdgeTo(destination));
    }
}

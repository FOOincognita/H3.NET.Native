// SPDX-License-Identifier: Apache-2.0

using System.Collections.Generic;
using System.Linq;
using H3.NET.Native.Tests.Fixtures;
using Xunit;

namespace H3.NET.Native.Tests.Differential;

/// <summary>
/// Differential test: the PR4 directed-edge surface must match the h3-py 4.5.0 oracle
/// (directed_edge.ndjson, neighbor.ndjson). Covers every oracle-backed projection:
/// cells_to_directed_edge, directed_edge_to_cells, origin_to_directed_edges (as an
/// unordered set, H3_NULL stripped, pentagons giving 5), directed_edge_to_boundary
/// (including the multi-vertex icosahedron-face-crossing case, which can exceed 2 verts
/// and may straddle the antimeridian without normalization), is_valid_directed_edge
/// over a mix of real edges and junk, are_neighbor_cells over both branches, and
/// reverse_directed_edge. The GetDirectedEdgesInto overload is checked against the array
/// overload across the corpus.
///
/// Only the H3Error happy-path subset of are_neighbor_cells lives here (the oracle never
/// records error pairs); the E_RES_MISMATCH error pairs are covered in the unit suite.
/// </summary>
public sealed class DirectedEdgeTests
{
    // Boundary lat/lng must match the oracle to ~1e-9; matches CellToBoundaryTests.
    private const double ToleranceDegrees = 1e-9;

    // Junk raw values that the oracle reports as NOT valid directed edges.
    private static readonly ulong[] JunkEdges =
    [
        0x0UL,
        0x1UL,
        0xdeadbeefUL,
        0xffffffffffffffffUL,
        0x7fffffffffffffffUL,
    ];

    // Cached corpus indexed positionally; rows carry only an int index so xUnit v3 can
    // serialize the theory data (complex objects are not natively serializable).
    private static readonly List<FixtureLoader.DirectedEdgeCase> AllOrigins =
        FixtureLoader.LoadDirectedEdge().ToList();

    private static readonly List<FixtureLoader.DirectedEdgeDetail> AllEdges =
        AllOrigins.SelectMany(c => c.Edges).ToList();

    public static IEnumerable<object[]> EdgeCases() =>
        Enumerable.Range(0, AllEdges.Count).Select(i => new object[] { i });

    public static IEnumerable<object[]> OriginCases() =>
        Enumerable.Range(0, AllOrigins.Count).Select(i => new object[] { i });

    public static IEnumerable<object[]> NeighborCases() =>
        FixtureLoader.LoadNeighbor()
            .Select(n => new object[] { n.Origin, n.Candidate, n.AreNeighbors });

    public static IEnumerable<object[]> ValidEdgeCases() =>
        AllEdges.Select(e => new object[] { e.Edge, true });

    public static IEnumerable<object[]> JunkEdgeCases() =>
        JunkEdges.Select(v => new object[] { v.ToString("x16", System.Globalization.CultureInfo.InvariantCulture), false });

    // Edges whose boundary exceeds two vertices: these cross an icosahedron face and
    // are exactly the subtle ToBoundary case the oracle exists to pin down.
    public static IEnumerable<object[]> MultiVertexEdgeCases() =>
        Enumerable.Range(0, AllEdges.Count)
            .Where(i => AllEdges[i].Boundary.Count > 2)
            .Select(i => new object[] { i });

    // ---- cells_to_directed_edge -------------------------------------------

    [Theory]
    [MemberData(nameof(EdgeCases))]
    public void DirectedEdgeTo_MatchesOracle(int index)
    {
        var oracle = AllEdges[index];
        var origin = H3Index.Parse(oracle.Cells[0]);
        var destination = H3Index.Parse(oracle.Cells[1]);

        var edge = origin.DirectedEdgeTo(destination);

        Assert.Equal(H3Index.Parse(oracle.Edge).Value, edge.Value);
    }

    // ---- directed_edge_to_cells -------------------------------------------

    [Theory]
    [MemberData(nameof(EdgeCases))]
    public void ToCells_MatchesOracle(int index)
    {
        var oracle = AllEdges[index];
        var edge = new H3DirectedEdge(H3Index.Parse(oracle.Edge).Value);

        var (origin, destination) = edge.ToCells();

        Assert.Equal(H3Index.Parse(oracle.Cells[0]), origin);
        Assert.Equal(H3Index.Parse(oracle.Cells[1]), destination);
    }

    // ---- getDirectedEdgeOrigin / Destination / reverse --------------------

    [Theory]
    [MemberData(nameof(EdgeCases))]
    public void OriginDestinationReverse_MatchOracle(int index)
    {
        var oracle = AllEdges[index];
        var edge = new H3DirectedEdge(H3Index.Parse(oracle.Edge).Value);

        Assert.Equal(H3Index.Parse(oracle.Origin), edge.GetOrigin());
        Assert.Equal(H3Index.Parse(oracle.Destination), edge.GetDestination());
        Assert.Equal(new H3DirectedEdge(H3Index.Parse(oracle.Reverse).Value), edge.Reverse());
    }

    // ---- origin_to_directed_edges (unordered set, H3_NULL stripped) -------

    [Theory]
    [MemberData(nameof(OriginCases))]
    public void GetDirectedEdges_MatchesOracleSet(int index)
    {
        var oracle = AllOrigins[index];
        var origin = H3Index.Parse(oracle.Origin);

        var expected = oracle.Edges.Select(e => H3Index.Parse(e.Edge).Value).ToHashSet();
        var actual = origin.GetDirectedEdges().Select(e => e.Value).ToHashSet();

        // Pentagons give 5 (H3_NULL stripped), hexagons 6; order is unspecified.
        Assert.Equal(oracle.IsPentagon ? 5 : 6, expected.Count);
        Assert.Equal(expected, actual);
    }

    [Theory]
    [MemberData(nameof(OriginCases))]
    public void GetDirectedEdgesInto_MatchesGetDirectedEdges(int index)
    {
        var oracle = AllOrigins[index];
        var origin = H3Index.Parse(oracle.Origin);
        var expected = origin.GetDirectedEdges();

        var destination = new H3DirectedEdge[6];
        int count = origin.GetDirectedEdgesInto(destination);

        Assert.Equal(expected.Length, count);
        Assert.Equal(expected, destination[..count]);
    }

    // ---- directed_edge_to_boundary ----------------------------------------

    [Theory]
    [MemberData(nameof(EdgeCases))]
    public void ToBoundary_MatchesOracle(int index)
    {
        var oracle = AllEdges[index];
        var edge = new H3DirectedEdge(H3Index.Parse(oracle.Edge).Value);

        var boundary = edge.ToBoundary();

        Assert.Equal(oracle.Boundary.Count, boundary.Count);
        for (int i = 0; i < oracle.Boundary.Count; i++)
        {
            // Oracle vertex layout is [lat, lng]; degrees, not normalized.
            Assert.Equal(oracle.Boundary[i][0], boundary[i].LatitudeDegrees, ToleranceDegrees);
            Assert.Equal(oracle.Boundary[i][1], boundary[i].LongitudeDegrees, ToleranceDegrees);
        }
    }

    [Theory]
    [MemberData(nameof(MultiVertexEdgeCases))]
    public void ToBoundary_FaceCrossing_HasMoreThanTwoVerts_MatchesOracle(int index)
    {
        var oracle = AllEdges[index];
        var edge = new H3DirectedEdge(H3Index.Parse(oracle.Edge).Value);

        var boundary = edge.ToBoundary();

        Assert.True(boundary.Count > 2);
        Assert.Equal(oracle.Boundary.Count, boundary.Count);
    }

    // ---- is_valid_directed_edge (real edges + junk) -----------------------

    [Theory]
    [MemberData(nameof(ValidEdgeCases))]
    [MemberData(nameof(JunkEdgeCases))]
    public void IsValid_MatchesOracle(string hex, bool expected)
    {
        var edge = new H3DirectedEdge(H3Index.Parse(hex).Value);
        Assert.Equal(expected, edge.IsValid());
        Assert.Equal(expected, H3DirectedEdge.IsValid(edge.Value));
    }

    // ---- are_neighbor_cells (both branches, happy path only) --------------

    [Theory]
    [MemberData(nameof(NeighborCases))]
    public void IsNeighbor_MatchesOracle(string originHex, string candidateHex, bool expected)
    {
        var origin = H3Index.Parse(originHex);
        var candidate = H3Index.Parse(candidateHex);

        Assert.Equal(expected, origin.IsNeighbor(candidate));
    }

    [Fact]
    public void Corpus_IsNonEmpty()
    {
        Assert.NotEmpty(EdgeCases());
        Assert.NotEmpty(OriginCases());
        Assert.NotEmpty(NeighborCases());
        Assert.NotEmpty(MultiVertexEdgeCases());
        // The corpus must include at least one pentagon so the 5-edge strip is exercised.
        Assert.Contains(AllOrigins, c => c.IsPentagon);
    }
}

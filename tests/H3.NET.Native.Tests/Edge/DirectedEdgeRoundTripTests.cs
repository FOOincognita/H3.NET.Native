// SPDX-License-Identifier: Apache-2.0

using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace H3.NET.Native.Tests.Edge;

/// <summary>
/// End-to-end directed-edge round-trips and inverses driven from the San Francisco
/// sample point across resolutions: DirectedEdgeTo -&gt; ToCells reproduces the
/// (origin, destination) pair; ToCells -&gt; DirectedEdgeTo reproduces the edge; Reverse
/// twice is identity; Origin / Destination agree with ToCells; and every edge returned
/// by origin.GetDirectedEdges() points away from that origin to one of its neighbors.
/// </summary>
public sealed class DirectedEdgeRoundTripTests
{
    private static readonly LatLng SamplePoint = new(37.775938728915946, -122.41795063018799);

    public static IEnumerable<object[]> AllResolutions() =>
        Enumerable.Range(0, 16).Select(r => new object[] { r });

    [Theory]
    [MemberData(nameof(AllResolutions))]
    public void DirectedEdgeTo_ThenToCells_ReproducesOriginDestination(int resolution)
    {
        var origin = H3Index.FromLatLng(SamplePoint, resolution);

        foreach (var neighbor in origin.GridRing(1))
        {
            var edge = origin.DirectedEdgeTo(neighbor);
            var (o, d) = edge.ToCells();

            Assert.Equal(origin, o);
            Assert.Equal(neighbor, d);

            // Origin / Destination must agree with ToCells.
            Assert.Equal(o, edge.GetOrigin());
            Assert.Equal(d, edge.GetDestination());
        }
    }

    [Theory]
    [MemberData(nameof(AllResolutions))]
    public void ToCells_ThenDirectedEdgeTo_ReproducesEdge(int resolution)
    {
        var origin = H3Index.FromLatLng(SamplePoint, resolution);

        foreach (var edge in origin.GetDirectedEdges())
        {
            var (o, d) = edge.ToCells();
            Assert.Equal(edge, o.DirectedEdgeTo(d));
        }
    }

    [Theory]
    [MemberData(nameof(AllResolutions))]
    public void Reverse_Twice_IsIdentity(int resolution)
    {
        var origin = H3Index.FromLatLng(SamplePoint, resolution);

        foreach (var edge in origin.GetDirectedEdges())
        {
            Assert.Equal(edge, edge.Reverse().Reverse());

            // A single reverse swaps the endpoints.
            var reversed = edge.Reverse();
            Assert.Equal(edge.GetOrigin(), reversed.GetDestination());
            Assert.Equal(edge.GetDestination(), reversed.GetOrigin());
        }
    }

    [Theory]
    [MemberData(nameof(AllResolutions))]
    public void GetDirectedEdges_EveryEdgeOriginatesHere_AndPointsToANeighbor(int resolution)
    {
        var origin = H3Index.FromLatLng(SamplePoint, resolution);

        foreach (var edge in origin.GetDirectedEdges())
        {
            Assert.True(edge.IsValid());
            Assert.Equal(origin, edge.GetOrigin());
            Assert.True(origin.IsNeighbor(edge.GetDestination()));
        }
    }
}

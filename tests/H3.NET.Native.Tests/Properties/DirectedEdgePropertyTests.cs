// SPDX-License-Identifier: Apache-2.0

using CsCheck;
using Xunit;

namespace H3.NET.Native.Tests.Properties;

/// <summary>
/// Directed-edge invariants for the PR4 surface, sampled over random cells via
/// <see cref="Generators.PointAtResolution"/>: every directed edge to a grid-ring(1)
/// neighbor round-trips through ToCells and rebuilds via DirectedEdgeTo; Reverse is an
/// involution that swaps endpoints; ToBoundary always yields 2..10 finite vertices in
/// canonical degree ranges; and GetDirectedEdges yields 6 edges for a hexagon / 5 for a
/// pentagon, each valid and originating at the sampled cell.
/// </summary>
public sealed class DirectedEdgePropertyTests
{
    private const long Iterations = 150;

    [Fact]
    public void DirectedEdgeToNeighbor_RoundTripsThroughToCells()
    {
        Generators.PointAtResolution.Sample(
            input =>
            {
                var (point, res) = input;
                var origin = H3Index.FromLatLng(point, res);

                foreach (var neighbor in origin.GridRing(1))
                {
                    var edge = origin.DirectedEdgeTo(neighbor);

                    var (o, d) = edge.ToCells();
                    Assert.Equal(origin, o);
                    Assert.Equal(neighbor, d);

                    // The edge is rebuildable from its own endpoints.
                    Assert.Equal(edge, o.DirectedEdgeTo(d));
                }
            },
            iter: Iterations);
    }

    [Fact]
    public void Reverse_IsAnInvolution_ThatSwapsEndpoints()
    {
        Generators.PointAtResolution.Sample(
            input =>
            {
                var (point, res) = input;
                var origin = H3Index.FromLatLng(point, res);

                foreach (var edge in origin.GetDirectedEdges())
                {
                    Assert.Equal(edge, edge.Reverse().Reverse());

                    var reversed = edge.Reverse();
                    Assert.Equal(edge.GetOrigin(), reversed.GetDestination());
                    Assert.Equal(edge.GetDestination(), reversed.GetOrigin());
                }
            },
            iter: Iterations);
    }

    [Fact]
    public void ToBoundary_AlwaysYieldsFiniteDegreesInRange()
    {
        Generators.PointAtResolution.Sample(
            input =>
            {
                var (point, res) = input;
                var origin = H3Index.FromLatLng(point, res);

                foreach (var edge in origin.GetDirectedEdges())
                {
                    var boundary = edge.ToBoundary();

                    Assert.InRange(boundary.Count, 2, 10);
                    foreach (var v in boundary)
                    {
                        Assert.True(double.IsFinite(v.LatitudeDegrees));
                        Assert.True(double.IsFinite(v.LongitudeDegrees));
                        Assert.InRange(v.LatitudeDegrees, -90.0, 90.0);
                        Assert.InRange(v.LongitudeDegrees, -180.0, 180.0);
                    }
                }
            },
            iter: Iterations);
    }

    [Fact]
    public void GetDirectedEdges_CountIsSixForHexagonFiveForPentagon_AllValidAndOriginatingHere()
    {
        Generators.PointAtResolution.Sample(
            input =>
            {
                var (point, res) = input;
                var origin = H3Index.FromLatLng(point, res);

                var edges = origin.GetDirectedEdges();

                Assert.Equal(origin.IsPentagon ? 5 : 6, edges.Length);
                Assert.All(edges, e =>
                {
                    Assert.True(e.IsValid());
                    Assert.Equal(origin, e.GetOrigin());
                    Assert.True(origin.IsNeighbor(e.GetDestination()));
                });
            },
            iter: Iterations);
    }
}

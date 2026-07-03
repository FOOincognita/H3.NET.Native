// SPDX-License-Identifier: Apache-2.0

using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace H3.NET.Native.Tests.Edge;

/// <summary>
/// End-to-end vertex edge cases and round-trips driven from the San Francisco sample
/// point across resolutions: saturated/garbage vertex values drive IsValid()==false and a
/// typed-or-graceful ToLatLng() (never a segfault); the H3Vertex.Null sentinel reports
/// IsNull / invalid and throws on ToLatLng; and GetVertex across every valid vertexNum for
/// a sampled cell yields distinct, valid vertices that agree with GetVertexes().
/// </summary>
public sealed class VertexRoundTripTests
{
    private static readonly LatLng SamplePoint = new(37.775938728915946, -122.41795063018799);

    // Saturated / garbage raw values that must never crash the projections.
    private static readonly ulong[] JunkVertices =
    [
        0x0UL,
        0x1UL,
        0xffffffffffffffffUL,
        0x7fffffffffffffffUL,
        0xdeadbeefUL,
    ];

    public static IEnumerable<object[]> AllResolutions() =>
        Enumerable.Range(0, 16).Select(r => new object[] { r });

    public static IEnumerable<object[]> JunkVertexCases() =>
        JunkVertices.Select(v => new object[] { v });

    [Theory]
    [MemberData(nameof(JunkVertexCases))]
    public void JunkVertex_IsInvalid_AndToLatLngIsTypedOrGraceful_NeverCrashes(ulong raw)
    {
        var vertex = new H3Vertex(raw);

        Assert.False(vertex.IsValid());

        try
        {
            vertex.ToLatLng();
        }
        catch (H3Exception)
        {
            // Typed, graceful failure is acceptable; a crash is not.
        }
    }

    [Fact]
    public void Null_IsNull_IsInvalid_AndToLatLngThrows()
    {
        Assert.True(H3Vertex.Null.IsNull);
        Assert.False(H3Vertex.Null.IsValid());
        Assert.Throws<H3InvalidIndexException>(() => H3Vertex.Null.ToLatLng());
    }

    [Theory]
    [MemberData(nameof(AllResolutions))]
    public void GetVertex_AllVertexNum_YieldsDistinctValidVertices_MatchingGetVertexes(int resolution)
    {
        var cell = H3Index.FromLatLng(SamplePoint, resolution);
        int count = cell.IsPentagon ? 5 : 6;

        var indexed = Enumerable.Range(0, count)
            .Select(n => cell.GetVertex(n))
            .ToList();

        // Each indexed vertex is valid and the set is distinct.
        Assert.All(indexed, v => Assert.True(v.IsValid()));
        Assert.Equal(count, indexed.Select(v => v.Value).Distinct().Count());

        // The indexed set matches the bulk GetVertexes() set.
        Assert.Equal(
            cell.GetVertexes().Select(v => v.Value).ToHashSet(),
            indexed.Select(v => v.Value).ToHashSet());
    }
}

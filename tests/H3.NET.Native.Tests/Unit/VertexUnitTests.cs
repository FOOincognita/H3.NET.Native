// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.Linq;
using H3.NET.Native.Tests.Fixtures;
using Xunit;

namespace H3.NET.Native.Tests.Unit;

/// <summary>
/// Per-member unit tests for the PR5 vertex surface: the H3Index trio (GetVertex /
/// GetVertexes / GetVertexesInto) and the H3Vertex value type (IsValid / ToLatLng).
/// Each member is exercised on its happy path plus every documented guard / error arm:
/// the E_DOMAIN (2) channel raised when vertexNum is out of range for the cell (a
/// hexagon's 6th index or a pentagon's 5th index, which must NOT be silently clamped),
/// the E_CELL_INVALID (5) -&gt; H3InvalidIndexException raised when a vertex operation runs
/// on a bogus origin cell, the E_VERTEX_INVALID (8) -&gt; H3InvalidIndexException raised when
/// a projection runs on a bogus vertex, the bare-int IsValid never-throws contract, the
/// M4 fixed-capacity-6 strip (hexagon = 6, pentagon = 5 with the H3_NULL hole stripped),
/// the *Into destination-length guard, the pre-clear of stale caller data, and a
/// junk-value no-segfault theory.
///
/// Error codes pinned against libh3 4.5.0 (the binding faithfully surfaces the raw native
/// channel): cellToVertex with an out-of-range vertexNum -&gt; E_DOMAIN (2) -&gt;
/// H3DomainException; vertexToLatLng on an invalid vertex -&gt; E_VERTEX_INVALID (8) -&gt;
/// H3InvalidIndexException; the receiver guard on an invalid origin cell -&gt;
/// E_CELL_INVALID (5) -&gt; H3InvalidIndexException.
/// </summary>
public sealed class VertexUnitTests
{
    private const int MaxVertexCount = 6;

    // Error codes emitted by libh3 4.5.0 for the vertex surface.
    private const uint DomainErrorCode = 2; // E_DOMAIN: out-of-range vertexNum.
    private const uint VertexInvalidErrorCode = 8; // E_VERTEX_INVALID: bogus vertex.

    // A junk raw value that is neither a valid cell nor a valid vertex; used to force the
    // invalid-vertex / invalid-cell error arms.
    private const ulong JunkVertex = 0xdeadbeefUL;

    private static readonly LatLng SamplePoint = new(37.775938728915946, -122.41795063018799);

    private static H3Index SampleCell(int res) => H3Index.FromLatLng(SamplePoint, res);

    // First pentagon from the corpus, plus a sample non-pentagon hexagon.
    private static H3Index PentagonCell =>
        H3Index.Parse(FixtureLoader.LoadVertex().First(c => c.IsPentagon).Origin);

    private static H3Index HexagonCell =>
        H3Index.Parse(FixtureLoader.LoadVertex().First(c => !c.IsPentagon).Origin);

    public static IEnumerable<object[]> HexagonOrigins() =>
        FixtureLoader.LoadVertex()
            .Where(c => !c.IsPentagon)
            .Select(c => new object[] { c.Origin });

    public static IEnumerable<object[]> PentagonOrigins() =>
        FixtureLoader.LoadVertex()
            .Where(c => c.IsPentagon)
            .Select(c => new object[] { c.Origin });

    // ---- (1) GetVertex -----------------------------------------------------

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void GetVertex_Hexagon_EveryVertexNum_ReturnsValidVertex(int vertexNum)
    {
        var vertex = HexagonCell.GetVertex(vertexNum);
        Assert.True(vertex.IsValid());
        Assert.False(vertex.IsNull);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void GetVertex_Pentagon_EveryVertexNum_ReturnsValidVertex(int vertexNum)
    {
        var vertex = PentagonCell.GetVertex(vertexNum);
        Assert.True(vertex.IsValid());
        Assert.False(vertex.IsNull);
    }

    [Fact]
    public void GetVertex_Hexagon_VertexNumSix_ThrowsH3Domain()
    {
        // A hexagon has vertex numbers 0..5; index 6 is out of range. Native validates
        // vertexNum and emits E_DOMAIN; the binding must NOT pre-clamp it.
        var ex = Assert.Throws<H3DomainException>(() => HexagonCell.GetVertex(6));
        Assert.Equal(DomainErrorCode, ex.ErrorCode);
    }

    [Fact]
    public void GetVertex_Pentagon_VertexNumFive_ThrowsH3Domain_NotClamped()
    {
        // A pentagon has vertex numbers 0..4; index 5 is out of range. The locked decision
        // is that pentagons are NOT pre-clamped: index 5 must throw E_DOMAIN, never silently
        // return vertex 4 (or any other clamped value).
        var ex = Assert.Throws<H3DomainException>(() => PentagonCell.GetVertex(5));
        Assert.Equal(DomainErrorCode, ex.ErrorCode);
    }

    [Fact]
    public void GetVertex_NegativeVertexNum_ThrowsH3Domain()
    {
        var ex = Assert.Throws<H3DomainException>(() => HexagonCell.GetVertex(-1));
        Assert.Equal(DomainErrorCode, ex.ErrorCode);
    }

    [Fact]
    public void GetVertex_OnNull_ThrowsH3InvalidCell()
    {
        Assert.Throws<H3InvalidIndexException>(() => H3Index.Null.GetVertex(0));
    }

    [Fact]
    public void GetVertex_OnInvalidIndex_ThrowsH3InvalidCell()
    {
        Assert.Throws<H3InvalidIndexException>(() => new H3Index(0xffffffffffffffffUL).GetVertex(0));
    }

    // ---- (2) GetVertexes ---------------------------------------------------

    [Theory]
    [MemberData(nameof(HexagonOrigins))]
    public void GetVertexes_Hexagon_ReturnsSixValidVertices_NoNull(string hex)
    {
        var cell = H3Index.Parse(hex);
        var vertexes = cell.GetVertexes();

        Assert.Equal(6, vertexes.Length);
        Assert.All(vertexes, v => Assert.True(v.IsValid()));
        Assert.DoesNotContain(H3Vertex.Null, vertexes);
    }

    [Theory]
    [MemberData(nameof(PentagonOrigins))]
    public void GetVertexes_Pentagon_ReturnsFiveValidVertices_NullStripped(string hex)
    {
        var cell = H3Index.Parse(hex);
        var vertexes = cell.GetVertexes();

        Assert.Equal(5, vertexes.Length);
        Assert.All(vertexes, v => Assert.True(v.IsValid()));
        Assert.DoesNotContain(H3Vertex.Null, vertexes);
    }

    [Fact]
    public void GetVertexes_OnNull_ThrowsH3InvalidCell()
    {
        Assert.Throws<H3InvalidIndexException>(() => H3Index.Null.GetVertexes());
    }

    [Fact]
    public void GetVertexes_OnInvalidIndex_ThrowsH3InvalidCell()
    {
        Assert.Throws<H3InvalidIndexException>(() => new H3Index(0xffffffffffffffffUL).GetVertexes());
    }

    // ---- (3) GetVertexesInto -----------------------------------------------

    [Fact]
    public void GetVertexesInto_WithTooSmallSpan_ThrowsArgumentOutOfRange()
    {
        var destination = new H3Vertex[MaxVertexCount - 1];
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() => HexagonCell.GetVertexesInto(destination));
        Assert.Equal("destination", ex.ParamName);
    }

    [Theory]
    [MemberData(nameof(HexagonOrigins))]
    public void GetVertexesInto_Hexagon_MatchesArrayOverload(string hex)
    {
        var cell = H3Index.Parse(hex);
        var expected = cell.GetVertexes();

        var destination = new H3Vertex[MaxVertexCount];
        int count = cell.GetVertexesInto(destination);

        Assert.Equal(6, count);
        Assert.Equal(expected, destination[..count]);
    }

    [Theory]
    [MemberData(nameof(PentagonOrigins))]
    public void GetVertexesInto_Pentagon_MatchesArrayOverload(string hex)
    {
        var cell = H3Index.Parse(hex);
        var expected = cell.GetVertexes();

        var destination = new H3Vertex[MaxVertexCount];
        int count = cell.GetVertexesInto(destination);

        Assert.Equal(5, count);
        Assert.Equal(expected, destination[..count]);
    }

    [Theory]
    [MemberData(nameof(PentagonOrigins))]
    public void GetVertexesInto_Pentagon_OversizedPreSeeded_PreClearsStaleData_StripsToFive(string hex)
    {
        var cell = H3Index.Parse(hex);
        var expected = cell.GetVertexes();

        // Seed every slot with a non-null sentinel. The pre-clear wipes these before the
        // native fill, so the strip never mistakes leftover caller data for a real vertex:
        // the front `count` entries are exactly the 5 valid pentagon vertices.
        var destination = new H3Vertex[MaxVertexCount];
        Array.Fill(destination, new H3Vertex(JunkVertex));

        int count = cell.GetVertexesInto(destination);

        Assert.Equal(5, count);
        Assert.Equal(expected, destination[..count]);
        Assert.DoesNotContain(new H3Vertex(JunkVertex), destination[..count]);
        Assert.DoesNotContain(H3Vertex.Null, destination[..count]);
    }

    [Fact]
    public void GetVertexesInto_OnInvalidIndex_Throws_AfterArgCheck()
    {
        // A valid-length span plus an invalid origin must still surface the cell exception
        // (the arg check passes, the receiver guard fires).
        Assert.Throws<H3InvalidIndexException>(
            () => new H3Index(0xffffffffffffffffUL).GetVertexesInto(new H3Vertex[MaxVertexCount]));
    }

    // ---- (4) ToLatLng ------------------------------------------------------

    [Fact]
    public void ToLatLng_OnRealVertex_YieldsFiniteDegreesInRange()
    {
        var vertex = HexagonCell.GetVertex(0);
        var point = vertex.ToLatLng();

        Assert.True(double.IsFinite(point.LatitudeDegrees));
        Assert.True(double.IsFinite(point.LongitudeDegrees));
        Assert.InRange(point.LatitudeDegrees, -90.0, 90.0);
        Assert.InRange(point.LongitudeDegrees, -180.0, 180.0);
    }

    [Fact]
    public void ToLatLng_OnJunkVertex_ThrowsH3InvalidCell_VertexInvalid()
    {
        // EnsureValidVertex fires before the H3Error channel; the code pins E_VERTEX_INVALID.
        var ex = Assert.Throws<H3InvalidIndexException>(() => new H3Vertex(JunkVertex).ToLatLng());
        Assert.Equal(VertexInvalidErrorCode, ex.ErrorCode);
    }

    [Fact]
    public void ToLatLng_OnNull_ThrowsH3InvalidCell_VertexInvalid()
    {
        var ex = Assert.Throws<H3InvalidIndexException>(() => H3Vertex.Null.ToLatLng());
        Assert.Equal(VertexInvalidErrorCode, ex.ErrorCode);
    }

    // ---- (5) IsValid -------------------------------------------------------

    [Fact]
    public void IsValid_RealVertex_ReturnsTrue_InstanceMatchesStatic()
    {
        var vertex = HexagonCell.GetVertex(0);
        Assert.True(vertex.IsValid());
        Assert.True(H3Vertex.IsValid(vertex.Value)); // static matches the instance.
    }

    [Theory]
    [InlineData(0UL)]
    [InlineData(0x1UL)]
    [InlineData(0xdeadbeefUL)]
    [InlineData(0xffffffffffffffffUL)]
    public void IsValid_OnGarbage_ReturnsFalse_NeverThrows(ulong raw)
    {
        var vertex = new H3Vertex(raw);
        Assert.False(vertex.IsValid());
        Assert.False(H3Vertex.IsValid(raw)); // static and instance agree.
    }

    // ---- (6) Junk-value no-segfault theory ---------------------------------
    //
    // Neither IsValid nor ToLatLng may segfault on a bogus raw value: IsValid must report
    // false (and never throw), and ToLatLng must either surface a typed H3Exception
    // (E_VERTEX_INVALID for a bogus vertex) or return gracefully. Mirrors
    // DirectedEdgeUnitTests.EdgeProjections_OnJunkValue. The load-bearing contract is "no
    // crash, typed-or-graceful".

    private static readonly ulong[] JunkVertices =
    [
        0x0UL,                  // H3_NULL sentinel.
        0xffffffffffffffffUL,   // all bits set.
        0x1UL,                  // tiny non-vertex.
        JunkVertex,             // arbitrary junk.
        0x7fffffffffffffffUL,   // high bit clear, otherwise saturated.
    ];

    public static IEnumerable<object[]> JunkVertexCases() =>
        JunkVertices.Select(v => new object[] { v });

    [Theory]
    [MemberData(nameof(JunkVertexCases))]
    public void VertexProjections_OnJunkValue_ThrowH3Exception_OrReturn_NeverCrash(ulong raw)
    {
        var vertex = new H3Vertex(raw);

        // IsValid never throws and must report these as invalid.
        Assert.False(vertex.IsValid());

        TryThrowOrReturn(() => vertex.ToLatLng());
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
}

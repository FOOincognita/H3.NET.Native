// SPDX-License-Identifier: Apache-2.0

using System.Collections.Generic;
using System.Linq;
using H3.NET.Native.Tests.Fixtures;
using Xunit;

namespace H3.NET.Native.Tests.Edge;

/// <summary>
/// Curated edge cases: the 122 res-0 base cells, the 192 pentagons, all 16 resolutions
/// round-tripping a sample point, and the antimeridian/pole rows from the oracle.
/// </summary>
public sealed class EdgeCaseTests
{
    [Fact]
    public void Res0Cells_AreAllValid_AtResolutionZero()
    {
        var cells = FixtureLoader.LoadRes0Cells();
        Assert.Equal(122, cells.Count);

        foreach (string hex in cells)
        {
            var cell = H3Index.Parse(hex);
            Assert.True(cell.IsValidCell(), $"{hex} should be a valid cell.");
            Assert.Equal(0, cell.Resolution);
        }
    }

    [Fact]
    public void Pentagons_AreAllValid_AndReportPentagon()
    {
        var pentagons = FixtureLoader.LoadPentagons();
        Assert.Equal(192, pentagons.Count);

        foreach (var (hex, res) in pentagons)
        {
            var cell = H3Index.Parse(hex);
            Assert.True(cell.IsValidCell(), $"{hex} should be a valid cell.");
            Assert.True(cell.IsPentagon, $"{hex} should be a pentagon.");
            Assert.Equal(res, cell.Resolution);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    [InlineData(10)]
    [InlineData(11)]
    [InlineData(12)]
    [InlineData(13)]
    [InlineData(14)]
    [InlineData(15)]
    public void SamplePoint_RoundTrips_AtEveryResolution(int resolution)
    {
        // San Francisco; an arbitrary mid-latitude land point.
        var point = new LatLng(37.775938728915946, -122.41795063018799);

        var cell = H3Index.FromLatLng(point, resolution);
        Assert.True(cell.IsValidCell());
        Assert.Equal(resolution, cell.Resolution);

        // Center re-binning is a fixed point at the same resolution.
        var center = cell.ToLatLng();
        Assert.Equal(cell.Value, H3Index.FromLatLng(center, resolution).Value);
    }

    public static IEnumerable<object[]> AntimeridianAndPoleCases() =>
        FixtureLoader.LoadLatLngToCell()
            .Where(c => c.Lng is 180.0 or -180.0 || c.Lat is 90.0 or -90.0)
            .Select(c => new object[] { c.Lat, c.Lng, c.Res, c.Cell });

    [Theory]
    [MemberData(nameof(AntimeridianAndPoleCases))]
    public void AntimeridianAndPole_ProduceValidCells_MatchingOracle(
        double lat, double lng, int res, string expectedHex)
    {
        var expected = H3Index.Parse(expectedHex);
        var actual = H3Index.FromLatLng(new LatLng(lat, lng), res);

        Assert.True(actual.IsValidCell());
        Assert.Equal(expected.Value, actual.Value);
    }

    [Fact]
    public void AntimeridianAndPole_Corpus_IsNonEmpty()
    {
        // Guards against the curated extreme rows silently disappearing from the corpus.
        Assert.NotEmpty(AntimeridianAndPoleCases());
    }

    [Fact]
    public void NullSentinel_IsZeroValued_AndReportsNull()
    {
        var sentinel = H3Index.Null;

        Assert.Equal(0UL, sentinel.Value);
        Assert.True(sentinel.IsNull);
        Assert.Equal(default, sentinel);
        Assert.Equal(sentinel, new H3Index(0));
    }

    [Fact]
    public void NullSentinel_IsNotAValidCell()
    {
        var sentinel = H3Index.Null;

        // Mode bits are zero, so the native validity check rejects the sentinel.
        Assert.False(sentinel.IsValidCell());
    }

    [Fact]
    public void NullSentinel_Resolution_Throws()
    {
        var sentinel = H3Index.Null;

        // Resolution is gated on IsValidCell, so the sentinel surfaces a typed failure.
        Assert.Throws<H3InvalidIndexException>(() => _ = sentinel.Resolution);
    }

    [Fact]
    public void NullSentinel_IsPentagon_Throws()
    {
        var sentinel = H3Index.Null;

        // IsPentagon is gated on IsValidCell, so the sentinel surfaces a typed failure.
        Assert.Throws<H3InvalidIndexException>(() => _ = sentinel.IsPentagon);
    }

    [Fact]
    public void NullSentinel_ToString_IsZeroPaddedHex()
    {
        // ToString is a pure formatter and does not validate; the sentinel renders as all zeros.
        Assert.Equal("0000000000000000", H3Index.Null.ToString());
    }

    [Theory]
    [InlineData(0x0UL)]                  // H3_NULL: in-range base cell, native returns garbage area.
    [InlineData(0x1UL)]                  // Reserved/mode bits unset; native does not reject.
    [InlineData(0x8528347300000000UL)]  // Wrong mode/digits, in-range base cell.
    public void CellArea_OnInvalidCell_ThrowsInvalidCell(ulong rawCell)
    {
        // Native cellArea* only rejects an out-of-range base cell, so the managed
        // validate-first guard supplies the documented H3InvalidIndexException.
        var cell = new H3Index(rawCell);
        Assert.False(cell.IsValidCell());

        Assert.Throws<H3InvalidIndexException>(() => cell.CellAreaRads2());
        Assert.Throws<H3InvalidIndexException>(() => cell.CellAreaKm2());
        Assert.Throws<H3InvalidIndexException>(() => cell.CellAreaM2());
    }

    [Fact]
    public void EdgeLength_OnInvalidEdge_ThrowsInvalidCell()
    {
        // A directed-edge-mode value whose origin is invalid: native directedEdgeToBoundary
        // never calls isValidCell(origin), so the managed guard supplies the exception.
        // Derive it by corrupting a real edge's origin digits while keeping edge mode + direction.
        var origin = H3Index.Parse("8928308280fffff");
        var edge = origin.GetDirectedEdges()[0];
        Assert.True(edge.IsValid());

        // Set a digit in the origin's unused (post-resolution) tail to 7, which fails
        // isValidCell's _hasAny7/_hasDeletedSubsequence checks while keeping directed-edge
        // mode and a parseable direction. Native directedEdgeToBoundary does not reject it.
        var broken = new H3DirectedEdge(edge.Value | (0x7UL << 18));
        Assert.False(broken.IsValid());

        Assert.Throws<H3InvalidIndexException>(() => broken.EdgeLengthRads());
        Assert.Throws<H3InvalidIndexException>(() => broken.EdgeLengthKm());
        Assert.Throws<H3InvalidIndexException>(() => broken.EdgeLengthM());
    }
}

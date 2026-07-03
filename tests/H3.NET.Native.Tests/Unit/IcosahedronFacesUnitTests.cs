// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.Linq;
using H3.NET.Native.Tests.Fixtures;
using Xunit;

namespace H3.NET.Native.Tests.Unit;

/// <summary>
/// Per-function unit tests for GetIcosahedronFaces / GetIcosahedronFacesInto: faces are
/// always in range with no -1 sentinel leaking through; pentagons report exactly five
/// distinct faces; the Span overload validates its destination length and matches the
/// array overload; invalid indices throw the typed cell exception.
/// </summary>
public sealed class IcosahedronFacesUnitTests
{
    public static IEnumerable<object[]> HexagonCells() =>
        FixtureLoader.LoadRes0Cells()
            .Concat(FixtureLoader.LoadIndexDigits().Select(c => c.Cell))
            .Distinct(System.StringComparer.Ordinal)
            .Select(hex => H3Index.Parse(hex))
            .Where(c => !c.IsPentagon)
            .Select(c => new object[] { c.ToString() });

    public static IEnumerable<object[]> PentagonCells() =>
        FixtureLoader.LoadPentagons().Select(p => new object[] { p.Cell });

    [Theory]
    [MemberData(nameof(HexagonCells))]
    public void Hexagon_HasOneOrTwoFaces_AllInRange_NoSentinel(string hex)
    {
        var faces = H3Index.Parse(hex).GetIcosahedronFaces();

        Assert.InRange(faces.Length, 1, 2);
        Assert.All(faces, f => Assert.InRange(f, 0, 19));
        Assert.DoesNotContain(-1, faces);
        Assert.Equal(faces.Length, faces.Distinct().Count());
    }

    [Theory]
    [MemberData(nameof(PentagonCells))]
    public void Pentagon_HasExactlyFiveDistinctFaces_AllInRange(string hex)
    {
        var faces = H3Index.Parse(hex).GetIcosahedronFaces();

        Assert.Equal(5, faces.Length);
        Assert.Equal(5, faces.Distinct().Count());
        Assert.All(faces, f => Assert.InRange(f, 0, 19));
        Assert.DoesNotContain(-1, faces);
    }

    [Fact]
    public void Into_WithTooSmallSpan_ThrowsArgumentOutOfRangeException()
    {
        // A pentagon needs 5 slots; a 4-slot span is too small.
        var pentagon = H3Index.Parse(FixtureLoader.LoadPentagons()[0].Cell);
        var destination = new int[4];
        Assert.Throws<ArgumentOutOfRangeException>(() => pentagon.GetIcosahedronFacesInto(destination));
    }

    [Theory]
    [MemberData(nameof(HexagonCells))]
    public void Into_MatchesArrayOverload_AndReturnsCount(string hex)
    {
        var cell = H3Index.Parse(hex);
        var expected = cell.GetIcosahedronFaces();

        var destination = new int[8];
        int count = cell.GetIcosahedronFacesInto(destination);

        Assert.Equal(expected.Length, count);
        Assert.Equal(expected, destination[..count]);
    }

    [Theory]
    [MemberData(nameof(PentagonCells))]
    public void Into_OnPentagon_MatchesArrayOverload(string hex)
    {
        var cell = H3Index.Parse(hex);
        var expected = cell.GetIcosahedronFaces();

        var destination = new int[8];
        int count = cell.GetIcosahedronFacesInto(destination);

        Assert.Equal(5, count);
        Assert.Equal(expected, destination[..count]);
    }

    [Fact]
    public void GetIcosahedronFaces_OnNull_Throws()
    {
        Assert.Throws<H3InvalidIndexException>(() => H3Index.Null.GetIcosahedronFaces());
    }

    [Fact]
    public void GetIcosahedronFacesInto_OnNull_Throws()
    {
        Assert.Throws<H3InvalidIndexException>(() => H3Index.Null.GetIcosahedronFacesInto(new int[8]));
    }

    [Fact]
    public void GetIcosahedronFaces_OnInvalidIndex_Throws()
    {
        Assert.Throws<H3InvalidIndexException>(() => new H3Index(0xffffffffffffffffUL).GetIcosahedronFaces());
    }
}

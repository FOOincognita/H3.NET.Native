// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using Xunit;

namespace H3.NET.Native.Tests.Unit;

/// <summary>
/// Per-member unit tests for the <see cref="GeoPolygon"/> constructor guards. The
/// happy-path region suites only build well-formed polygons, so the constructor's
/// argument validation was previously cold. Each case pins the documented contract: a
/// null exterior is an <see cref="ArgumentNullException"/>, a null hole ring is an
/// <see cref="ArgumentException"/> naming <c>holes</c>, and a null holes collection is
/// normalized to an empty list rather than rejected.
/// </summary>
public sealed class GeoPolygonUnitTests
{
    private static IReadOnlyList<LatLng> Triangle() =>
    [
        new LatLng(37.80, -122.45),
        new LatLng(37.80, -122.40),
        new LatLng(37.75, -122.42),
    ];

    [Fact]
    public void Constructor_NullExterior_ThrowsArgumentNullException()
    {
        var ex = Assert.Throws<ArgumentNullException>(() => new GeoPolygon(null!));
        Assert.Equal("exterior", ex.ParamName);
    }

    [Fact]
    public void Constructor_NullHoleRing_ThrowsArgumentException()
    {
        // A non-null holes collection whose element is null is rejected up front: a null
        // ring can never be pinned into a native loop, so it fails fast at construction.
        var holes = new IReadOnlyList<LatLng>[] { null! };
        var ex = Assert.Throws<ArgumentException>(() => new GeoPolygon(Triangle(), holes));
        Assert.Equal("holes", ex.ParamName);
    }

    [Fact]
    public void Constructor_NullHolesCollection_NormalizesToEmpty()
    {
        // A null holes argument is the documented "no holes" form, not an error.
        var polygon = new GeoPolygon(Triangle());
        Assert.Empty(polygon.Holes);
        Assert.Equal(3, polygon.Exterior.Count);
    }

    [Fact]
    public void Constructor_NonNullHoles_ArePreserved()
    {
        var hole = new List<LatLng> { new(37.78, -122.43), new(37.78, -122.42), new(37.77, -122.42) };
        var polygon = new GeoPolygon(Triangle(), new[] { (IReadOnlyList<LatLng>)hole });
        Assert.Single(polygon.Holes);
        Assert.Equal(3, polygon.Holes[0].Count);
    }
}

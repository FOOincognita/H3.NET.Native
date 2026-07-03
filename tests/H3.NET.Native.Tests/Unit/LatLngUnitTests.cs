// SPDX-License-Identifier: Apache-2.0

using System;
using Xunit;

namespace H3.NET.Native.Tests.Unit;

/// <summary>
/// Per-member unit tests for <see cref="LatLng.Validate"/>, the managed range guard that
/// runs BEFORE any native call (in <see cref="H3Index.FromLatLng"/> and the polygon
/// vertex staging). The happy-path differential suites only ever feed in-range
/// coordinates, so the reject branches were previously cold. Each case pins the
/// documented contract: latitude must be finite and within [-90, 90], longitude finite
/// and within [-180, 180]; the inclusive endpoints are accepted, and every rejection is
/// an <see cref="ArgumentOutOfRangeException"/> naming the offending component. This
/// managed guard is why a bad coordinate surfaces as ArgumentOutOfRangeException rather
/// than reaching the native E_LATLNG_DOMAIN path.
/// </summary>
public sealed class LatLngUnitTests
{
    // ---- Latitude out of range ---------------------------------------------

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    [InlineData(-90.0001)] // just below the southern limit
    [InlineData(90.0001)]  // just above the northern limit
    public void Validate_InvalidLatitude_ThrowsArgumentOutOfRange(double latitudeDegrees)
    {
        // Longitude is valid, so the guard must reject on latitude alone.
        var coordinate = new LatLng(latitudeDegrees, 0.0);
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() => coordinate.Validate());
        Assert.Equal("latitudeDegrees", ex.ParamName);
    }

    // ---- Longitude out of range --------------------------------------------

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    [InlineData(-180.0001)] // just west of the antimeridian
    [InlineData(180.0001)]  // just east of the antimeridian
    public void Validate_InvalidLongitude_ThrowsArgumentOutOfRange(double longitudeDegrees)
    {
        // Latitude is valid, so the short-circuited guard reaches and rejects longitude.
        var coordinate = new LatLng(0.0, longitudeDegrees);
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() => coordinate.Validate());
        Assert.Equal("longitudeDegrees", ex.ParamName);
    }

    // ---- Inclusive boundaries are accepted ---------------------------------

    [Theory]
    [InlineData(-90.0, -180.0)]
    [InlineData(-90.0, 180.0)]
    [InlineData(90.0, -180.0)]
    [InlineData(90.0, 180.0)]
    [InlineData(0.0, 0.0)]
    public void Validate_InclusiveBoundaries_DoNotThrow(double latitudeDegrees, double longitudeDegrees)
    {
        // The canonical ranges are inclusive, so the exact poles and antimeridian pass.
        Assert.Null(Record.Exception(() => new LatLng(latitudeDegrees, longitudeDegrees).Validate()));
    }
}

// SPDX-License-Identifier: Apache-2.0

using System;
using System.Runtime.InteropServices;
using H3.NET.Native.Interop;

namespace H3.NET.Native;

/// <summary>
/// A geographic coordinate expressed in <b>degrees</b>.
/// </summary>
/// <param name="LatitudeDegrees">Latitude in degrees, in the range [-90, 90].</param>
/// <param name="LongitudeDegrees">Longitude in degrees, in the range [-180, 180].</param>
/// <remarks>
/// This is the public, degrees-based coordinate type. The underlying H3 C library
/// operates in radians; conversion happens only at the interop boundary.
/// </remarks>
[StructLayout(LayoutKind.Auto)]
public readonly record struct LatLng(double LatitudeDegrees, double LongitudeDegrees)
{
    private const double DegreesToRadians = Math.PI / 180.0;
    private const double RadiansToDegrees = 180.0 / Math.PI;

    /// <summary>
    /// Validates that <see cref="LatitudeDegrees"/> and <see cref="LongitudeDegrees"/>
    /// are finite and within the canonical degree ranges.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Latitude is outside [-90, 90], or longitude is outside [-180, 180], or either
    /// component is not a finite number.
    /// </exception>
    public void Validate() => ValidateComponents(LatitudeDegrees, LongitudeDegrees);

    private static void ValidateComponents(double latitudeDegrees, double longitudeDegrees)
    {
        if (!double.IsFinite(latitudeDegrees) || latitudeDegrees is < -90.0 or > 90.0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(latitudeDegrees),
                latitudeDegrees,
                "Latitude must be a finite value in the range [-90, 90] degrees.");
        }

        if (!double.IsFinite(longitudeDegrees) || longitudeDegrees is < -180.0 or > 180.0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(longitudeDegrees),
                longitudeDegrees,
                "Longitude must be a finite value in the range [-180, 180] degrees.");
        }
    }

    /// <summary>Converts this degrees-based coordinate into the radians-based interop struct.</summary>
    /// <returns>The equivalent <see cref="NativeLatLng"/> with components in radians.</returns>
    internal NativeLatLng ToNative() => new()
    {
        Lat = LatitudeDegrees * DegreesToRadians,
        Lng = LongitudeDegrees * DegreesToRadians,
    };

    /// <summary>Converts a radians-based interop struct into a degrees-based <see cref="LatLng"/>.</summary>
    /// <param name="native">The interop coordinate with components in radians.</param>
    /// <returns>The equivalent degrees-based <see cref="LatLng"/>.</returns>
    internal static LatLng FromNative(in NativeLatLng native) =>
        new(native.Lat * RadiansToDegrees, native.Lng * RadiansToDegrees);

    /// <summary>
    /// Returns the great-circle (haversine) distance between two coordinates in radians.
    /// </summary>
    /// <param name="a">The first coordinate, in degrees.</param>
    /// <param name="b">The second coordinate, in degrees.</param>
    /// <returns>The great-circle distance in radians.</returns>
    public static double GreatCircleDistanceRads(LatLng a, LatLng b)
    {
        // Public LatLng is in degrees; the native struct is in radians. ToNative()
        // stages the degrees->radians conversion before the call. Native returns the
        // distance directly and never throws.
        var na = a.ToNative();
        var nb = b.ToNative();
        return NativeMethods.GreatCircleDistanceRads(na, nb);
    }

    /// <summary>
    /// Returns the great-circle (haversine) distance between two coordinates in kilometers.
    /// </summary>
    /// <param name="a">The first coordinate, in degrees.</param>
    /// <param name="b">The second coordinate, in degrees.</param>
    /// <returns>The great-circle distance in kilometers.</returns>
    public static double GreatCircleDistanceKm(LatLng a, LatLng b)
    {
        var na = a.ToNative();
        var nb = b.ToNative();
        return NativeMethods.GreatCircleDistanceKm(na, nb);
    }

    /// <summary>
    /// Returns the great-circle (haversine) distance between two coordinates in meters.
    /// </summary>
    /// <param name="a">The first coordinate, in degrees.</param>
    /// <param name="b">The second coordinate, in degrees.</param>
    /// <returns>The great-circle distance in meters.</returns>
    public static double GreatCircleDistanceM(LatLng a, LatLng b)
    {
        var na = a.ToNative();
        var nb = b.ToNative();
        return NativeMethods.GreatCircleDistanceM(na, nb);
    }

    /// <summary>
    /// Converts an angle from degrees to radians.
    /// </summary>
    /// <param name="degrees">The angle to convert, in degrees.</param>
    /// <returns>The equivalent angle in radians.</returns>
    public static double DegsToRads(double degrees) => NativeMethods.DegsToRads(degrees);

    /// <summary>
    /// Converts an angle from radians to degrees.
    /// </summary>
    /// <param name="radians">The angle to convert, in radians.</param>
    /// <returns>The equivalent angle in degrees.</returns>
    public static double RadsToDegs(double radians) => NativeMethods.RadsToDegs(radians);
}

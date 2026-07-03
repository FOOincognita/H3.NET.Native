// SPDX-License-Identifier: Apache-2.0

using CsCheck;
using Xunit;

namespace H3.NET.Native.Tests.Properties;

/// <summary>
/// Error-path invariants: out-of-range resolutions throw <see cref="H3DomainException"/>,
/// validating accessors (<see cref="H3Index.Resolution"/>) throw
/// <see cref="H3InvalidIndexException"/> on invalid indices, and the non-validating
/// decode <see cref="H3Index.ToLatLng"/> always returns safely (a finite, in-range
/// coordinate) or throws a typed <see cref="H3Exception"/> -- never a crash, and never
/// non-finite or out-of-range garbage.
/// </summary>
public sealed class ErrorPathPropertyTests
{
    private const long Iterations = 100;

    /// <summary>
    /// High-entropy source of genuinely invalid 64-bit indices. Almost every random
    /// 64-bit value fails <see cref="H3Index.IsValidCell()"/> (wrong mode, reserved bits
    /// set, out-of-range base cell, illegal digit, or a resolution/digit count mismatch),
    /// so the validity filter is cheap and never starves. A small slice of low-magnitude
    /// values is retained so the trivial "top bits all zero" path is still covered, but
    /// the dominant mass is full-width values that exercise near-valid bit layouts the
    /// old 1..15 source could never reach.
    /// </summary>
    private static readonly Gen<ulong> InvalidIndexGen =
        Gen.Frequency((9, Gen.ULong), (1, Gen.ULong[1UL, 0xFUL]))
            .Where(v => !new H3Index(v).IsValidCell());

    [Fact]
    public void FromLatLng_WithInvalidResolution_Throws_H3DomainException()
    {
        // Resolutions outside [0, 15] are domain errors. Split above/below the range.
        var gen = Generators.LatLngGen
            .Select(Gen.OneOf(Gen.Int[16, 1000], Gen.Int[-1000, -1]), (p, res) => (p, res));

        gen.Sample(
            input =>
            {
                var (point, res) = input;
                Assert.Throws<H3DomainException>(() => H3Index.FromLatLng(point, res));
            },
            iter: Iterations);
    }

    [Fact]
    public void Resolution_OnInvalidIndex_Throws_H3InvalidIndexException()
    {
        // Diverse 64-bit values that IsValidCell rejects (wrong mode, reserved bits set,
        // out-of-range base cell, illegal digit, resolution/digit mismatch). The filter
        // guarantees every sampled value is genuinely invalid, so no per-iteration skip
        // is needed.
        InvalidIndexGen.Sample(
            raw =>
            {
                var cell = new H3Index(raw);
                Assert.Throws<H3InvalidIndexException>(() => _ = cell.Resolution);
            },
            iter: Iterations);
    }

    [Fact]
    public void ToLatLng_OnInvalidIndex_NeverCrashes_AndStaysInDomain()
    {
        // H3's cellToLatLng is intentionally NON-validating: for many invalid raw values
        // whose bit layout still decodes, the native call returns E_SUCCESS and the
        // binding forwards a coordinate (it does not call EnsureValidCell, unlike
        // Resolution/IsPentagon); for others it surfaces a typed H3Exception. Feeding the
        // full-width invalid generator stresses both paths far more thoroughly than the
        // old 1..15 source. The hard safety contract is: the call must NEVER crash the
        // process, and must EITHER throw a typed H3Exception OR return a finite, in-range
        // coordinate -- never NaN/inf or out-of-range garbage, and never an untyped/native
        // exception.
        InvalidIndexGen.Sample(
            raw =>
            {
                var cell = new H3Index(raw);

                try
                {
                    var center = cell.ToLatLng();

                    // Returned safely: the coordinate must still be well-formed.
                    Assert.True(double.IsFinite(center.LatitudeDegrees));
                    Assert.True(double.IsFinite(center.LongitudeDegrees));
                    Assert.InRange(center.LatitudeDegrees, -90.0, 90.0);
                    Assert.InRange(center.LongitudeDegrees, -180.0, 180.0);
                }
                catch (H3Exception)
                {
                    // Also acceptable: a typed, graceful failure (no crash).
                }
            },
            iter: Iterations);
    }
}

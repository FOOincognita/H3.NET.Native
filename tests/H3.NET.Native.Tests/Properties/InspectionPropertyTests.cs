// SPDX-License-Identifier: Apache-2.0

using CsCheck;
using Xunit;

namespace H3.NET.Native.Tests.Properties;

/// <summary>
/// Property invariants for the inspection surface over the high-entropy invalid-index
/// generator: validating accessors (BaseCellNumber, IsResClassIII) either throw a typed
/// H3Exception or return without crashing; GetIndexDigit at a legal resolution likewise
/// never crashes; IsValidIndex never throws and is a superset of IsValidCell
/// (IsValidCell => IsValidIndex).
/// </summary>
public sealed class InspectionPropertyTests
{
    private const long Iterations = 100;

    /// <summary>
    /// High-entropy source of genuinely invalid 64-bit indices, identical in spirit to
    /// the generator in <see cref="ErrorPathPropertyTests"/>: almost every random 64-bit
    /// value fails IsValidCell, with a small low-magnitude slice retained.
    /// </summary>
    private static readonly Gen<ulong> InvalidIndexGen =
        Gen.Frequency((9, Gen.ULong), (1, Gen.ULong[1UL, 0xFUL]))
            .Where(v => !new H3Index(v).IsValidCell());

    [Fact]
    public void BaseCellNumber_OnInvalidIndex_ThrowsTyped_OrReturns()
    {
        InvalidIndexGen.Sample(
            raw =>
            {
                var cell = new H3Index(raw);
                try
                {
                    _ = cell.BaseCellNumber;
                }
                catch (H3Exception)
                {
                    // Typed, graceful failure is acceptable.
                }
            },
            iter: Iterations);
    }

    [Fact]
    public void IsResClassIII_OnInvalidIndex_ThrowsTyped_OrReturns()
    {
        InvalidIndexGen.Sample(
            raw =>
            {
                var cell = new H3Index(raw);
                try
                {
                    _ = cell.IsResClassIII;
                }
                catch (H3Exception)
                {
                    // Typed, graceful failure is acceptable.
                }
            },
            iter: Iterations);
    }

    [Fact]
    public void GetIndexDigit_OnInvalidIndex_AtLegalResolution_ThrowsTyped_OrReturns()
    {
        // getIndexDigit does not validate cell validity; with a legal resolution it
        // either returns the bit-extracted digit or throws a typed H3Exception. It must
        // never crash the process.
        var gen = InvalidIndexGen.Select(Gen.Int[1, 15], (raw, res) => (raw, res));
        gen.Sample(
            input =>
            {
                var (raw, res) = input;
                var cell = new H3Index(raw);
                try
                {
                    int digit = cell.GetIndexDigit(res);
                    Assert.InRange(digit, 0, 7);
                }
                catch (H3Exception)
                {
                    // Typed, graceful failure is acceptable.
                }
            },
            iter: Iterations);
    }

    [Fact]
    public void IsValidIndex_NeverThrows_AndIsSupersetOfIsValidCell()
    {
        // Over the full 64-bit space: IsValidIndex must never throw, and any value that
        // IsValidCell accepts must also be a valid index.
        Gen.ULong.Sample(
            raw =>
            {
                var cell = new H3Index(raw);
                bool validIndex = cell.IsValidIndex();
                if (cell.IsValidCell())
                {
                    Assert.True(validIndex);
                }
            },
            iter: Iterations);
    }
}

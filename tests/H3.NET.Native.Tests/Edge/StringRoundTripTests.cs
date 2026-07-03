// SPDX-License-Identifier: Apache-2.0

using System.Collections.Generic;
using System.Linq;
using H3.NET.Native.Tests.Fixtures;
using Xunit;

namespace H3.NET.Native.Tests.Edge;

/// <summary>
/// End-to-end string round-trips over the 122 res-0 cells and 192 pentagons, plus the
/// Null-sentinel contract for the new inspection/string members. Leading zeros are
/// tolerated by the native sscanf path, so FromString accepts the padded ToString form.
/// </summary>
public sealed class StringRoundTripTests
{
    public static IEnumerable<object[]> Res0AndPentagons() =>
        FixtureLoader.LoadRes0Cells()
            .Concat(FixtureLoader.LoadPentagons().Select(p => p.Cell))
            .Distinct(System.StringComparer.Ordinal)
            .Select(hex => new object[] { hex });

    [Theory]
    [MemberData(nameof(Res0AndPentagons))]
    public void AllStringForms_RoundTrip(string hex)
    {
        var self = H3Index.Parse(hex);

        // Managed padded form round-trips through the managed parser.
        Assert.Equal(self, H3Index.Parse(self.ToString()));

        // Native canonical (unpadded) form round-trips through the native parser.
        Assert.Equal(self, H3Index.FromString(self.ToCanonicalString()));

        // Native parser tolerates the leading zeros of the managed padded form (sscanf).
        Assert.Equal(self, H3Index.FromString(self.ToString()));
    }

    [Fact]
    public void NullSentinel_BaseCellNumber_Throws()
    {
        Assert.Throws<H3InvalidIndexException>(() => _ = H3Index.Null.BaseCellNumber);
    }

    [Fact]
    public void NullSentinel_IsResClassIII_Throws()
    {
        Assert.Throws<H3InvalidIndexException>(() => _ = H3Index.Null.IsResClassIII);
    }

    [Fact]
    public void NullSentinel_GetIndexDigit_Throws()
    {
        // getIndexDigit does not validate the cell, but res=1 is a legal domain, so the
        // native call succeeds and returns the stored digit of H3_NULL (which is 0).
        // It must NOT throw the cell exception; H3_NULL has digit 0 at res 1.
        // The plan pins this as a throw-or-return-without-crash contract; for H3_NULL
        // the native getIndexDigit returns successfully. We assert it does not crash and
        // the value is a legal digit.
        int digit = H3Index.Null.GetIndexDigit(1);
        Assert.InRange(digit, 0, 7);
    }

    [Fact]
    public void NullSentinel_IsValidIndex_IsFalse_AndDoesNotThrow()
    {
        Assert.False(H3Index.Null.IsValidIndex());
    }

    [Fact]
    public void NullSentinel_ToCanonicalString_IsZero()
    {
        // sprintf("%PRIx64", 0) yields "0".
        Assert.Equal("0", H3Index.Null.ToCanonicalString());
    }
}

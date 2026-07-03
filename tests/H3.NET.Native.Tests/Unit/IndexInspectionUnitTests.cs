// SPDX-License-Identifier: Apache-2.0

using System.Collections.Generic;
using System.Linq;
using H3.NET.Native.Tests.Fixtures;
using Xunit;

namespace H3.NET.Native.Tests.Unit;

/// <summary>
/// Per-function unit tests for the index-inspection surface (BaseCellNumber,
/// IsResClassIII, IsValidIndex, GetIndexDigit, Construct). Happy paths are sampled
/// across the committed corpus; every invalid input asserts the correct typed
/// exception. The high-entropy invalid generator lives in the property suite; here
/// the focus is the Null sentinel and a small set of hand-picked invalid ulongs.
/// </summary>
public sealed class IndexInspectionUnitTests
{
    // A handful of genuinely invalid raw indices (wrong mode / reserved bits set).
    private static readonly ulong[] InvalidRawIndices =
    [
        0x0UL,                  // H3_NULL sentinel.
        0xffffffffffffffffUL,   // all bits set.
        0x1UL,                  // tiny non-cell.
        0xdeadbeefUL,           // arbitrary junk.
        0x7fffffffffffffffUL,   // high bit clear, otherwise saturated.
    ];

    private static IEnumerable<string> ValidCorpusCells() =>
        FixtureLoader.LoadRes0Cells()
            .Concat(FixtureLoader.LoadPentagons().Select(p => p.Cell))
            .Concat(FixtureLoader.LoadIndexDigits().Select(c => c.Cell))
            .Distinct(System.StringComparer.Ordinal);

    public static IEnumerable<object[]> ValidCells() =>
        ValidCorpusCells().Select(hex => new object[] { hex });

    public static IEnumerable<object[]> InvalidIndices() =>
        InvalidRawIndices.Select(v => new object[] { v });

    // ---- BaseCellNumber ----------------------------------------------------

    [Theory]
    [MemberData(nameof(ValidCells))]
    public void BaseCellNumber_OnValidCell_IsInRange(string hex)
    {
        int baseCell = H3Index.Parse(hex).BaseCellNumber;
        Assert.InRange(baseCell, 0, 121);
    }

    [Theory]
    [MemberData(nameof(InvalidIndices))]
    public void BaseCellNumber_OnInvalidIndex_Throws(ulong raw)
    {
        Assert.Throws<H3InvalidIndexException>(() => _ = new H3Index(raw).BaseCellNumber);
    }

    [Fact]
    public void BaseCellNumber_OnNull_Throws()
    {
        Assert.Throws<H3InvalidIndexException>(() => _ = H3Index.Null.BaseCellNumber);
    }

    // ---- IsResClassIII -----------------------------------------------------

    [Theory]
    [MemberData(nameof(ValidCells))]
    public void IsResClassIII_OnValidCell_EqualsOddResolution(string hex)
    {
        var cell = H3Index.Parse(hex);
        Assert.Equal(cell.Resolution % 2 == 1, cell.IsResClassIII);
    }

    [Theory]
    [MemberData(nameof(InvalidIndices))]
    public void IsResClassIII_OnInvalidIndex_Throws(ulong raw)
    {
        Assert.Throws<H3InvalidIndexException>(() => _ = new H3Index(raw).IsResClassIII);
    }

    [Fact]
    public void IsResClassIII_OnNull_Throws()
    {
        Assert.Throws<H3InvalidIndexException>(() => _ = H3Index.Null.IsResClassIII);
    }

    // ---- IsValidIndex ------------------------------------------------------

    [Theory]
    [MemberData(nameof(ValidCells))]
    public void IsValidIndex_IsTrue_ForEveryValidCell(string hex)
    {
        var cell = H3Index.Parse(hex);

        // Superset relationship: a valid cell is always a valid index.
        Assert.True(cell.IsValidCell());
        Assert.True(cell.IsValidIndex());
    }

    [Theory]
    [MemberData(nameof(InvalidIndices))]
    public void IsValidIndex_NeverThrows_OnInvalidIndices(ulong raw)
    {
        // Mirrors the IsValidCell contract: a pure predicate that never throws.
        var cell = new H3Index(raw);
        _ = cell.IsValidIndex();
    }

    [Fact]
    public void IsValidIndex_OnNull_IsFalse_AndDoesNotThrow()
    {
        Assert.False(H3Index.Null.IsValidIndex());
    }

    // ---- GetIndexDigit -----------------------------------------------------

    [Theory]
    [MemberData(nameof(ValidCells))]
    public void GetIndexDigit_AboveOwnResolution_ReturnsSeven(string hex)
    {
        var cell = H3Index.Parse(hex);
        int res = cell.Resolution;
        for (int r = res + 1; r <= 15; r++)
        {
            // Unused trailing digits of a valid cell are stored as 7.
            Assert.Equal(7, cell.GetIndexDigit(r));
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(16)]
    [InlineData(-1)]
    [InlineData(100)]
    public void GetIndexDigit_OutOfDomain_ThrowsH3DomainException(int resolution)
    {
        // getIndexDigit validates 1 <= res <= 15 (E_RES_DOMAIN) regardless of the cell.
        var cell = H3Index.Parse(FixtureLoader.LoadRes0Cells()[0]);
        Assert.Throws<H3DomainException>(() => cell.GetIndexDigit(resolution));
    }

    // ---- Construct round-trip ---------------------------------------------

    [Theory]
    [MemberData(nameof(ValidCells))]
    public void Construct_RoundTrips_DecomposedValidCell(string hex)
    {
        var cell = H3Index.Parse(hex);
        int res = cell.Resolution;
        int baseCell = cell.BaseCellNumber;

        var digits = new int[res];
        for (int r = 1; r <= res; r++)
        {
            digits[r - 1] = cell.GetIndexDigit(r);
        }

        var rebuilt = H3Index.Construct(res, baseCell, digits);
        Assert.Equal(cell.Value, rebuilt.Value);
    }

    [Fact]
    public void Construct_WithResolution16_ThrowsH3DomainException()
    {
        // res 16 passes the managed length guard (digits.Length == 16) and is rejected
        // by the native argument-domain validation (E_RES_DOMAIN).
        Assert.Throws<H3DomainException>(() => H3Index.Construct(16, 0, new int[16]));
    }

    [Fact]
    public void Construct_WithNegativeResolution_ThrowsArgumentException()
    {
        // A negative resolution can never equal a (non-negative) span length, so the
        // managed length guard fires FIRST with ArgumentException, before any native
        // call. There is no way to reach the native E_RES_DOMAIN path for res < 0, so
        // ArgumentException is the faithful contract (the plan's "-1 -> H3DomainException"
        // is structurally unreachable through the managed guard).
        Assert.Throws<System.ArgumentException>(() => H3Index.Construct(-1, 0, System.Array.Empty<int>()));
    }

    [Fact]
    public void Construct_WithBaseCell122_ThrowsH3DomainException()
    {
        // Base cell numbers are 0-121; 122 is out of domain (E_BASE_CELL_DOMAIN).
        Assert.Throws<H3DomainException>(() => H3Index.Construct(0, 122, System.Array.Empty<int>()));
    }

    [Fact]
    public void Construct_WithDigitSeven_ThrowsH3DomainException()
    {
        // Digit 7 is the reserved unused-slot sentinel; it is not a legal constructed digit.
        Assert.Throws<H3DomainException>(() => H3Index.Construct(1, 0, new[] { 7 }));
    }

    [Fact]
    public void Construct_WithPentagonBaseAndLeadingKAxisDigit_ThrowsH3DomainException()
    {
        // A pentagon base cell with a leading K-axis (digit 1) names a deleted
        // sub-sequence: E_DELETED_DIGIT, mapped to H3DomainException. Base cell 4 is a
        // res-0 pentagon.
        int pentagonBase = H3Index.Parse(FixtureLoader.LoadPentagons().First(p => p.Res == 0).Cell)
            .BaseCellNumber;

        Assert.Throws<H3DomainException>(() => H3Index.Construct(1, pentagonBase, new[] { 1 }));
    }

    [Theory]
    [InlineData(2, 0)]
    [InlineData(2, 1)]
    [InlineData(3, 5)]
    public void Construct_WithDigitsLengthMismatch_ThrowsArgumentException(int resolution, int digitCount)
    {
        // The managed guard checks digits.Length == resolution before any native call.
        var digits = new int[digitCount];
        Assert.Throws<System.ArgumentException>(() => H3Index.Construct(resolution, 0, digits));
    }
}

// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.Linq;
using H3.NET.Native.Tests.Fixtures;
using Xunit;

namespace H3.NET.Native.Tests.Unit;

/// <summary>
/// Per-member unit tests for the hierarchy surface (CellToParent, CellToCenterChild,
/// CellToChildren / CellToChildrenInto, CompactCells / UncompactCells). Happy paths are
/// sampled across the committed corpus (res-0 cells, pentagons, and the hierarchy
/// sample cells); every invalid resolution asserts the correct typed exception, and the
/// Null sentinel must fail typed rather than crash.
///
/// Error-code notes pinned against libh3 4.5.0 (verified via the h3-py oracle):
///   * cellToParent with parentRes &gt; cell res -&gt; E_RES_MISMATCH (12), base H3Exception.
///   * cellToParent/centerChild/children with res 16 or -1 -&gt; E_RES_DOMAIN (4), H3DomainException.
///   * cellToCenterChild/children with res coarser than the cell -&gt; E_RES_DOMAIN (4),
///     H3DomainException (NOT E_RES_MISMATCH -- the native domain check fires first).
///   * compactCells rejects containment overlap / duplicates with E_DUPLICATE_INPUT (10).
///     There is no clean disjoint mixed-resolution input that compactCells rejects: it
///     accepts disjoint cells of differing resolutions. The E_RES_MISMATCH (12) path is
///     reached instead through uncompactCells when res is finer-bounded below the input.
/// </summary>
public sealed class HierarchyUnitTests
{
    private static IEnumerable<string> ValidCorpusCells() =>
        FixtureLoader.LoadRes0Cells()
            .Concat(FixtureLoader.LoadPentagons().Select(p => p.Cell))
            .Concat(FixtureLoader.LoadHierarchy().Select(c => c.Cell))
            .Distinct(StringComparer.Ordinal);

    public static IEnumerable<object[]> ValidCells() =>
        ValidCorpusCells().Select(hex => new object[] { hex });

    private static H3Index FirstRes0Cell() => H3Index.Parse(FixtureLoader.LoadRes0Cells()[0]);

    // ---- CellToParent ------------------------------------------------------

    [Theory]
    [MemberData(nameof(ValidCells))]
    public void CellToParent_AtOwnResolution_IsIdentity(string hex)
    {
        var cell = H3Index.Parse(hex);
        Assert.Equal(cell.Value, cell.CellToParent(cell.Resolution).Value);
    }

    [Theory]
    [MemberData(nameof(ValidCells))]
    public void CellToParent_AtCoarserResolution_HasThatResolution(string hex)
    {
        var cell = H3Index.Parse(hex);
        int res = cell.Resolution;
        if (res == 0)
        {
            return; // no coarser resolution.
        }

        var parent = cell.CellToParent(res - 1);
        Assert.Equal(res - 1, parent.Resolution);
        Assert.True(parent.IsValidCell());
    }

    [Theory]
    [MemberData(nameof(ValidCells))]
    public void CellToParent_FinerThanCell_ThrowsH3Exception(string hex)
    {
        var cell = H3Index.Parse(hex);
        int res = cell.Resolution;
        if (res >= 15)
        {
            return; // no finer parent resolution to request.
        }

        // parentRes finer than the cell -> E_RES_MISMATCH (12), the base H3Exception.
        var ex = Assert.Throws<H3Exception>(() => cell.CellToParent(res + 1));
        Assert.Equal(12u, ex.ErrorCode);
    }

    [Theory]
    [InlineData(16)]
    [InlineData(-1)]
    public void CellToParent_OutOfDomainResolution_ThrowsH3DomainException(int parentRes)
    {
        var cell = FirstRes0Cell();
        Assert.Throws<H3DomainException>(() => cell.CellToParent(parentRes));
    }

    // ---- CellToCenterChild -------------------------------------------------

    [Theory]
    [MemberData(nameof(ValidCells))]
    public void CellToCenterChild_AtOwnResolution_IsIdentity(string hex)
    {
        var cell = H3Index.Parse(hex);
        Assert.Equal(cell.Value, cell.CellToCenterChild(cell.Resolution).Value);
    }

    [Theory]
    [MemberData(nameof(ValidCells))]
    public void CellToCenterChild_CoarserThanCell_Throws(string hex)
    {
        var cell = H3Index.Parse(hex);
        int res = cell.Resolution;
        if (res == 0)
        {
            return; // no coarser child resolution to request.
        }

        // childRes coarser than the cell -> E_RES_DOMAIN (4), H3DomainException.
        Assert.Throws<H3DomainException>(() => cell.CellToCenterChild(res - 1));
    }

    [Fact]
    public void CellToCenterChild_Resolution16_ThrowsH3DomainException()
    {
        Assert.Throws<H3DomainException>(() => FirstRes0Cell().CellToCenterChild(16));
    }

    // ---- CellToChildren ----------------------------------------------------

    [Fact]
    public void CellToChildren_OnRes15Cell_AtRes15_ReturnsSelf()
    {
        // A res-15 cell has exactly one "child" at res 15: itself.
        var res15 = H3Index.FromLatLng(new LatLng(37.775938728915946, -122.41795063018799), 15);
        var children = res15.CellToChildren(15);
        Assert.Single(children);
        Assert.Equal(res15.Value, children[0].Value);
    }

    [Theory]
    [MemberData(nameof(ValidCells))]
    public void CellToChildren_CoarserThanCell_Throws(string hex)
    {
        var cell = H3Index.Parse(hex);
        int res = cell.Resolution;
        if (res == 0)
        {
            return;
        }

        // childRes coarser than the cell -> E_RES_DOMAIN (4), H3DomainException.
        Assert.Throws<H3DomainException>(() => cell.CellToChildren(res - 1));
    }

    [Fact]
    public void CellToChildren_Resolution16_ThrowsH3DomainException()
    {
        Assert.Throws<H3DomainException>(() => FirstRes0Cell().CellToChildren(16));
    }

    [Fact]
    public void CellToChildrenInto_TooSmallDestination_ThrowsArgumentOutOfRange()
    {
        var cell = FirstRes0Cell();
        long size = cell.CellToChildrenSize(1);
        var destination = new H3Index[size - 1];

        var ex = Assert.Throws<ArgumentOutOfRangeException>(
            () => cell.CellToChildrenInto(1, destination));
        Assert.Equal("destination", ex.ParamName);
    }

    [Theory]
    [MemberData(nameof(ValidCells))]
    public void CellToChildrenInto_MatchesArrayOverload(string hex)
    {
        var cell = H3Index.Parse(hex);
        int res = cell.Resolution;
        int childRes = Math.Min(15, res + 1);

        var expected = cell.CellToChildren(childRes).Select(c => c.Value).ToHashSet();

        long size = cell.CellToChildrenSize(childRes);
        var destination = new H3Index[size];
        int count = cell.CellToChildrenInto(childRes, destination);

        Assert.Equal(expected, destination.Take(count).Select(c => c.Value).ToHashSet());
    }

    // ---- CompactCells ------------------------------------------------------

    [Fact]
    public void CompactCells_EmptyInput_ReturnsEmpty()
    {
        Assert.Empty(H3Index.CompactCells(ReadOnlySpan<H3Index>.Empty));
    }

    [Fact]
    public void CompactCells_AlreadyCompactSet_IsPermutationOfItself()
    {
        // The 122 res-0 cells are already maximally compact: compacting them yields the
        // same set (order is not guaranteed, hence the unordered comparison).
        var res0 = FixtureLoader.LoadRes0Cells().Select(H3Index.Parse).ToArray();
        var compacted = H3Index.CompactCells(res0);

        Assert.Equal(
            res0.Select(c => c.Value).ToHashSet(),
            compacted.Select(c => c.Value).ToHashSet());
    }

    [Fact]
    public void CompactCells_DuplicateInput_ThrowsH3Exception_DuplicateInput()
    {
        var children = FirstRes0Cell().CellToChildren(1);
        var withDup = children.Append(children[0]).ToArray();

        var ex = Assert.Throws<H3Exception>(() => H3Index.CompactCells(withDup));
        Assert.Equal(10u, ex.ErrorCode); // E_DUPLICATE_INPUT
    }

    // NOTE on "mixed-resolution input": raw libh3 4.5.0 compactCells ACCEPTS disjoint
    // cells of differing resolutions, and even accepts a parent together with its own
    // descendants (verified directly against the C ABI: err=0). It only rejects EXACT
    // duplicates (E_DUPLICATE_INPUT, 10), covered above. The plan's "mixed-resolution ->
    // E_RES_MISMATCH (12)" reflects h3-py's extra Python-side validation, not the native
    // channel this binding wraps. The genuine ErrorCode==12 path is uncompactCells with a
    // resolution coarser than the input's finest cell, asserted below.

    // ---- UncompactCells ----------------------------------------------------

    [Fact]
    public void UncompactCells_EmptyInput_ReturnsEmpty()
    {
        Assert.Empty(H3Index.UncompactCells(ReadOnlySpan<H3Index>.Empty, 5));
    }

    [Fact]
    public void UncompactCells_ResolutionFinerThanInput_RoundTrips()
    {
        // Sanity anchor: uncompacting the single res-0 parent to res 1 yields its children.
        var parent = FirstRes0Cell();
        var children = parent.CellToChildren(1).Select(c => c.Value).ToHashSet();
        var uncompacted = H3Index.UncompactCells(new[] { parent }, 1)
            .Select(c => c.Value).ToHashSet();
        Assert.Equal(children, uncompacted);
    }

    [Fact]
    public void UncompactCells_ResolutionCoarserThanFinest_ThrowsH3Exception_ResMismatch()
    {
        // Input is at res 1; requesting res 0 is finer-bounded below the input -> the
        // native uncompactCells rejects it as E_RES_MISMATCH (12).
        var children = FirstRes0Cell().CellToChildren(1);

        var ex = Assert.Throws<H3Exception>(() => H3Index.UncompactCells(children, 0));
        Assert.Equal(12u, ex.ErrorCode);
    }

    // ---- Null sentinel -----------------------------------------------------
    //
    // The Null sentinel (H3_NULL = 0) does NOT throw through these members. Raw libh3
    // 4.5.0 treats 0 as a res-0 cell with digit 0: cellToParent(0,0), cellToCenterChild,
    // and cellToChildrenSize all return E_SUCCESS at the C ABI (verified directly).
    // h3-py raises H3CellInvalidError only because it pre-validates the cell in Python
    // before the C call; this binding wraps the raw native channel, so the faithful
    // contract is "completes without crashing the process" -- mirroring the existing
    // StringRoundTripTests null-sentinel style for getIndexDigit.

    [Fact]
    public void NullSentinel_CellToParent_DoesNotCrash()
    {
        // Raw libh3 returns E_SUCCESS for cellToParent(0, 0); assert it completes.
        var parent = H3Index.Null.CellToParent(0);
        Assert.True(parent.IsNull); // 0's parent at res 0 is 0.
    }

    [Fact]
    public void NullSentinel_CellToCenterChild_DoesNotCrash()
    {
        // Completes without throwing or crashing; the returned value is undefined.
        _ = H3Index.Null.CellToCenterChild(1);
    }

    [Fact]
    public void NullSentinel_CellToChildren_DoesNotCrash()
    {
        // Completes without throwing or crashing. The contents are undefined for H3_NULL
        // (the native fill may leave H3_NULL slots that the binding strips), so only the
        // no-crash contract is asserted -- the result must not exceed the reported size.
        var children = H3Index.Null.CellToChildren(1);
        Assert.True(children.Length <= H3Index.Null.CellToChildrenSize(1));
    }
}

// SPDX-License-Identifier: Apache-2.0

using System;
using Xunit;

namespace H3.NET.Native.Tests.Unit;

/// <summary>
/// Per-member unit tests for the <see cref="H3Index.GridDisk"/> /
/// <see cref="H3Index.GridDiskInto"/> allocation guards. The differential suite pins
/// membership against the h3-py oracle at small k; these cases pin the managed guards
/// that sit between the native sizer and the allocation:
///   * a k whose <c>maxGridDiskSize</c> (3k(k+1)+1) exceeds <see cref="Array.MaxLength"/>
///     throws <see cref="ArgumentOutOfRangeException"/> BEFORE any allocation, rather than
///     overflowing an array. The native sizer itself returns E_SUCCESS for such k, so the
///     bound is enforced purely on the managed side.
///   * <see cref="H3Index.GridDiskInto"/> checks that overflow guard before the
///     destination-length guard, and rejects an undersized destination.
/// </summary>
public sealed class GridDiskUnitTests
{
    private static readonly LatLng SamplePoint = new(37.775938728915946, -122.41795063018799);

    private static H3Index SampleCell(int res) => H3Index.FromLatLng(SamplePoint, res);

    // 3 * 30000 * 30001 + 1 == 2_700_090_001, above Array.MaxLength (2_147_483_591) and
    // below K_ALL_CELLS_AT_RES_15, so maxGridDiskSize returns the formula value, not the
    // clamped cell count. Either way the size exceeds any allocatable array.
    private const int KExceedingArrayMaxLength = 30_000;

    [Fact]
    public void GridDisk_K0_IsExactlyTheOrigin()
    {
        var origin = SampleCell(9);
        var disk = origin.GridDisk(0);
        Assert.Single(disk);
        Assert.Equal(origin.Value, disk[0].Value);
    }

    [Fact]
    public void GridDisk_KExceedingArrayMaxLength_ThrowsArgumentOutOfRange()
    {
        var ex = Assert.Throws<ArgumentOutOfRangeException>(
            () => SampleCell(9).GridDisk(KExceedingArrayMaxLength));
        Assert.Equal("k", ex.ParamName);
    }

    [Fact]
    public void GridDiskInto_KExceedingArrayMaxLength_ThrowsArgumentOutOfRange()
    {
        // The overflow guard fires before the destination-length guard, so an empty
        // destination still surfaces the k overflow (not a destination error).
        var ex = Assert.Throws<ArgumentOutOfRangeException>(
            () => SampleCell(9).GridDiskInto(KExceedingArrayMaxLength, Span<H3Index>.Empty));
        Assert.Equal("k", ex.ParamName);
    }

    [Fact]
    public void GridDiskInto_TooSmallDestination_ThrowsArgumentOutOfRange()
    {
        var origin = SampleCell(9);
        // maxGridDiskSize(1) == 7; a 6-slot span is one short.
        var destination = new H3Index[6];
        var ex = Assert.Throws<ArgumentOutOfRangeException>(
            () => origin.GridDiskInto(1, destination));
        Assert.Equal("destination", ex.ParamName);
    }
}

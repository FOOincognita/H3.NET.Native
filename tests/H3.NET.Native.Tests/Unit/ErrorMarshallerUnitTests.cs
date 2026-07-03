// SPDX-License-Identifier: Apache-2.0

using H3.NET.Native.Interop;
using Xunit;

namespace H3.NET.Native.Tests.Unit;

/// <summary>
/// Per-code unit tests for <see cref="H3ErrorMarshaller.ThrowIfError"/>, the single
/// translation point from a native <see cref="H3ErrorCode"/> to the public exception
/// hierarchy. Several codes cannot be reached through the public API (for example
/// E_LATLNG_DOMAIN is pre-empted by the managed <see cref="LatLng.Validate"/> guard, and
/// the E_MEMORY_* / E_PENTAGON arms have no deterministic trigger), so the mapping is
/// exercised directly against the internal marshaller (InternalsVisibleTo is enabled).
///
/// Codes are passed as their raw <see cref="uint"/> to keep the public test signatures
/// free of the internal enum type; each case pins that the mapped exception is the exact
/// documented type and that its <see cref="H3Exception.ErrorCode"/> round-trips the
/// upstream E_* value 1:1, so the surfaced code is indistinguishable from native.
/// </summary>
public sealed class ErrorMarshallerUnitTests
{
    [Fact]
    public void ThrowIfError_Success_DoesNotThrow()
    {
        Assert.Null(Record.Exception(() => H3ErrorMarshaller.ThrowIfError(H3ErrorCode.Success)));
    }

    // ---- Domain family -> H3DomainException --------------------------------

    [Theory]
    [InlineData(2u)]  // E_DOMAIN
    [InlineData(3u)]  // E_LATLNG_DOMAIN (unreachable via public API: LatLng.Validate pre-empts it)
    [InlineData(4u)]  // E_RES_DOMAIN
    [InlineData(15u)] // E_OPTION_INVALID
    [InlineData(17u)] // E_BASE_CELL_DOMAIN
    [InlineData(18u)] // E_DIGIT_DOMAIN
    [InlineData(19u)] // E_DELETED_DIGIT
    public void ThrowIfError_DomainFamily_ThrowsH3DomainException(uint rawCode)
    {
        var ex = Assert.Throws<H3DomainException>(() => H3ErrorMarshaller.ThrowIfError((H3ErrorCode)rawCode));
        Assert.Equal(rawCode, ex.ErrorCode);
        Assert.NotEmpty(ex.Message);
    }

    // ---- Invalid-index family -> H3InvalidIndexException -------------------

    [Theory]
    [InlineData(5u)]  // E_CELL_INVALID
    [InlineData(6u)]  // E_DIR_EDGE_INVALID
    [InlineData(7u)]  // E_UNDIR_EDGE_INVALID
    [InlineData(8u)]  // E_VERTEX_INVALID
    [InlineData(16u)] // E_INDEX_INVALID
    public void ThrowIfError_InvalidIndexFamily_ThrowsH3InvalidIndexException(uint rawCode)
    {
        var ex = Assert.Throws<H3InvalidIndexException>(() => H3ErrorMarshaller.ThrowIfError((H3ErrorCode)rawCode));
        Assert.Equal(rawCode, ex.ErrorCode);
        Assert.NotEmpty(ex.Message);
    }

    [Fact]
    public void ThrowIfError_Pentagon_ThrowsH3PentagonException()
    {
        var ex = Assert.Throws<H3PentagonException>(() => H3ErrorMarshaller.ThrowIfError(H3ErrorCode.Pentagon));
        Assert.Equal(9u, ex.ErrorCode);
        Assert.NotEmpty(ex.Message);
    }

    // ---- Memory family -> H3MemoryException --------------------------------

    [Theory]
    [InlineData(13u)] // E_MEMORY_ALLOC
    [InlineData(14u)] // E_MEMORY_BOUNDS
    public void ThrowIfError_MemoryFamily_ThrowsH3MemoryException(uint rawCode)
    {
        var ex = Assert.Throws<H3MemoryException>(() => H3ErrorMarshaller.ThrowIfError((H3ErrorCode)rawCode));
        Assert.Equal(rawCode, ex.ErrorCode);
        Assert.NotEmpty(ex.Message);
    }

    // ---- Everything else falls through to the base H3Exception ------------

    [Theory]
    [InlineData(1u)]  // E_FAILED
    [InlineData(10u)] // E_DUPLICATE_INPUT
    [InlineData(11u)] // E_NOT_NEIGHBORS
    [InlineData(12u)] // E_RES_MISMATCH
    public void ThrowIfError_GeneralFailures_ThrowExactBaseH3Exception(uint rawCode)
    {
        // Assert.Throws requires the EXACT runtime type, so this also proves these codes
        // are NOT mapped to any subtype (they land on the switch's default arm).
        var ex = Assert.Throws<H3Exception>(() => H3ErrorMarshaller.ThrowIfError((H3ErrorCode)rawCode));
        Assert.Equal(rawCode, ex.ErrorCode);
        Assert.NotEmpty(ex.Message);
    }
}

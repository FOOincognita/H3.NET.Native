// SPDX-License-Identifier: Apache-2.0

using System;
using System.Globalization;
using System.Runtime.InteropServices;
using H3.NET.Native.Interop;
using Xunit;

namespace H3.NET.Native.Tests.Marshalling;

/// <summary>
/// Per-platform ABI layout gate. The native libh3 C structs have fixed sizes; if the
/// managed mirrors drift (padding, alignment, vertex-count mismatch) marshalling
/// silently corrupts memory. These assertions must hold on every OS/arch in CI.
/// </summary>
public sealed class LayoutGoldenTests
{
    [Fact]
    public void CellBoundary_IsExactly168Bytes()
    {
        // 4 (NumVerts) + 4 (padding) + 10 * 16 (Verts) = 168.
        Assert.Equal(168, Marshal.SizeOf<CellBoundary>());
    }

    [Fact]
    public void NativeLatLng_IsExactly16Bytes()
    {
        Assert.Equal(16, Marshal.SizeOf<NativeLatLng>());
    }

    [Fact]
    public void NativeCoordIJ_IsExactly8Bytes()
    {
        Assert.Equal(8, Marshal.SizeOf<NativeCoordIJ>());
    }

    [Fact]
    public unsafe void NativeLatLng_UnmanagedSizeofMatches16()
    {
        Assert.Equal(16, sizeof(NativeLatLng));
    }

    [Fact]
    public unsafe void NativeCoordIJ_UnmanagedSizeofMatches8()
    {
        Assert.Equal(8, sizeof(NativeCoordIJ));
    }

    [Fact]
    public unsafe void CellBoundary_UnmanagedSizeofMatches168()
    {
        Assert.Equal(168, sizeof(CellBoundary));
    }

    [Fact]
    public void CellBoundary_FieldOffsets_MatchCAbi()
    {
        // NumVerts first, then 4 bytes of padding before the 8-byte-aligned Verts array.
        Assert.Equal(0, (int)Marshal.OffsetOf<CellBoundary>(nameof(CellBoundary.NumVerts)));
        Assert.Equal(8, (int)Marshal.OffsetOf<CellBoundary>(nameof(CellBoundary.Verts)));
    }

    [Fact]
    public void NativeLatLng_FieldOffsets_MatchCAbi()
    {
        // Lat precedes Lng; a transposition would still be 16 bytes, so offsets pin the order.
        Assert.Equal(0, (int)Marshal.OffsetOf<NativeLatLng>(nameof(NativeLatLng.Lat)));
        Assert.Equal(8, (int)Marshal.OffsetOf<NativeLatLng>(nameof(NativeLatLng.Lng)));
    }

    [Fact]
    public void NativeCoordIJ_FieldOffsets_MatchCAbi()
    {
        // I precedes J; an I/J swap would still be 8 bytes, so offsets pin the order.
        Assert.Equal(0, (int)Marshal.OffsetOf<NativeCoordIJ>(nameof(NativeCoordIJ.I)));
        Assert.Equal(4, (int)Marshal.OffsetOf<NativeCoordIJ>(nameof(NativeCoordIJ.J)));
    }

    [Theory]
    // Including negatives and asymmetric I/J pins that the conversion never transposes,
    // negates, or drops a sign bit while crossing the public<->interop boundary.
    [InlineData(0, 0)]
    [InlineData(1, 2)]
    [InlineData(-3, 7)]
    [InlineData(int.MinValue, int.MaxValue)]
    public void CoordIJ_ToNative_PreservesComponents(int i, int j)
    {
        var managed = new CoordIJ(i, j);
        var native = managed.ToNative();

        Assert.Equal(i, native.I);
        Assert.Equal(j, native.J);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 2)]
    [InlineData(-3, 7)]
    [InlineData(int.MinValue, int.MaxValue)]
    public void CoordIJ_FromNative_PreservesComponents(int i, int j)
    {
        var native = new NativeCoordIJ { I = i, J = j };
        var managed = CoordIJ.FromNative(native);

        Assert.Equal(i, managed.I);
        Assert.Equal(j, managed.J);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 2)]
    [InlineData(-3, 7)]
    [InlineData(int.MinValue, int.MaxValue)]
    public void CoordIJ_NativeRoundTrip_IsIdentity(int i, int j)
    {
        // CoordIJ -> NativeCoordIJ -> CoordIJ must reproduce the original record exactly.
        var original = new CoordIJ(i, j);
        var roundTripped = CoordIJ.FromNative(original.ToNative());

        Assert.Equal(original, roundTripped);
    }

    [Fact]
    public void CellBoundaryVerts_InlineArray_HasContiguous16ByteStride()
    {
        // Write a distinct pattern through the indexer, then read it back. This pins the
        // 16-byte NativeLatLng stride of the InlineArray buffer without any native call;
        // a wrong stride would alias adjacent slots and corrupt the readback.
        CellBoundary boundary = default;

        for (int i = 0; i < NativeLayout.MaxCellBoundaryVerts; i++)
        {
            boundary.Verts[i] = new NativeLatLng { Lat = i, Lng = i + 100 };
        }

        for (int i = 0; i < NativeLayout.MaxCellBoundaryVerts; i++)
        {
            Assert.Equal(i, boundary.Verts[i].Lat);
            Assert.Equal(i + 100, boundary.Verts[i].Lng);
        }
    }

    [Theory]
    // A known hexagon (res 8, San Francisco) and a known res-0 pentagon.
    [InlineData("8928308280fffff")]
    [InlineData("08009fffffffffff")]
    public void Boundary_RoundTrip_YieldsFiniteVerticesInDegrees(string hex)
    {
        var cell = H3Index.Parse(hex);
        Assert.True(cell.IsValidCell(), $"{hex} should be a valid cell.");

        var boundary = cell.GetBoundary();

        // libh3 produces 5 verts for pentagons, up to 10 when edges cross faces.
        Assert.InRange(boundary.Count, 5, 10);

        foreach (var v in boundary)
        {
            Assert.True(double.IsFinite(v.LatitudeDegrees), FormattableString.Invariant($"lat not finite: {v.LatitudeDegrees}"));
            Assert.True(double.IsFinite(v.LongitudeDegrees), FormattableString.Invariant($"lng not finite: {v.LongitudeDegrees}"));
            Assert.InRange(v.LatitudeDegrees, -90.0, 90.0);
            Assert.InRange(v.LongitudeDegrees, -180.0, 180.0);
        }
    }

    [Fact]
    public void DirectedEdgeToBoundary_FillsSameCellBoundaryLayout_AsCellToBoundary()
    {
        // directedEdgeToBoundary reuses the EXACT CellBoundary [InlineArray(10)] struct +
        // golden marshalling already pinned for cellToBoundary above (the 168-byte size
        // and field-offset facts cover M3). This builds a real directed edge and asserts
        // the vertices come back finite and in canonical degree ranges -- the same layout
        // contract, exercised through the directed-edge code path.
        var origin = H3Index.Parse("8928308280fffff"); // res-8 San Francisco hexagon.
        var edge = origin.DirectedEdgeTo(origin.GetDirectedEdges()[0].GetDestination());

        var boundary = edge.ToBoundary();

        Assert.InRange(boundary.Count, 2, NativeLayout.MaxCellBoundaryVerts);
        foreach (var v in boundary)
        {
            Assert.True(double.IsFinite(v.LatitudeDegrees), FormattableString.Invariant($"lat not finite: {v.LatitudeDegrees}"));
            Assert.True(double.IsFinite(v.LongitudeDegrees), FormattableString.Invariant($"lng not finite: {v.LongitudeDegrees}"));
            Assert.InRange(v.LatitudeDegrees, -90.0, 90.0);
            Assert.InRange(v.LongitudeDegrees, -180.0, 180.0);
        }
    }

    [Fact]
    public void Pentagon_Boundary_HasFiveVertices()
    {
        // 08009fffffffffff is a res-0 pentagon in the corpus (pentagons.csv).
        var pentagon = H3Index.Parse("08009fffffffffff");
        Assert.True(pentagon.IsPentagon, "Expected a pentagon cell.");
        Assert.Equal(5, pentagon.GetBoundary().Count);
    }

    [Fact]
    public void HexString_RoundTrips_Through_ToString()
    {
        const string hex = "8928308280fffff";
        var cell = H3Index.Parse(hex);

        // ToString is zero-padded 16-hex; parsing it back must be stable.
        string padded = cell.ToString();
        Assert.Equal(16, padded.Length);
        Assert.Equal(cell, H3Index.Parse(padded));
        Assert.Equal(
            ulong.Parse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture),
            cell.Value);
    }
}

// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using H3.NET.Native.Interop;

namespace H3.NET.Native;

/// <summary>
/// A 64-bit H3 directed-edge index, identifying one of the directed boundary edges
/// from an origin cell to one of its neighbors. All public coordinates exposed
/// through its members are in <b>degrees</b>.
/// </summary>
/// <param name="Value">The raw 64-bit H3 directed-edge index value.</param>
public readonly record struct H3DirectedEdge(ulong Value)
{
    /// <summary>Gets the sentinel "null" edge (raw value <c>0</c>), representing an invalid or absent directed edge.</summary>
    public static H3DirectedEdge Null => default;

    /// <summary>Gets a value indicating whether this is the <see cref="Null"/> sentinel (raw value <c>0</c>).</summary>
    public bool IsNull => Value == 0;

    /// <summary>
    /// Returns the origin cell of this directed edge.
    /// </summary>
    /// <returns>The origin <see cref="H3Index"/> cell.</returns>
    /// <exception cref="H3InvalidIndexException">This is not a valid directed edge.</exception>
    public H3Index GetOrigin()
    {
        H3ErrorMarshaller.ThrowIfError(NativeMethods.GetDirectedEdgeOrigin(Value, out ulong cell));
        return new H3Index(cell);
    }

    /// <summary>
    /// Returns the destination cell of this directed edge.
    /// </summary>
    /// <returns>The destination <see cref="H3Index"/> cell.</returns>
    /// <exception cref="H3InvalidIndexException">This is not a valid directed edge.</exception>
    public H3Index GetDestination()
    {
        H3ErrorMarshaller.ThrowIfError(NativeMethods.GetDirectedEdgeDestination(Value, out ulong cell));
        return new H3Index(cell);
    }

    /// <summary>
    /// Returns a value indicating whether the native library considers this a valid H3
    /// directed-edge index. This never throws, even for an arbitrary raw value.
    /// </summary>
    /// <returns><see langword="true"/> if this is a valid directed edge; otherwise <see langword="false"/>.</returns>
    public bool IsValid() => NativeMethods.IsValidDirectedEdge(Value) != 0;

    /// <summary>
    /// Returns a value indicating whether the native library considers a raw value a
    /// valid H3 directed-edge index. This never throws.
    /// </summary>
    /// <param name="value">The raw 64-bit value to test.</param>
    /// <returns><see langword="true"/> if <paramref name="value"/> is a valid directed edge; otherwise <see langword="false"/>.</returns>
    public static bool IsValid(ulong value) => NativeMethods.IsValidDirectedEdge(value) != 0;

    /// <summary>
    /// Returns the origin and destination cells of this directed edge.
    /// </summary>
    /// <returns>A tuple of the origin and destination <see cref="H3Index"/> cells.</returns>
    /// <exception cref="H3InvalidIndexException">This is not a valid directed edge.</exception>
    public unsafe (H3Index Origin, H3Index Destination) ToCells()
    {
        // Constant-size 2 (M2): native writes both slots (out[0]=origin, out[1]=destination),
        // so there is no H3_NULL strip and no pre-clear required.
        Span<ulong> buffer = stackalloc ulong[2];
        fixed (ulong* ptr = buffer)
        {
            H3ErrorMarshaller.ThrowIfError(NativeMethods.DirectedEdgeToCells(Value, ptr));
        }

        return (new H3Index(buffer[0]), new H3Index(buffer[1]));
    }

    /// <summary>
    /// Returns the boundary vertices of this directed edge in order from origin to
    /// destination.
    /// </summary>
    /// <returns>
    /// The boundary vertices, in degrees (usually 2, more when the edge crosses an
    /// icosahedron face). The result may straddle the antimeridian and is not normalized.
    /// </returns>
    /// <exception cref="H3InvalidIndexException">This is not a valid directed edge.</exception>
    public IReadOnlyList<LatLng> ToBoundary()
    {
        H3ErrorMarshaller.ThrowIfError(NativeMethods.DirectedEdgeToBoundary(Value, out CellBoundary boundary));

        int count = boundary.NumVerts;
        var result = new LatLng[count];
        for (int i = 0; i < count; i++)
        {
            result[i] = LatLng.FromNative(boundary.Verts[i]);
        }

        return result;
    }

    /// <summary>
    /// Returns the directed edge pointing in the opposite direction (from this edge's
    /// destination back to its origin).
    /// </summary>
    /// <returns>The reversed <see cref="H3DirectedEdge"/>.</returns>
    /// <exception cref="H3InvalidIndexException">This is not a valid directed edge.</exception>
    public H3DirectedEdge Reverse()
    {
        H3ErrorMarshaller.ThrowIfError(NativeMethods.ReverseDirectedEdge(Value, out ulong edge));
        return new H3DirectedEdge(edge);
    }

    /// <summary>
    /// Returns the length of this directed edge in radians.
    /// </summary>
    /// <returns>The edge length in radians.</returns>
    /// <exception cref="H3InvalidIndexException">This is not a valid directed edge.</exception>
    public double EdgeLengthRads()
    {
        // Native edgeLengthRads -> directedEdgeToBoundary validates the edge mode and
        // direction but never calls isValidCell(origin), so a directed-edge-mode value
        // with an in-range base cell but malformed origin digits returns E_SUCCESS with
        // a garbage length. Validate-first to honor the documented exception.
        EnsureValid();
        H3ErrorMarshaller.ThrowIfError(NativeMethods.EdgeLengthRads(Value, out double length));
        return length;
    }

    /// <summary>
    /// Returns the length of this directed edge in kilometers.
    /// </summary>
    /// <returns>The edge length in kilometers.</returns>
    /// <exception cref="H3InvalidIndexException">This is not a valid directed edge.</exception>
    public double EdgeLengthKm()
    {
        // Native does not fully validate the edge origin (see EdgeLengthRads); validate-first.
        EnsureValid();
        H3ErrorMarshaller.ThrowIfError(NativeMethods.EdgeLengthKm(Value, out double length));
        return length;
    }

    /// <summary>
    /// Returns the length of this directed edge in meters.
    /// </summary>
    /// <returns>The edge length in meters.</returns>
    /// <exception cref="H3InvalidIndexException">This is not a valid directed edge.</exception>
    public double EdgeLengthM()
    {
        // Native does not fully validate the edge origin (see EdgeLengthRads); validate-first.
        EnsureValid();
        H3ErrorMarshaller.ThrowIfError(NativeMethods.EdgeLengthM(Value, out double length));
        return length;
    }

    /// <summary>
    /// Deconstructs this directed edge into its origin and destination cells.
    /// </summary>
    /// <param name="origin">The origin <see cref="H3Index"/> cell.</param>
    /// <param name="destination">The destination <see cref="H3Index"/> cell.</param>
    /// <exception cref="H3InvalidIndexException">This is not a valid directed edge.</exception>
    public void Deconstruct(out H3Index origin, out H3Index destination)
    {
        (origin, destination) = ToCells();
    }

    private void EnsureValid()
    {
        if (!IsValid())
        {
            throw new H3InvalidIndexException(
                (uint)H3ErrorCode.DirEdgeInvalid,
                $"0x{Value:x16} is not a valid H3 directed edge.");
        }
    }
}

// SPDX-License-Identifier: Apache-2.0

using H3.NET.Native.Interop;

namespace H3.NET.Native;

/// <summary>
/// A 64-bit H3 vertex index, identifying one of the topological vertices shared by
/// adjacent cells. All public coordinates exposed through its members are in
/// <b>degrees</b>.
/// </summary>
/// <param name="Value">The raw 64-bit H3 vertex index value.</param>
public readonly record struct H3Vertex(ulong Value)
{
    /// <summary>Gets the sentinel "null" vertex (raw value <c>0</c>), representing an invalid or absent vertex.</summary>
    public static H3Vertex Null => default;

    /// <summary>Gets a value indicating whether this is the <see cref="Null"/> sentinel (raw value <c>0</c>).</summary>
    public bool IsNull => Value == 0;

    /// <summary>
    /// Returns a value indicating whether the native library considers this a valid H3
    /// vertex index. This never throws, even for an arbitrary raw value.
    /// </summary>
    /// <returns><see langword="true"/> if this is a valid vertex; otherwise <see langword="false"/>.</returns>
    public bool IsValid() => NativeMethods.IsValidVertex(Value) != 0;

    /// <summary>
    /// Returns a value indicating whether the native library considers a raw value a
    /// valid H3 vertex index. This never throws.
    /// </summary>
    /// <param name="value">The raw 64-bit value to test.</param>
    /// <returns><see langword="true"/> if <paramref name="value"/> is a valid vertex; otherwise <see langword="false"/>.</returns>
    public static bool IsValid(ulong value) => NativeMethods.IsValidVertex(value) != 0;

    /// <summary>
    /// Returns the geographic coordinate of this vertex.
    /// </summary>
    /// <returns>The vertex location, in degrees.</returns>
    /// <exception cref="H3InvalidIndexException">This is not a valid H3 vertex.</exception>
    public LatLng ToLatLng()
    {
        EnsureValidVertex();
        H3ErrorMarshaller.ThrowIfError(NativeMethods.VertexToLatLng(Value, out NativeLatLng native));
        return LatLng.FromNative(native);
    }

    private void EnsureValidVertex()
    {
        if (!IsValid())
        {
            throw new H3InvalidIndexException(
                (uint)H3ErrorCode.VertexInvalid,
                $"0x{Value:x16} is not a valid H3 vertex.");
        }
    }
}

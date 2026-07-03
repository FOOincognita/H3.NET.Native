// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;
using H3.NET.Native.Interop;

namespace H3.NET.Native;

/// <summary>
/// A 64-bit H3 cell index. This is the primary handle for an H3 cell; all
/// public coordinates exposed through its members are in <b>degrees</b>.
/// </summary>
/// <param name="Value">The raw 64-bit H3 index value.</param>
public readonly partial record struct H3Index(ulong Value)
{
    /// <summary>Gets the sentinel "null" index (raw value <c>0</c>), representing an invalid or absent cell.</summary>
    public static H3Index Null => default;

    /// <summary>Gets a value indicating whether this is the <see cref="Null"/> sentinel (raw value <c>0</c>).</summary>
    public bool IsNull => Value == 0;

    /// <summary>
    /// Returns a value indicating whether the native library considers this a valid H3 cell.
    /// </summary>
    /// <returns><see langword="true"/> if this is a valid H3 cell; otherwise <see langword="false"/>.</returns>
    public bool IsValidCell() => NativeMethods.IsValidCell(Value) != 0;

    /// <summary>
    /// Returns a value indicating whether the native library considers this a valid H3
    /// index of any kind: a cell, a directed edge, or a vertex. This is broader than
    /// <see cref="IsValidCell()"/>, which is true only for cells; it returns
    /// <see langword="true"/> for valid edge and vertex indexes even though the typed
    /// edge and vertex APIs are introduced in later releases.
    /// </summary>
    /// <returns><see langword="true"/> if this is a valid H3 index (cell, directed edge, or vertex); otherwise <see langword="false"/>.</returns>
    public bool IsValidIndex() => NativeMethods.IsValidIndex(Value) != 0;

    /// <summary>
    /// Gets the resolution (0-15) of this cell.
    /// </summary>
    /// <exception cref="H3InvalidIndexException">This is not a valid H3 cell.</exception>
    public int Resolution
    {
        get
        {
            EnsureValidCell();
            return NativeMethods.GetResolution(Value);
        }
    }

    /// <summary>
    /// Gets the base cell number (0-121) of this cell.
    /// </summary>
    /// <exception cref="H3InvalidIndexException">This is not a valid H3 cell.</exception>
    public int BaseCellNumber
    {
        get
        {
            EnsureValidCell();
            return NativeMethods.GetBaseCellNumber(Value);
        }
    }

    /// <summary>
    /// Gets a value indicating whether this cell is one of the twelve pentagons at its resolution.
    /// </summary>
    /// <exception cref="H3InvalidIndexException">This is not a valid H3 cell.</exception>
    public bool IsPentagon
    {
        get
        {
            EnsureValidCell();
            return NativeMethods.IsPentagon(Value) != 0;
        }
    }

    /// <summary>
    /// Gets a value indicating whether this cell is a Class III cell, that is, one
    /// whose resolution is odd. Class III cells are rotated and slightly distorted
    /// relative to Class II (even-resolution) cells.
    /// </summary>
    /// <exception cref="H3InvalidIndexException">This is not a valid H3 cell.</exception>
    public bool IsResClassIII
    {
        get
        {
            EnsureValidCell();
            return NativeMethods.IsResClassIII(Value) != 0;
        }
    }

    /// <summary>
    /// Converts a geographic coordinate to the containing H3 cell at the given resolution.
    /// </summary>
    /// <param name="latLng">The coordinate, in degrees.</param>
    /// <param name="resolution">The target H3 resolution (0-15).</param>
    /// <returns>The H3 cell containing <paramref name="latLng"/>.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The coordinate components are outside their valid ranges.</exception>
    /// <exception cref="H3DomainException">The resolution or coordinate was rejected by the native library.</exception>
    public static H3Index FromLatLng(LatLng latLng, int resolution)
    {
        latLng.Validate();
        var native = latLng.ToNative();
        H3ErrorMarshaller.ThrowIfError(NativeMethods.LatLngToCell(native, resolution, out ulong cell));
        return new H3Index(cell);
    }

    /// <summary>
    /// Returns the geographic center of this cell.
    /// </summary>
    /// <returns>The cell center, in degrees.</returns>
    /// <exception cref="H3InvalidIndexException">This is not a valid H3 cell.</exception>
    public LatLng ToLatLng()
    {
        H3ErrorMarshaller.ThrowIfError(NativeMethods.CellToLatLng(Value, out NativeLatLng native));
        return LatLng.FromNative(native);
    }

    /// <summary>
    /// Returns the boundary vertices of this cell in counter-clockwise order.
    /// </summary>
    /// <returns>The boundary vertices, in degrees (5 vertices for pentagons, 6 for hexagons, more when edges cross icosahedron faces).</returns>
    /// <exception cref="H3InvalidIndexException">This is not a valid H3 cell.</exception>
    public IReadOnlyList<LatLng> GetBoundary()
    {
        H3ErrorMarshaller.ThrowIfError(NativeMethods.CellToBoundary(Value, out CellBoundary boundary));

        int count = boundary.NumVerts;
        var result = new LatLng[count];
        for (int i = 0; i < count; i++)
        {
            result[i] = LatLng.FromNative(boundary.Verts[i]);
        }

        return result;
    }

    /// <summary>
    /// Returns all cells within grid distance <paramref name="k"/> of this cell
    /// (the cell itself plus its <paramref name="k"/>-ring neighborhood).
    /// </summary>
    /// <param name="k">The grid distance (number of rings); must be non-negative.</param>
    /// <returns>The cells in the k-ring, with null padding slots removed. Order is not guaranteed.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="k"/> is negative, or so large that the result would exceed <see cref="Array.MaxLength"/>.</exception>
    /// <exception cref="H3Exception">The native operation failed.</exception>
    public unsafe H3Index[] GridDisk(int k)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(k);

        H3ErrorMarshaller.ThrowIfError(NativeMethods.MaxGridDiskSize(k, out long maxSize));
        if (maxSize <= 0)
        {
            return [];
        }

        // Upstream maxGridDiskSize clamps very large k to the cell count at res 15
        // (~5.7e11) without erroring, which would overflow any .NET array allocation.
        if (maxSize > Array.MaxLength)
        {
            throw new ArgumentOutOfRangeException(
                nameof(k),
                k,
                $"k={k} would require {maxSize} cells, exceeding the maximum array length.");
        }

        var buffer = new ulong[maxSize];
        fixed (ulong* ptr = buffer)
        {
            H3ErrorMarshaller.ThrowIfError(NativeMethods.GridDisk(Value, k, ptr));
        }

        int count = 0;
        for (long i = 0; i < maxSize; i++)
        {
            if (buffer[i] != 0)
            {
                count++;
            }
        }

        var result = new H3Index[count];
        int next = 0;
        for (long i = 0; i < maxSize; i++)
        {
            if (buffer[i] != 0)
            {
                result[next++] = new H3Index(buffer[i]);
            }
        }

        return result;
    }

    /// <summary>
    /// Fills <paramref name="destination"/> with the cells within grid distance
    /// <paramref name="k"/> of this cell, packing non-null cells to the front.
    /// </summary>
    /// <param name="k">The grid distance (number of rings); must be non-negative.</param>
    /// <param name="destination">
    /// The destination span. Its length must be at least the maximum grid-disk size
    /// for <paramref name="k"/> (see the upstream <c>maxGridDiskSize</c>).
    /// </param>
    /// <returns>The number of non-null cells written to the front of <paramref name="destination"/>.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="k"/> is negative, so large that the result would exceed <see cref="Array.MaxLength"/>, or <paramref name="destination"/> is too small.</exception>
    /// <exception cref="H3Exception">The native operation failed.</exception>
    public unsafe int GridDiskInto(int k, Span<H3Index> destination)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(k);

        H3ErrorMarshaller.ThrowIfError(NativeMethods.MaxGridDiskSize(k, out long maxSize));

        // Upstream maxGridDiskSize clamps very large k to the cell count at res 15
        // (~5.7e11) without erroring, which no .NET span could ever satisfy.
        if (maxSize > Array.MaxLength)
        {
            throw new ArgumentOutOfRangeException(
                nameof(k),
                k,
                $"k={k} would require {maxSize} cells, exceeding the maximum array length.");
        }

        if (destination.Length < maxSize)
        {
            throw new ArgumentOutOfRangeException(
                nameof(destination),
                destination.Length,
                $"Destination span must hold at least {maxSize} elements for k={k}.");
        }

        if (maxSize <= 0)
        {
            return 0;
        }

        // Write directly into the caller's span: H3Index is blittable and layout-compatible
        // with ulong, so the native fill needs no intermediate buffer.
        fixed (H3Index* ptr = destination)
        {
            H3ErrorMarshaller.ThrowIfError(NativeMethods.GridDisk(Value, k, (ulong*)ptr));
        }

        // Compact the H3_NULL (0) padding holes in place. The write index never
        // outruns the read index, so no live entry is clobbered.
        int count = 0;
        for (long i = 0; i < maxSize; i++)
        {
            if (destination[(int)i].Value != 0)
            {
                destination[count++] = destination[(int)i];
            }
        }

        return count;
    }

    /// <summary>
    /// Returns the stored indexing digit (0-7) at the given resolution.
    /// </summary>
    /// <param name="resolution">
    /// The 1-indexed resolution (1-15) of the digit to read. Resolution 0 is the base
    /// cell, not a digit. The resolution may exceed this cell's own resolution, in
    /// which case the stored digit (7 for a valid cell) is returned.
    /// </param>
    /// <returns>The indexing digit (0-7) at <paramref name="resolution"/>.</returns>
    /// <exception cref="H3DomainException"><paramref name="resolution"/> is outside the range 1-15.</exception>
    public int GetIndexDigit(int resolution)
    {
        H3ErrorMarshaller.ThrowIfError(NativeMethods.GetIndexDigit(Value, resolution, out int digit));
        return digit;
    }

    /// <summary>
    /// Constructs an H3 cell from its base cell number and per-resolution indexing digits.
    /// </summary>
    /// <param name="resolution">The target resolution (0-15).</param>
    /// <param name="baseCellNumber">The base cell number (0-121).</param>
    /// <param name="digits">
    /// The indexing digits (each 0-6), one per resolution. Its length must equal
    /// <paramref name="resolution"/>.
    /// </param>
    /// <returns>The constructed <see cref="H3Index"/>.</returns>
    /// <exception cref="ArgumentException">The length of <paramref name="digits"/> does not equal <paramref name="resolution"/>.</exception>
    /// <exception cref="H3DomainException">The resolution, base cell number, or a digit was rejected by the native library (including a pentagon base cell with a leading K-axis digit).</exception>
    public static H3Index Construct(int resolution, int baseCellNumber, ReadOnlySpan<int> digits)
    {
        if (digits.Length != resolution)
        {
            throw new ArgumentException(
                $"digits length ({digits.Length}) must equal resolution ({resolution}).",
                nameof(digits));
        }

        unsafe
        {
            fixed (int* p = digits)
            {
                H3ErrorMarshaller.ThrowIfError(NativeMethods.ConstructCell(resolution, baseCellNumber, p, out ulong cell));
                return new H3Index(cell);
            }
        }
    }

    /// <summary>
    /// Returns the icosahedron face numbers (0-19) that this cell intersects.
    /// </summary>
    /// <returns>The distinct face numbers the cell touches (1-2 for hexagons, 5 for pentagons).</returns>
    /// <exception cref="H3InvalidIndexException">This is not a valid H3 cell.</exception>
    /// <exception cref="H3Exception">The native operation failed.</exception>
    public int[] GetIcosahedronFaces()
    {
        EnsureValidCell();

        int maxCount = MaxFaceCount();
        var buffer = new int[maxCount];
        unsafe
        {
            fixed (int* ptr = buffer)
            {
                H3ErrorMarshaller.ThrowIfError(NativeMethods.GetIcosahedronFaces(Value, ptr));
            }
        }

        int count = 0;
        for (int i = 0; i < maxCount; i++)
        {
            if (buffer[i] != InvalidFace)
            {
                count++;
            }
        }

        var result = new int[count];
        int next = 0;
        for (int i = 0; i < maxCount; i++)
        {
            if (buffer[i] != InvalidFace)
            {
                result[next++] = buffer[i];
            }
        }

        return result;
    }

    /// <summary>
    /// Fills <paramref name="destination"/> with the icosahedron face numbers (0-19)
    /// that this cell intersects, packing the valid faces to the front.
    /// </summary>
    /// <param name="destination">
    /// The destination span. Its length must be at least the maximum face count for
    /// this cell (2 for hexagons, 5 for pentagons).
    /// </param>
    /// <returns>The number of distinct faces written to the front of <paramref name="destination"/>.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="destination"/> is too small.</exception>
    /// <exception cref="H3InvalidIndexException">This is not a valid H3 cell.</exception>
    /// <exception cref="H3Exception">The native operation failed.</exception>
    public int GetIcosahedronFacesInto(Span<int> destination)
    {
        EnsureValidCell();

        int maxCount = MaxFaceCount();
        if (destination.Length < maxCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(destination),
                destination.Length,
                $"Destination span must hold at least {maxCount} elements for this cell.");
        }

        unsafe
        {
            fixed (int* ptr = destination)
            {
                H3ErrorMarshaller.ThrowIfError(NativeMethods.GetIcosahedronFaces(Value, ptr));
            }
        }

        // Compact the INVALID_FACE (-1) padding holes in place; 0 is a valid face.
        int count = 0;
        for (int i = 0; i < maxCount; i++)
        {
            if (destination[i] != InvalidFace)
            {
                destination[count++] = destination[i];
            }
        }

        return count;
    }

    /// <summary>
    /// Parses an H3 index from its hexadecimal string representation using the native
    /// library. Unlike <see cref="Parse(string)"/>, an unparseable string surfaces as
    /// an <see cref="H3Exception"/> rather than a <see cref="FormatException"/>.
    /// </summary>
    /// <param name="value">The hexadecimal index string.</param>
    /// <returns>The parsed <see cref="H3Index"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is <see langword="null"/>.</exception>
    /// <exception cref="H3Exception"><paramref name="value"/> could not be parsed by the native library.</exception>
    public static H3Index FromString(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        H3ErrorMarshaller.ThrowIfError(NativeMethods.StringToH3(value, out ulong cell));
        return new H3Index(cell);
    }

    /// <summary>
    /// Attempts to parse an H3 index from its hexadecimal string representation using
    /// the native library.
    /// </summary>
    /// <param name="value">The hexadecimal index string.</param>
    /// <param name="result">When this method returns <see langword="true"/>, the parsed index; otherwise <see cref="Null"/>.</param>
    /// <returns><see langword="true"/> if the native library parsed the string; otherwise <see langword="false"/>.</returns>
    public static bool TryFromString([NotNullWhen(true)] string? value, out H3Index result)
    {
        result = Null;
        if (value is null)
        {
            return false;
        }

        if (NativeMethods.StringToH3(value, out ulong cell) != H3ErrorCode.Success)
        {
            return false;
        }

        result = new H3Index(cell);
        return true;
    }

    /// <summary>Returns the lowercase, zero-padded 16-digit hexadecimal representation of this index.</summary>
    /// <returns>The 16-character hexadecimal index string.</returns>
    public override string ToString() => Value.ToString("x16", CultureInfo.InvariantCulture);

    /// <summary>
    /// Returns the native library's canonical hexadecimal representation of this index.
    /// Unlike <see cref="ToString()"/>, this is <b>not</b> zero-padded, so it is a
    /// variable-length lowercase hex string (the two forms differ only by leading zeros).
    /// </summary>
    /// <returns>The canonical (unpadded) lowercase hexadecimal index string.</returns>
    /// <exception cref="H3Exception">The native operation failed.</exception>
    public string ToCanonicalString()
    {
        Span<byte> buf = stackalloc byte[17];
        unsafe
        {
            fixed (byte* p = buf)
            {
                H3ErrorMarshaller.ThrowIfError(NativeMethods.H3ToString(Value, p, (nuint)buf.Length));
            }
        }

        int len = buf.IndexOf((byte)0);
        return Encoding.ASCII.GetString(buf[..(len < 0 ? buf.Length : len)]);
    }

    /// <summary>
    /// Parses an H3 index from its hexadecimal string representation.
    /// </summary>
    /// <param name="value">The hexadecimal index string (with or without a leading "0x").</param>
    /// <returns>The parsed <see cref="H3Index"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is <see langword="null"/>.</exception>
    /// <exception cref="FormatException"><paramref name="value"/> is not valid hexadecimal.</exception>
    public static H3Index Parse(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (!TryParse(value, out H3Index result))
        {
            throw new FormatException($"'{value}' is not a valid hexadecimal H3 index.");
        }

        return result;
    }

    /// <summary>
    /// Attempts to parse an H3 index from its hexadecimal string representation.
    /// </summary>
    /// <param name="value">The hexadecimal index string (with or without a leading "0x").</param>
    /// <param name="result">When this method returns <see langword="true"/>, the parsed index; otherwise <see cref="Null"/>.</param>
    /// <returns><see langword="true"/> if parsing succeeded; otherwise <see langword="false"/>.</returns>
    public static bool TryParse([NotNullWhen(true)] string? value, out H3Index result)
    {
        result = Null;
        if (string.IsNullOrEmpty(value))
        {
            return false;
        }

        ReadOnlySpan<char> span = value;
        if (span.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            span = span[2..];
        }

        if (!ulong.TryParse(span, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ulong parsed))
        {
            return false;
        }

        result = new H3Index(parsed);
        return true;
    }

    #region Hierarchy

    /// <summary>
    /// Returns the parent cell of this cell at the given coarser resolution.
    /// </summary>
    /// <param name="parentResolution">The target parent resolution (0-15); must be less than or equal to this cell's resolution.</param>
    /// <returns>The parent <see cref="H3Index"/> at <paramref name="parentResolution"/>.</returns>
    /// <exception cref="H3DomainException"><paramref name="parentResolution"/> is outside the range 0-15.</exception>
    /// <exception cref="H3Exception"><paramref name="parentResolution"/> is finer than this cell's resolution, or the native operation failed.</exception>
    public H3Index CellToParent(int parentResolution)
    {
        H3ErrorMarshaller.ThrowIfError(NativeMethods.CellToParent(Value, parentResolution, out ulong parent));
        return new H3Index(parent);
    }

    /// <summary>
    /// Returns the center child cell of this cell at the given finer resolution.
    /// </summary>
    /// <param name="childResolution">The target child resolution (0-15); must be greater than or equal to this cell's resolution.</param>
    /// <returns>The center child <see cref="H3Index"/> at <paramref name="childResolution"/>.</returns>
    /// <exception cref="H3DomainException"><paramref name="childResolution"/> is outside the range 0-15.</exception>
    /// <exception cref="H3Exception"><paramref name="childResolution"/> is coarser than this cell's resolution, or the native operation failed.</exception>
    public H3Index CellToCenterChild(int childResolution)
    {
        H3ErrorMarshaller.ThrowIfError(NativeMethods.CellToCenterChild(Value, childResolution, out ulong child));
        return new H3Index(child);
    }

    /// <summary>
    /// Returns the position of this cell within an ordered traversal of all children
    /// of its parent at the given coarser resolution. Inverse of <see cref="ChildPosToCell"/>.
    /// </summary>
    /// <param name="parentResolution">The parent resolution (0-15); must be less than or equal to this cell's resolution.</param>
    /// <returns>The zero-based child position of this cell within its parent at <paramref name="parentResolution"/>.</returns>
    /// <exception cref="H3DomainException"><paramref name="parentResolution"/> is outside the range 0-15.</exception>
    /// <exception cref="H3Exception"><paramref name="parentResolution"/> is finer than this cell's resolution, or the native operation failed.</exception>
    public long CellToChildPos(int parentResolution)
    {
        H3ErrorMarshaller.ThrowIfError(NativeMethods.CellToChildPos(Value, parentResolution, out long pos));
        return pos;
    }

    /// <summary>
    /// Returns the child cell of this (parent) cell at the given child position and
    /// resolution. Inverse of <see cref="CellToChildPos"/>.
    /// </summary>
    /// <param name="childPosition">The zero-based child position, in <c>[0, cellToChildrenSize)</c>.</param>
    /// <param name="childResolution">The child resolution (0-15); must be greater than or equal to this cell's resolution.</param>
    /// <returns>The child <see cref="H3Index"/> at <paramref name="childPosition"/> and <paramref name="childResolution"/>.</returns>
    /// <exception cref="H3DomainException"><paramref name="childResolution"/> is outside the range 0-15, or <paramref name="childPosition"/> is outside the valid range.</exception>
    /// <exception cref="H3Exception"><paramref name="childResolution"/> is coarser than this cell's resolution, or the native operation failed.</exception>
    public H3Index ChildPosToCell(long childPosition, int childResolution)
    {
        H3ErrorMarshaller.ThrowIfError(NativeMethods.ChildPosToCell(childPosition, Value, childResolution, out ulong child));
        return new H3Index(child);
    }

    /// <summary>
    /// Returns all child cells of this cell at the given finer resolution.
    /// </summary>
    /// <param name="childResolution">The target child resolution (0-15); must be greater than or equal to this cell's resolution.</param>
    /// <returns>The child cells, with null padding slots removed (pentagon parents yield fewer than the hexagon maximum). Order is not guaranteed.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="childResolution"/> implies a result so large that it would exceed <see cref="Array.MaxLength"/>.</exception>
    /// <exception cref="H3DomainException"><paramref name="childResolution"/> is outside the range 0-15.</exception>
    /// <exception cref="H3Exception"><paramref name="childResolution"/> is coarser than this cell's resolution, or the native operation failed.</exception>
    public unsafe H3Index[] CellToChildren(int childResolution)
    {
        long maxSize = CellToChildrenSize(childResolution);
        if (maxSize <= 0)
        {
            return [];
        }

        if (maxSize > Array.MaxLength)
        {
            throw new ArgumentOutOfRangeException(
                nameof(childResolution),
                childResolution,
                $"childResolution={childResolution} would require {maxSize} cells, exceeding the maximum array length.");
        }

        var buffer = new ulong[maxSize];
        fixed (ulong* ptr = buffer)
        {
            H3ErrorMarshaller.ThrowIfError(NativeMethods.CellToChildren(Value, childResolution, ptr));
        }

        int count = 0;
        for (long i = 0; i < maxSize; i++)
        {
            if (buffer[i] != 0)
            {
                count++;
            }
        }

        var result = new H3Index[count];
        int next = 0;
        for (long i = 0; i < maxSize; i++)
        {
            if (buffer[i] != 0)
            {
                result[next++] = new H3Index(buffer[i]);
            }
        }

        return result;
    }

    /// <summary>
    /// Fills <paramref name="destination"/> with the child cells of this cell at the
    /// given finer resolution, packing non-null cells to the front.
    /// </summary>
    /// <param name="childResolution">The target child resolution (0-15); must be greater than or equal to this cell's resolution.</param>
    /// <param name="destination">
    /// The destination span. Its length must be at least the maximum child count for
    /// <paramref name="childResolution"/> (see the upstream <c>cellToChildrenSize</c>).
    /// </param>
    /// <returns>The number of non-null child cells written to the front of <paramref name="destination"/>.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="childResolution"/> implies a result that would exceed <see cref="Array.MaxLength"/>, or <paramref name="destination"/> is too small.</exception>
    /// <exception cref="H3DomainException"><paramref name="childResolution"/> is outside the range 0-15.</exception>
    /// <exception cref="H3Exception"><paramref name="childResolution"/> is coarser than this cell's resolution, or the native operation failed.</exception>
    public unsafe int CellToChildrenInto(int childResolution, Span<H3Index> destination)
    {
        long maxSize = CellToChildrenSize(childResolution);

        if (maxSize > Array.MaxLength)
        {
            throw new ArgumentOutOfRangeException(
                nameof(childResolution),
                childResolution,
                $"childResolution={childResolution} would require {maxSize} cells, exceeding the maximum array length.");
        }

        if (destination.Length < maxSize)
        {
            throw new ArgumentOutOfRangeException(
                nameof(destination),
                destination.Length,
                $"Destination span must hold at least {maxSize} elements for childResolution={childResolution}.");
        }

        if (maxSize <= 0)
        {
            return 0;
        }

        // Write directly into the caller's span: H3Index is blittable and layout-compatible
        // with ulong, so the native fill needs no intermediate buffer.
        fixed (H3Index* ptr = destination)
        {
            H3ErrorMarshaller.ThrowIfError(NativeMethods.CellToChildren(Value, childResolution, (ulong*)ptr));
        }

        // Compact the H3_NULL (0) padding holes in place. The write index never
        // outruns the read index, so no live entry is clobbered.
        int count = 0;
        for (long i = 0; i < maxSize; i++)
        {
            if (destination[(int)i].Value != 0)
            {
                destination[count++] = destination[(int)i];
            }
        }

        return count;
    }

    /// <summary>
    /// Compacts a set of cells into the smaller, mixed-resolution set of cells that
    /// covers the same area. Inverse of <see cref="UncompactCells(ReadOnlySpan{H3Index}, int)"/>.
    /// </summary>
    /// <param name="cells">The cells to compact. All must be the same resolution and duplicate-free.</param>
    /// <returns>The compacted, mixed-resolution set of cells. Order is not guaranteed.</returns>
    /// <exception cref="H3Exception">The input contains mixed resolutions or duplicates, or the native operation failed.</exception>
    public static unsafe H3Index[] CompactCells(ReadOnlySpan<H3Index> cells)
    {
        if (cells.Length == 0)
        {
            return [];
        }

        // `new ulong[]` is CLR zero-initialized, so the trailing slots the native
        // compactCells leaves untouched read as H3_NULL(0) and the strip below is valid.
        // A future switch to a non-cleared buffer (e.g. ArrayPool) MUST pre-clear it.
        var buffer = new ulong[cells.Length];
        fixed (H3Index* inPtr = cells)
        fixed (ulong* outPtr = buffer)
        {
            H3ErrorMarshaller.ThrowIfError(NativeMethods.CompactCells((ulong*)inPtr, outPtr, cells.Length));
        }

        int count = 0;
        for (int i = 0; i < buffer.Length; i++)
        {
            if (buffer[i] != 0)
            {
                count++;
            }
        }

        var result = new H3Index[count];
        int next = 0;
        for (int i = 0; i < buffer.Length; i++)
        {
            if (buffer[i] != 0)
            {
                result[next++] = new H3Index(buffer[i]);
            }
        }

        return result;
    }

    /// <summary>
    /// Compacts a set of cells into <paramref name="destination"/>, packing non-null
    /// cells to the front. Inverse of <see cref="UncompactCellsInto"/>.
    /// </summary>
    /// <param name="cells">The cells to compact. All must be the same resolution and duplicate-free.</param>
    /// <param name="destination">The destination span. Its length must be at least <paramref name="cells"/>.Length (compaction never grows the set).</param>
    /// <returns>The number of cells written to the front of <paramref name="destination"/>.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="destination"/> is too small.</exception>
    /// <exception cref="H3Exception">The input contains mixed resolutions or duplicates, or the native operation failed.</exception>
    public static unsafe int CompactCellsInto(ReadOnlySpan<H3Index> cells, Span<H3Index> destination)
    {
        if (destination.Length < cells.Length)
        {
            throw new ArgumentOutOfRangeException(
                nameof(destination),
                destination.Length,
                $"Destination span must hold at least {cells.Length} elements.");
        }

        if (cells.Length == 0)
        {
            return 0;
        }

        // The native compactCells writes its result strictly front-to-back and leaves the
        // trailing slots untouched -- it does NOT H3_NULL-pad the unused tail. The array
        // overload survives this only because `new ulong[]` is CLR zero-initialized; a
        // caller-supplied span may carry stale data. Pre-clear the scanned window so the
        // unwritten tail reads as H3_NULL(0) and the strip below counts only real cells.
        destination[..cells.Length].Clear();

        fixed (H3Index* inPtr = cells)
        fixed (H3Index* outPtr = destination)
        {
            H3ErrorMarshaller.ThrowIfError(NativeMethods.CompactCells((ulong*)inPtr, (ulong*)outPtr, cells.Length));
        }

        // Compact the H3_NULL (0) padding holes in place. The native body fills the
        // output front-to-back, so the write index never outruns the read index.
        int count = 0;
        for (int i = 0; i < cells.Length; i++)
        {
            if (destination[i].Value != 0)
            {
                destination[count++] = destination[i];
            }
        }

        return count;
    }

    /// <summary>
    /// Uncompacts a mixed-resolution set of cells to the equivalent set at the given
    /// uniform resolution. Inverse of <see cref="CompactCells"/>.
    /// </summary>
    /// <param name="cells">The compacted, mixed-resolution cells.</param>
    /// <param name="resolution">The target uniform resolution (0-15); must be greater than or equal to the finest resolution present in <paramref name="cells"/>.</param>
    /// <returns>The uncompacted set of cells, all at <paramref name="resolution"/>. Order is not guaranteed.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="resolution"/> implies a result so large that it would exceed <see cref="Array.MaxLength"/>.</exception>
    /// <exception cref="H3DomainException"><paramref name="resolution"/> is outside the range 0-15.</exception>
    /// <exception cref="H3Exception"><paramref name="resolution"/> is finer than the input requires, or the native operation failed.</exception>
    public static unsafe H3Index[] UncompactCells(ReadOnlySpan<H3Index> cells, int resolution)
    {
        if (cells.Length == 0)
        {
            return [];
        }

        long maxOut = UncompactCellsSize(cells, resolution);
        if (maxOut <= 0)
        {
            return [];
        }

        if (maxOut > Array.MaxLength)
        {
            throw new ArgumentOutOfRangeException(
                nameof(resolution),
                resolution,
                $"resolution={resolution} would require {maxOut} cells, exceeding the maximum array length.");
        }

        var buffer = new ulong[maxOut];
        fixed (H3Index* inPtr = cells)
        fixed (ulong* outPtr = buffer)
        {
            H3ErrorMarshaller.ThrowIfError(NativeMethods.UncompactCells((ulong*)inPtr, cells.Length, outPtr, maxOut, resolution));
        }

        int count = 0;
        for (long i = 0; i < maxOut; i++)
        {
            if (buffer[i] != 0)
            {
                count++;
            }
        }

        var result = new H3Index[count];
        int next = 0;
        for (long i = 0; i < maxOut; i++)
        {
            if (buffer[i] != 0)
            {
                result[next++] = new H3Index(buffer[i]);
            }
        }

        return result;
    }

    /// <summary>
    /// Uncompacts a mixed-resolution set of cells into <paramref name="destination"/>
    /// at the given uniform resolution, packing non-null cells to the front. Inverse
    /// of <see cref="CompactCellsInto"/>.
    /// </summary>
    /// <param name="cells">The compacted, mixed-resolution cells.</param>
    /// <param name="resolution">The target uniform resolution (0-15); must be greater than or equal to the finest resolution present in <paramref name="cells"/>.</param>
    /// <param name="destination">
    /// The destination span. Its length must be at least the uncompacted size for
    /// <paramref name="resolution"/> (see the upstream <c>uncompactCellsSize</c>).
    /// </param>
    /// <returns>The number of cells written to the front of <paramref name="destination"/>.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="resolution"/> implies a result that would exceed <see cref="Array.MaxLength"/>, or <paramref name="destination"/> is too small.</exception>
    /// <exception cref="H3DomainException"><paramref name="resolution"/> is outside the range 0-15.</exception>
    /// <exception cref="H3Exception"><paramref name="resolution"/> is finer than the input requires, or the native operation failed.</exception>
    public static unsafe int UncompactCellsInto(ReadOnlySpan<H3Index> cells, int resolution, Span<H3Index> destination)
    {
        if (cells.Length == 0)
        {
            return 0;
        }

        long maxOut = UncompactCellsSize(cells, resolution);

        if (maxOut > Array.MaxLength)
        {
            throw new ArgumentOutOfRangeException(
                nameof(resolution),
                resolution,
                $"resolution={resolution} would require {maxOut} cells, exceeding the maximum array length.");
        }

        if (destination.Length < maxOut)
        {
            throw new ArgumentOutOfRangeException(
                nameof(destination),
                destination.Length,
                $"Destination span must hold at least {maxOut} elements for resolution={resolution}.");
        }

        if (maxOut <= 0)
        {
            return 0;
        }

        fixed (H3Index* inPtr = cells)
        fixed (H3Index* outPtr = destination)
        {
            H3ErrorMarshaller.ThrowIfError(NativeMethods.UncompactCells((ulong*)inPtr, cells.Length, (ulong*)outPtr, maxOut, resolution));
        }

        // Compact the H3_NULL (0) padding holes in place. The native body fills the
        // output front-to-back, so the write index never outruns the read index.
        int count = 0;
        for (long i = 0; i < maxOut; i++)
        {
            if (destination[(int)i].Value != 0)
            {
                destination[count++] = destination[(int)i];
            }
        }

        return count;
    }

    #endregion Hierarchy

    #region Grid traversal

    /// <summary>
    /// Returns the cells forming the hollow grid ring at exactly grid distance
    /// <paramref name="k"/> from this cell (the boundary of the k-ring, excluding the
    /// interior). For <paramref name="k"/> = 0 the result is this cell itself.
    /// </summary>
    /// <param name="k">The grid distance (ring radius); must be non-negative.</param>
    /// <returns>The cells on the ring, with null padding slots removed (pentagon distortion can leave holes). Order is not guaranteed.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="k"/> is negative, or so large that the result would exceed <see cref="Array.MaxLength"/>.</exception>
    /// <exception cref="H3InvalidIndexException">This is not a valid H3 cell.</exception>
    /// <exception cref="H3Exception">The native operation failed.</exception>
    public unsafe H3Index[] GridRing(int k)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(k);

        long maxSize = MaxGridRingSize(k);
        if (maxSize <= 0)
        {
            return [];
        }

        if (maxSize > Array.MaxLength)
        {
            throw new ArgumentOutOfRangeException(
                nameof(k),
                k,
                $"k={k} would require {maxSize} cells, exceeding the maximum array length.");
        }

        var buffer = new ulong[maxSize];
        fixed (ulong* ptr = buffer)
        {
            H3ErrorMarshaller.ThrowIfError(NativeMethods.GridRing(Value, k, ptr));
        }

        int count = 0;
        for (long i = 0; i < maxSize; i++)
        {
            if (buffer[i] != 0)
            {
                count++;
            }
        }

        var result = new H3Index[count];
        int next = 0;
        for (long i = 0; i < maxSize; i++)
        {
            if (buffer[i] != 0)
            {
                result[next++] = new H3Index(buffer[i]);
            }
        }

        return result;
    }

    /// <summary>
    /// Fills <paramref name="destination"/> with the cells forming the hollow grid ring
    /// at exactly grid distance <paramref name="k"/> from this cell, packing non-null
    /// cells to the front.
    /// </summary>
    /// <param name="k">The grid distance (ring radius); must be non-negative.</param>
    /// <param name="destination">
    /// The destination span. Its length must be at least the maximum grid-ring size for
    /// <paramref name="k"/> (see the upstream <c>maxGridRingSize</c>).
    /// </param>
    /// <returns>The number of non-null cells written to the front of <paramref name="destination"/>.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="k"/> is negative, so large that the result would exceed <see cref="Array.MaxLength"/>, or <paramref name="destination"/> is too small.</exception>
    /// <exception cref="H3InvalidIndexException">This is not a valid H3 cell.</exception>
    /// <exception cref="H3Exception">The native operation failed.</exception>
    public unsafe int GridRingInto(int k, Span<H3Index> destination)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(k);

        long maxSize = MaxGridRingSize(k);

        if (maxSize > Array.MaxLength)
        {
            throw new ArgumentOutOfRangeException(
                nameof(k),
                k,
                $"k={k} would require {maxSize} cells, exceeding the maximum array length.");
        }

        if (destination.Length < maxSize)
        {
            throw new ArgumentOutOfRangeException(
                nameof(destination),
                destination.Length,
                $"Destination span must hold at least {maxSize} elements for k={k}.");
        }

        if (maxSize <= 0)
        {
            return 0;
        }

        // Native gridRing does NOT guarantee zero-padding the tail (pentagon holes), and a
        // caller-supplied span may carry stale data, so pre-clear before the fill + strip.
        destination[..(int)maxSize].Clear();

        // Write directly into the caller's span: H3Index is blittable and layout-compatible
        // with ulong, so the native fill needs no intermediate buffer.
        fixed (H3Index* ptr = destination)
        {
            H3ErrorMarshaller.ThrowIfError(NativeMethods.GridRing(Value, k, (ulong*)ptr));
        }

        // Compact the H3_NULL (0) padding holes in place. The write index never
        // outruns the read index, so no live entry is clobbered.
        int count = 0;
        for (long i = 0; i < maxSize; i++)
        {
            if (destination[(int)i].Value != 0)
            {
                destination[count++] = destination[(int)i];
            }
        }

        return count;
    }

    /// <summary>
    /// Returns the line of cells forming the minimal-length grid path from this cell
    /// (inclusive) to <paramref name="destination"/> (inclusive).
    /// </summary>
    /// <param name="destination">The end cell of the path; must be the same resolution as this cell.</param>
    /// <returns>
    /// The ordered path of cells, where the first element is this cell and the last is
    /// <paramref name="destination"/>. The path is not necessarily unique near pentagons.
    /// </returns>
    /// <exception cref="ArgumentOutOfRangeException">The path would exceed <see cref="Array.MaxLength"/>.</exception>
    /// <exception cref="H3Exception">The endpoints are too far apart, are different resolutions, or the path crosses a pentagon or the antimeridian.</exception>
    public unsafe H3Index[] GridPathCells(H3Index destination)
    {
        long size = GridPathCellsSize(this, destination);
        if (size <= 0)
        {
            return [];
        }

        if (size > Array.MaxLength)
        {
            throw new ArgumentOutOfRangeException(
                nameof(destination),
                destination,
                $"The path would require {size} cells, exceeding the maximum array length.");
        }

        var result = new H3Index[size];
        // gridPathCellsSize returns the EXACT length, so the native fill writes every
        // slot and no H3_NULL strip is required; copy straight through.
        fixed (H3Index* ptr = result)
        {
            H3ErrorMarshaller.ThrowIfError(NativeMethods.GridPathCells(Value, destination.Value, (ulong*)ptr));
        }

        return result;
    }

    /// <summary>
    /// Fills <paramref name="result"/> with the line of cells forming the minimal-length
    /// grid path from this cell (inclusive) to <paramref name="destination"/> (inclusive).
    /// </summary>
    /// <param name="destination">The end cell of the path; must be the same resolution as this cell.</param>
    /// <param name="result">
    /// The destination span. Its length must be at least the exact path size (see the
    /// upstream <c>gridPathCellsSize</c>); the first written element is this cell and the
    /// last is <paramref name="destination"/>.
    /// </param>
    /// <returns>The number of cells written to the front of <paramref name="result"/> (the exact path length).</returns>
    /// <exception cref="ArgumentOutOfRangeException">The path would exceed <see cref="Array.MaxLength"/>, or <paramref name="result"/> is too small.</exception>
    /// <exception cref="H3Exception">The endpoints are too far apart, are different resolutions, or the path crosses a pentagon or the antimeridian.</exception>
    public unsafe int GridPathCellsInto(H3Index destination, Span<H3Index> result)
    {
        long size = GridPathCellsSize(this, destination);

        if (size > Array.MaxLength)
        {
            throw new ArgumentOutOfRangeException(
                nameof(destination),
                destination,
                $"The path would require {size} cells, exceeding the maximum array length.");
        }

        if (result.Length < size)
        {
            throw new ArgumentOutOfRangeException(
                nameof(result),
                result.Length,
                $"Result span must hold at least {size} elements for this path.");
        }

        if (size <= 0)
        {
            return 0;
        }

        // Exact size: the native fill writes every slot front-to-back, so this is a
        // straight copy with no H3_NULL strip and no pre-clear required.
        fixed (H3Index* ptr = result)
        {
            H3ErrorMarshaller.ThrowIfError(NativeMethods.GridPathCells(Value, destination.Value, (ulong*)ptr));
        }

        return (int)size;
    }

    /// <summary>
    /// Returns the grid distance (number of steps along the H3 grid) between this cell
    /// and <paramref name="other"/>. The metric is symmetric and reflexive (a cell's
    /// distance to itself is 0).
    /// </summary>
    /// <param name="other">The other cell; must be the same resolution as this cell.</param>
    /// <returns>The grid distance between the two cells.</returns>
    /// <exception cref="H3InvalidIndexException">Either cell is not a valid H3 cell.</exception>
    /// <exception cref="H3Exception">The cells are different resolutions, are too far apart, or pentagon distortion prevents a finite distance.</exception>
    public long GridDistance(H3Index other)
    {
        H3ErrorMarshaller.ThrowIfError(NativeMethods.GridDistance(Value, other.Value, out long distance));
        return distance;
    }

    /// <summary>
    /// Returns the local IJ coordinates of <paramref name="target"/> relative to this
    /// cell as the anchoring origin.
    /// </summary>
    /// <param name="target">The cell to locate relative to this origin.</param>
    /// <returns>The local IJ coordinates of <paramref name="target"/>.</returns>
    /// <remarks>
    /// Local IJ coordinates are only meaningful relative to this origin and are not
    /// guaranteed to be defined across pentagons or the antimeridian.
    /// </remarks>
    /// <exception cref="H3InvalidIndexException">Either cell is not a valid H3 cell.</exception>
    /// <exception cref="H3Exception">The cells are different resolutions, <paramref name="target"/> is too far from the origin, or pentagon distortion prevents a local coordinate.</exception>
    public CoordIJ CellToLocalIJ(H3Index target)
    {
        // mode is reserved (only 0 is defined), so it is hidden and always passed as 0.
        H3ErrorMarshaller.ThrowIfError(NativeMethods.CellToLocalIj(Value, target.Value, 0u, out NativeCoordIJ native));
        return CoordIJ.FromNative(native);
    }

    /// <summary>
    /// Returns the cell at the given local IJ coordinates relative to this cell as the
    /// anchoring origin. Inverse of <see cref="CellToLocalIJ"/>.
    /// </summary>
    /// <param name="ij">The local IJ coordinates relative to this origin.</param>
    /// <returns>The cell at <paramref name="ij"/> relative to this origin.</returns>
    /// <remarks>
    /// This is the inverse of <see cref="CellToLocalIJ"/>, but the round trip is not
    /// guaranteed invertible near pentagons or the antimeridian.
    /// </remarks>
    /// <exception cref="H3InvalidIndexException">This origin is not a valid H3 cell.</exception>
    /// <exception cref="H3Exception">The IJ coordinates do not resolve to a cell near this origin.</exception>
    public H3Index LocalIJToCell(CoordIJ ij)
    {
        // mode is reserved (only 0 is defined), so it is hidden and always passed as 0.
        var native = ij.ToNative();
        H3ErrorMarshaller.ThrowIfError(NativeMethods.LocalIjToCell(Value, in native, 0u, out ulong cell));
        return new H3Index(cell);
    }

    /// <summary>
    /// Returns all cells within grid distance <paramref name="k"/> of this cell paired
    /// with their grid distance from this cell, as two parallel arrays where index
    /// <c>i</c> in <c>Cells</c> corresponds to index <c>i</c> in <c>Distances</c>.
    /// </summary>
    /// <param name="k">The grid distance (number of rings); must be non-negative.</param>
    /// <returns>
    /// A tuple of parallel arrays: the cells in the k-ring (this cell at distance 0) and
    /// each cell's grid distance in <c>[0, k]</c>, with null padding slots removed. Order
    /// is not guaranteed.
    /// </returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="k"/> is negative, or so large that the result would exceed <see cref="Array.MaxLength"/>.</exception>
    /// <exception cref="H3Exception">The native operation failed.</exception>
    public unsafe (H3Index[] Cells, int[] Distances) GridDiskDistances(int k)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(k);

        H3ErrorMarshaller.ThrowIfError(NativeMethods.MaxGridDiskSize(k, out long maxSize));
        if (maxSize <= 0)
        {
            return ([], []);
        }

        if (maxSize > Array.MaxLength)
        {
            throw new ArgumentOutOfRangeException(
                nameof(k),
                k,
                $"k={k} would require {maxSize} cells, exceeding the maximum array length.");
        }

        var cellBuffer = new ulong[maxSize];
        var distanceBuffer = new int[maxSize];
        fixed (ulong* cellPtr = cellBuffer)
        fixed (int* distancePtr = distanceBuffer)
        {
            H3ErrorMarshaller.ThrowIfError(NativeMethods.GridDiskDistances(Value, k, cellPtr, distancePtr));
        }

        // The cells buffer is the sentinel channel: distances carries no sentinel (a real
        // distance can be 0 at the origin), so a slot is live iff its cell is non-null.
        int count = 0;
        for (long i = 0; i < maxSize; i++)
        {
            if (cellBuffer[i] != 0)
            {
                count++;
            }
        }

        var cells = new H3Index[count];
        var distances = new int[count];
        int next = 0;
        for (long i = 0; i < maxSize; i++)
        {
            if (cellBuffer[i] != 0)
            {
                cells[next] = new H3Index(cellBuffer[i]);
                distances[next] = distanceBuffer[i];
                next++;
            }
        }

        return (cells, distances);
    }

    /// <summary>
    /// Fills <paramref name="cells"/> and <paramref name="distances"/> with the cells
    /// within grid distance <paramref name="k"/> of this cell and their grid distances,
    /// packing the non-null entries in lockstep to the front of both spans.
    /// </summary>
    /// <param name="k">The grid distance (number of rings); must be non-negative.</param>
    /// <param name="cells">
    /// The destination span for the cells. Its length must be at least the maximum
    /// grid-disk size for <paramref name="k"/> (see the upstream <c>maxGridDiskSize</c>).
    /// </param>
    /// <param name="distances">
    /// The destination span for the per-cell grid distances; index <c>i</c> here
    /// corresponds to index <c>i</c> in <paramref name="cells"/>. Its length must be at
    /// least the same maximum grid-disk size.
    /// </param>
    /// <returns>The number of non-null entries written to the front of both spans.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="k"/> is negative, so large that the result would exceed <see cref="Array.MaxLength"/>, or either span is too small.</exception>
    /// <exception cref="H3Exception">The native operation failed.</exception>
    public unsafe int GridDiskDistancesInto(int k, Span<H3Index> cells, Span<int> distances)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(k);

        H3ErrorMarshaller.ThrowIfError(NativeMethods.MaxGridDiskSize(k, out long maxSize));

        if (maxSize > Array.MaxLength)
        {
            throw new ArgumentOutOfRangeException(
                nameof(k),
                k,
                $"k={k} would require {maxSize} cells, exceeding the maximum array length.");
        }

        if (cells.Length < maxSize)
        {
            throw new ArgumentOutOfRangeException(
                nameof(cells),
                cells.Length,
                $"Cells span must hold at least {maxSize} elements for k={k}.");
        }

        if (distances.Length < maxSize)
        {
            throw new ArgumentOutOfRangeException(
                nameof(distances),
                distances.Length,
                $"Distances span must hold at least {maxSize} elements for k={k}.");
        }

        if (maxSize <= 0)
        {
            return 0;
        }

        // The cells buffer is the H3_NULL sentinel channel. Native gridDiskDistances does
        // not guarantee zero-padding the tail and a caller span may carry stale data, so
        // pre-clear the cells window before the fill so empty slots read as H3_NULL(0).
        cells[..(int)maxSize].Clear();

        // H3Index is blittable/layout-compatible with ulong: native fills directly.
        fixed (H3Index* cellPtr = cells)
        fixed (int* distancePtr = distances)
        {
            H3ErrorMarshaller.ThrowIfError(NativeMethods.GridDiskDistances(Value, k, (ulong*)cellPtr, distancePtr));
        }

        // Compact both spans in lockstep using the cells buffer as the sentinel channel.
        // The write index never outruns the read index, so no live entry is clobbered.
        int count = 0;
        for (long i = 0; i < maxSize; i++)
        {
            if (cells[(int)i].Value != 0)
            {
                cells[count] = cells[(int)i];
                distances[count] = distances[(int)i];
                count++;
            }
        }

        return count;
    }

    #endregion Grid traversal

    #region Directed edges

    /// <summary>
    /// Returns a value indicating whether <paramref name="destination"/> is an adjacent
    /// neighbor of this cell.
    /// </summary>
    /// <param name="destination">The candidate neighbor cell; must be the same resolution as this cell.</param>
    /// <returns><see langword="true"/> if the two cells are adjacent neighbors; otherwise <see langword="false"/>.</returns>
    /// <exception cref="H3Exception">Either cell is not valid, or the cells are different resolutions.</exception>
    public bool IsNeighbor(H3Index destination)
    {
        H3ErrorMarshaller.ThrowIfError(NativeMethods.AreNeighborCells(Value, destination.Value, out int neighbor));
        return neighbor != 0;
    }

    /// <summary>
    /// Returns the directed edge from this cell to <paramref name="destination"/>.
    /// </summary>
    /// <param name="destination">The neighbor cell the edge points to; must be an adjacent neighbor at the same resolution.</param>
    /// <returns>The <see cref="H3DirectedEdge"/> from this cell to <paramref name="destination"/>.</returns>
    /// <exception cref="H3Exception">The two cells are not neighbors, or are different resolutions.</exception>
    public H3DirectedEdge DirectedEdgeTo(H3Index destination)
    {
        H3ErrorMarshaller.ThrowIfError(NativeMethods.CellsToDirectedEdge(Value, destination.Value, out ulong edge));
        return new H3DirectedEdge(edge);
    }

    /// <summary>
    /// Returns the directed edges originating at this cell, one per adjacent neighbor.
    /// </summary>
    /// <returns>The directed edges (6 for a hexagon, 5 for a pentagon). Order is not guaranteed.</returns>
    /// <exception cref="H3InvalidIndexException">This is not a valid H3 cell.</exception>
    /// <exception cref="H3Exception">The native operation failed.</exception>
    public unsafe H3DirectedEdge[] GetDirectedEdges()
    {
        // Native originToDirectedEdges does not validate its origin (it merely bit-flips the
        // mode/reserved bits and always returns E_SUCCESS), so guard here to honor the
        // documented H3InvalidIndexException, mirroring GetIcosahedronFaces.
        EnsureValidCell();

        // Fixed-capacity 6 (M4): `stackalloc` is CLR zero-initialized, so the pentagon's
        // single unwritten slot reads as H3_NULL(0) and the strip below is valid.
        Span<ulong> buffer = stackalloc ulong[6];
        fixed (ulong* ptr = buffer)
        {
            H3ErrorMarshaller.ThrowIfError(NativeMethods.OriginToDirectedEdges(Value, ptr));
        }

        int count = 0;
        for (int i = 0; i < 6; i++)
        {
            if (buffer[i] != 0)
            {
                count++;
            }
        }

        var result = new H3DirectedEdge[count];
        int next = 0;
        for (int i = 0; i < 6; i++)
        {
            if (buffer[i] != 0)
            {
                result[next++] = new H3DirectedEdge(buffer[i]);
            }
        }

        return result;
    }

    /// <summary>
    /// Fills <paramref name="destination"/> with the directed edges originating at this
    /// cell, packing the non-null edges to the front.
    /// </summary>
    /// <param name="destination">The destination span. Its length must be at least 6 (the per-cell maximum).</param>
    /// <returns>The number of directed edges written to the front of <paramref name="destination"/> (6 for a hexagon, 5 for a pentagon).</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="destination"/> is too small.</exception>
    /// <exception cref="H3InvalidIndexException">This is not a valid H3 cell.</exception>
    /// <exception cref="H3Exception">The native operation failed.</exception>
    public unsafe int GetDirectedEdgesInto(Span<H3DirectedEdge> destination)
    {
        if (destination.Length < MaxEdgeCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(destination),
                destination.Length,
                $"Destination span must hold at least {MaxEdgeCount} elements.");
        }

        // Native originToDirectedEdges does not validate its origin (it always returns
        // E_SUCCESS), so guard the receiver after the caller-arg check to honor the
        // documented H3InvalidIndexException, mirroring GetIcosahedronFaces.
        EnsureValidCell();

        // Native originToDirectedEdges does NOT guarantee zero-padding the pentagon hole,
        // and a caller-supplied span may carry stale data, so pre-clear before the fill + strip.
        destination[..MaxEdgeCount].Clear();

        // Write directly into the caller's span: H3DirectedEdge is blittable and layout-
        // compatible with ulong, so the native fill needs no intermediate buffer.
        fixed (H3DirectedEdge* ptr = destination)
        {
            H3ErrorMarshaller.ThrowIfError(NativeMethods.OriginToDirectedEdges(Value, (ulong*)ptr));
        }

        // Compact the H3_NULL (0) padding holes in place. The write index never
        // outruns the read index, so no live entry is clobbered.
        int count = 0;
        for (int i = 0; i < MaxEdgeCount; i++)
        {
            if (destination[i].Value != 0)
            {
                destination[count++] = destination[i];
            }
        }

        return count;
    }

    #endregion Directed edges

    #region Vertices

    /// <summary>
    /// Returns one of the topological vertices of this cell.
    /// </summary>
    /// <param name="vertexNumber">The vertex number (0-5 for hexagons, 0-4 for pentagons).</param>
    /// <returns>The requested <see cref="H3Vertex"/>.</returns>
    /// <exception cref="H3DomainException"><paramref name="vertexNumber"/> is out of range for this cell.</exception>
    /// <exception cref="H3InvalidIndexException">This is not a valid H3 cell.</exception>
    public H3Vertex GetVertex(int vertexNumber)
    {
        // Native cellToVertex does NOT validate its origin: an invalid cell may return
        // E_SUCCESS with a garbage vertex or E_FAILED (not E_CELL_INVALID), so guard the
        // receiver here to honor the documented H3InvalidIndexException, mirroring
        // GetVertexes. vertexNumber is left to native (E_DOMAIN) and is NOT pre-clamped.
        EnsureValidCell();
        H3ErrorMarshaller.ThrowIfError(NativeMethods.CellToVertex(Value, vertexNumber, out ulong v));
        return new H3Vertex(v);
    }

    /// <summary>
    /// Returns the topological vertices of this cell.
    /// </summary>
    /// <returns>The vertices (6 for a hexagon, 5 for a pentagon). Order is not guaranteed.</returns>
    /// <exception cref="H3InvalidIndexException">This is not a valid H3 cell.</exception>
    /// <exception cref="H3Exception">The native operation failed.</exception>
    public unsafe H3Vertex[] GetVertexes()
    {
        // Native cellToVertexes does not validate its origin, so guard here to honor the
        // documented H3InvalidIndexException, mirroring GetDirectedEdges.
        EnsureValidCell();

        // Fixed-capacity 6 (M4): `stackalloc` is CLR zero-initialized, so the pentagon's
        // single unwritten slot reads as H3_NULL(0) and the strip below is valid.
        Span<ulong> buffer = stackalloc ulong[MaxVertexCount];
        fixed (ulong* ptr = buffer)
        {
            H3ErrorMarshaller.ThrowIfError(NativeMethods.CellToVertexes(Value, ptr));
        }

        int count = 0;
        for (int i = 0; i < MaxVertexCount; i++)
        {
            if (buffer[i] != 0)
            {
                count++;
            }
        }

        var result = new H3Vertex[count];
        int next = 0;
        for (int i = 0; i < MaxVertexCount; i++)
        {
            if (buffer[i] != 0)
            {
                result[next++] = new H3Vertex(buffer[i]);
            }
        }

        return result;
    }

    /// <summary>
    /// Fills <paramref name="destination"/> with the topological vertices of this cell,
    /// packing the non-null vertices to the front.
    /// </summary>
    /// <param name="destination">The destination span. Its length must be at least 6 (the per-cell maximum).</param>
    /// <returns>The number of vertices written to the front of <paramref name="destination"/> (6 for a hexagon, 5 for a pentagon).</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="destination"/> is too small.</exception>
    /// <exception cref="H3InvalidIndexException">This is not a valid H3 cell.</exception>
    /// <exception cref="H3Exception">The native operation failed.</exception>
    public unsafe int GetVertexesInto(Span<H3Vertex> destination)
    {
        if (destination.Length < MaxVertexCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(destination),
                destination.Length,
                $"Destination span must hold at least {MaxVertexCount} elements.");
        }

        // Native cellToVertexes does not validate its origin, so guard the receiver after
        // the caller-arg check to honor the documented H3InvalidIndexException, mirroring
        // GetDirectedEdgesInto.
        EnsureValidCell();

        // Native cellToVertexes does NOT guarantee zero-padding the pentagon hole, and a
        // caller-supplied span may carry stale data, so pre-clear before the fill + strip.
        destination[..MaxVertexCount].Clear();

        // Write directly into the caller's span: H3Vertex is blittable and layout-
        // compatible with ulong, so the native fill needs no intermediate buffer.
        fixed (H3Vertex* ptr = destination)
        {
            H3ErrorMarshaller.ThrowIfError(NativeMethods.CellToVertexes(Value, (ulong*)ptr));
        }

        // Compact the H3_NULL (0) padding holes in place. The write index never
        // outruns the read index, so no live entry is clobbered.
        int count = 0;
        for (int i = 0; i < MaxVertexCount; i++)
        {
            if (destination[i].Value != 0)
            {
                destination[count++] = destination[i];
            }
        }

        return count;
    }

    #endregion Vertices

    #region Measures

    /// <summary>
    /// Returns the area of this cell in steradians (square radians).
    /// </summary>
    /// <returns>The cell area in steradians.</returns>
    /// <exception cref="H3InvalidIndexException">This is not a valid H3 cell.</exception>
    public double CellAreaRads2()
    {
        // Native cellAreaRads2 -> cellToBoundary -> _h3ToFaceIjk only rejects an
        // out-of-range base cell (>= NUM_BASE_CELLS); it returns E_SUCCESS with a
        // garbage area for most invalid cells (H3_NULL, wrong mode, deleted digits).
        // Validate-first to honor the documented H3InvalidIndexException.
        EnsureValidCell();
        H3ErrorMarshaller.ThrowIfError(NativeMethods.CellAreaRads2(Value, out double area));
        return area;
    }

    /// <summary>
    /// Returns the area of this cell in square kilometers.
    /// </summary>
    /// <returns>The cell area in square kilometers.</returns>
    /// <exception cref="H3InvalidIndexException">This is not a valid H3 cell.</exception>
    public double CellAreaKm2()
    {
        // Native does not fully validate the cell (see CellAreaRads2); validate-first.
        EnsureValidCell();
        H3ErrorMarshaller.ThrowIfError(NativeMethods.CellAreaKm2(Value, out double area));
        return area;
    }

    /// <summary>
    /// Returns the area of this cell in square meters.
    /// </summary>
    /// <returns>The cell area in square meters.</returns>
    /// <exception cref="H3InvalidIndexException">This is not a valid H3 cell.</exception>
    public double CellAreaM2()
    {
        // Native does not fully validate the cell (see CellAreaRads2); validate-first.
        EnsureValidCell();
        H3ErrorMarshaller.ThrowIfError(NativeMethods.CellAreaM2(Value, out double area));
        return area;
    }

    /// <summary>
    /// Returns the average area of a hexagonal cell at the given resolution, in square kilometers.
    /// </summary>
    /// <param name="resolution">The H3 resolution (0-15).</param>
    /// <returns>The average hexagon area at <paramref name="resolution"/>, in square kilometers.</returns>
    /// <exception cref="H3DomainException"><paramref name="resolution"/> is outside the range 0-15.</exception>
    public static double GetHexagonAreaAvgKm2(int resolution)
    {
        H3ErrorMarshaller.ThrowIfError(NativeMethods.GetHexagonAreaAvgKm2(resolution, out double area));
        return area;
    }

    /// <summary>
    /// Returns the average area of a hexagonal cell at the given resolution, in square meters.
    /// </summary>
    /// <param name="resolution">The H3 resolution (0-15).</param>
    /// <returns>The average hexagon area at <paramref name="resolution"/>, in square meters.</returns>
    /// <exception cref="H3DomainException"><paramref name="resolution"/> is outside the range 0-15.</exception>
    public static double GetHexagonAreaAvgM2(int resolution)
    {
        H3ErrorMarshaller.ThrowIfError(NativeMethods.GetHexagonAreaAvgM2(resolution, out double area));
        return area;
    }

    /// <summary>
    /// Returns the average edge length of a hexagonal cell at the given resolution, in kilometers.
    /// </summary>
    /// <param name="resolution">The H3 resolution (0-15).</param>
    /// <returns>The average hexagon edge length at <paramref name="resolution"/>, in kilometers.</returns>
    /// <exception cref="H3DomainException"><paramref name="resolution"/> is outside the range 0-15.</exception>
    public static double GetHexagonEdgeLengthAvgKm(int resolution)
    {
        H3ErrorMarshaller.ThrowIfError(NativeMethods.GetHexagonEdgeLengthAvgKm(resolution, out double length));
        return length;
    }

    /// <summary>
    /// Returns the average edge length of a hexagonal cell at the given resolution, in meters.
    /// </summary>
    /// <param name="resolution">The H3 resolution (0-15).</param>
    /// <returns>The average hexagon edge length at <paramref name="resolution"/>, in meters.</returns>
    /// <exception cref="H3DomainException"><paramref name="resolution"/> is outside the range 0-15.</exception>
    public static double GetHexagonEdgeLengthAvgM(int resolution)
    {
        H3ErrorMarshaller.ThrowIfError(NativeMethods.GetHexagonEdgeLengthAvgM(resolution, out double length));
        return length;
    }

    /// <summary>
    /// Returns the total number of unique H3 cells at the given resolution.
    /// </summary>
    /// <param name="resolution">The H3 resolution (0-15).</param>
    /// <returns>The number of cells at <paramref name="resolution"/> (122 at resolution 0).</returns>
    /// <exception cref="H3DomainException"><paramref name="resolution"/> is outside the range 0-15.</exception>
    public static long GetNumCells(int resolution)
    {
        H3ErrorMarshaller.ThrowIfError(NativeMethods.GetNumCells(resolution, out long count));
        return count;
    }

    /// <summary>
    /// Returns all 122 resolution-0 base cells.
    /// </summary>
    /// <returns>The 122 resolution-0 cells. Order is not guaranteed.</returns>
    /// <exception cref="H3Exception">The native operation failed.</exception>
    public static unsafe H3Index[] GetRes0Cells()
    {
        // Constant count (122); every entry is valid, so there is no size-first call
        // and no H3_NULL strip.
        int count = Res0CellCount();
        var result = new H3Index[count];
        fixed (H3Index* ptr = result)
        {
            H3ErrorMarshaller.ThrowIfError(NativeMethods.GetRes0Cells((ulong*)ptr));
        }

        return result;
    }

    /// <summary>
    /// Fills <paramref name="destination"/> with all 122 resolution-0 base cells.
    /// </summary>
    /// <param name="destination">The destination span. Its length must be at least 122.</param>
    /// <returns>The number of cells written (always 122).</returns>
    /// <exception cref="ArgumentException"><paramref name="destination"/> is smaller than 122.</exception>
    /// <exception cref="H3Exception">The native operation failed.</exception>
    public static unsafe int GetRes0CellsInto(Span<H3Index> destination)
    {
        int count = Res0CellCount();
        if (destination.Length < count)
        {
            throw new ArgumentException(
                $"Destination span must hold at least {count} elements.",
                nameof(destination));
        }

        // Constant count; every entry is valid, so write straight through with no strip.
        fixed (H3Index* ptr = destination)
        {
            H3ErrorMarshaller.ThrowIfError(NativeMethods.GetRes0Cells((ulong*)ptr));
        }

        return count;
    }

    /// <summary>
    /// Returns the 12 pentagon cells at the given resolution.
    /// </summary>
    /// <param name="resolution">The H3 resolution (0-15).</param>
    /// <returns>The 12 pentagons at <paramref name="resolution"/>. Order is not guaranteed.</returns>
    /// <exception cref="H3DomainException"><paramref name="resolution"/> is outside the range 0-15.</exception>
    /// <exception cref="H3Exception">The native operation failed.</exception>
    public static unsafe H3Index[] GetPentagons(int resolution)
    {
        // Constant count (12); every entry is valid, so there is no size-first call
        // and no H3_NULL strip. Native getPentagons validates the resolution.
        int count = PentagonCount();
        var result = new H3Index[count];
        fixed (H3Index* ptr = result)
        {
            H3ErrorMarshaller.ThrowIfError(NativeMethods.GetPentagons(resolution, (ulong*)ptr));
        }

        return result;
    }

    /// <summary>
    /// Fills <paramref name="destination"/> with the 12 pentagon cells at the given resolution.
    /// </summary>
    /// <param name="resolution">The H3 resolution (0-15).</param>
    /// <param name="destination">The destination span. Its length must be at least 12.</param>
    /// <returns>The number of pentagons written (always 12).</returns>
    /// <exception cref="ArgumentException"><paramref name="destination"/> is smaller than 12.</exception>
    /// <exception cref="H3DomainException"><paramref name="resolution"/> is outside the range 0-15.</exception>
    /// <exception cref="H3Exception">The native operation failed.</exception>
    public static unsafe int GetPentagonsInto(int resolution, Span<H3Index> destination)
    {
        // The length check MUST precede the native call: getPentagons does no size
        // check and overruns a short buffer.
        int count = PentagonCount();
        if (destination.Length < count)
        {
            throw new ArgumentException(
                $"Destination span must hold at least {count} elements.",
                nameof(destination));
        }

        fixed (H3Index* ptr = destination)
        {
            H3ErrorMarshaller.ThrowIfError(NativeMethods.GetPentagons(resolution, (ulong*)ptr));
        }

        return count;
    }

    #endregion Measures

    // Per-cell directed-edge capacity: 6 for a hexagon, 5 valid + 1 H3_NULL for a pentagon.
    private const int MaxEdgeCount = 6;

    // Per-cell vertex capacity (NUM_HEX_VERTS): 6 for a hexagon, 5 valid + 1 H3_NULL for a pentagon.
    private const int MaxVertexCount = 6;

    // Sentinel written by getIcosahedronFaces into unused slots; 0 is a valid face,
    // so the compaction passes strip -1, not H3_NULL.
    private const int InvalidFace = -1;

    internal int MaxFaceCount()
    {
        H3ErrorMarshaller.ThrowIfError(NativeMethods.MaxFaceCount(Value, out int count));
        return count;
    }

    // Constant 122: the number of resolution-0 base cells. The returned array length
    // conveys the count to callers, so this stays internal.
    internal static int Res0CellCount() => NativeMethods.Res0CellCount();

    // Constant 12: the number of pentagons at any resolution. The returned array length
    // conveys the count to callers, so this stays internal.
    internal static int PentagonCount() => NativeMethods.PentagonCount();

    internal long CellToChildrenSize(int childRes)
    {
        H3ErrorMarshaller.ThrowIfError(NativeMethods.CellToChildrenSize(Value, childRes, out long size));
        return size;
    }

    internal static unsafe long UncompactCellsSize(ReadOnlySpan<H3Index> cells, int res)
    {
        fixed (H3Index* p = cells)
        {
            H3ErrorMarshaller.ThrowIfError(NativeMethods.UncompactCellsSize((ulong*)p, cells.Length, res, out long size));
            return size;
        }
    }

    internal static long MaxGridRingSize(int k)
    {
        H3ErrorMarshaller.ThrowIfError(NativeMethods.MaxGridRingSize(k, out long size));
        return size;
    }

    internal static long GridPathCellsSize(H3Index start, H3Index end)
    {
        H3ErrorMarshaller.ThrowIfError(NativeMethods.GridPathCellsSize(start.Value, end.Value, out long size));
        return size;
    }

    private void EnsureValidCell()
    {
        if (!IsValidCell())
        {
            throw new H3InvalidIndexException(
                (uint)H3ErrorCode.CellInvalid,
                $"0x{Value:x16} is not a valid H3 cell.");
        }
    }
}

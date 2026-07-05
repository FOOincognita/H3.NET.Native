# Getting started

## Requirements

- A .NET SDK that can target `net10.0` or `net8.0`.
- A supported runtime: `linux-x64`, `linux-arm64`, `linux-musl-x64`, `osx-arm64`, or `win-x64`. The
  matching native `libh3` is bundled in the package and resolved automatically for these RIDs
  (Intel macOS, `osx-x64`, is not currently bundled).

## Add the package

```bash
dotnet add package H3.NET.Native
```

The package id is `H3.NET.Native`; the repository is `H3.NET.Native`.

## First program

All angular values are in **degrees**.

```csharp
using H3.NET.Native;

// 1. Index a coordinate at a chosen resolution (0 = coarsest, 15 = finest).
var sanFrancisco = new LatLng(LatitudeDegrees: 37.7752, LongitudeDegrees: -122.4188);
H3Index cell = H3Index.FromLatLng(sanFrancisco, resolution: 9);
Console.WriteLine(cell);             // 08928308280fffff

// 2. Recover the cell center as latitude/longitude (degrees).
LatLng center = cell.ToLatLng();
Console.WriteLine($"{center.LatitudeDegrees}, {center.LongitudeDegrees}");

// 3. Inspect the cell.
Console.WriteLine(cell.Resolution);  // 9
```

## Error handling

Operations that can fail surface H3 status codes as typed exceptions derived from
`H3Exception` (for example `H3InvalidIndexException`, `H3DomainException`,
`H3PentagonException`, `H3MemoryException`). Catch `H3Exception` to handle any of them.

## Unsafe grid-traversal variants

Upstream H3 v4.5.0 exports four public functions whose names end in `Unsafe`:
`gridDiskUnsafe`, `gridDiskDistancesUnsafe`, `gridDisksUnsafe`, and `gridRingUnsafe`.
Each assumes no pentagon or pentagon-distortion area is crossed; per the upstream
header and source comments, output is undefined (`gridDiskUnsafe`,
`gridDiskDistancesUnsafe`, `gridDisksUnsafe`) or the call can simply fail
(`gridRingUnsafe`) when one is encountered, and the caller is responsible for
detecting that and falling back. H3.NET.Native does not P/Invoke any of the four
and does not expose them on the public surface, so `H3Exception`-based error
handling stays reliable near pentagons with no extra bookkeeping on the caller's
part.

This costs nothing in practice: the safe functions the binding does bind,
`GridDisk`, `GridDiskDistances`, and `GridRing`, are themselves implemented
upstream as fast/slow pairs that try the matching `Unsafe` algorithm first and
transparently fall back to a slower, pentagon-correct algorithm on failure.
`GridRing`'s native entry point (`gridRing`) self-dispatches to `gridRingUnsafe`
this way; it is an internal optimization detail, not a caller-visible option,
since the binding only ever calls the safe entry point. `gridDisksUnsafe` (bulk
`gridDisk` over multiple origins) has no safe counterpart upstream at all, so
that capability is not exposed here; iterate `GridDisk` per origin instead.

## Next steps

Browse the <xref:H3.NET.Native> API reference for the full set of types and members.

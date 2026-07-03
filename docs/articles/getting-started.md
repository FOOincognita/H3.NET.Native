# Getting started

## Requirements

- A .NET SDK that can target `net10.0` or `net8.0`.
- A supported runtime: `linux-x64`, `linux-musl-x64`, or `osx-arm64`. The matching
  native `libh3` is bundled in the package and resolved automatically for these RIDs.

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

## Next steps

Browse the <xref:H3.NET.Native> API reference for the full set of types and members.

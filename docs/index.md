# H3.NET.Native

An idiomatic P/Invoke binding over [Uber H3](https://h3geo.org) v4.5.0 for .NET. The package
bundles the native `libh3` for the supported runtimes, so consumers do not build or ship the
native library themselves.

The NuGet package id is **`H3.NET.Native`** (the GitHub repository is `H3.NET.Native`).

## Install

```bash
dotnet add package H3.NET.Native
```

## At a glance

- **Target frameworks:** `net10.0`, `net8.0`.
- **Bundled native runtimes (RIDs):** `linux-x64`, `linux-arm64`, `linux-musl-x64`, `osx-arm64`, `win-x64` (Intel macOS, `osx-x64`, is not currently bundled).
- **Angular units:** all latitude/longitude values are in **degrees** (not radians).
- **API surface:** the public `H3.NET.Native` namespace. The internal `H3.NET.Native.Interop`
  marshalling layer is intentionally not documented.

## Quickstart

```csharp
using H3.NET.Native;

// Index a coordinate (near the Ferry Building, San Francisco) at resolution 9.
var sanFrancisco = new LatLng(LatitudeDegrees: 37.7752, LongitudeDegrees: -122.4188);
H3Index cell = H3Index.FromLatLng(sanFrancisco, resolution: 9);

Console.WriteLine(cell);            // 08928308280fffff
Console.WriteLine(cell.Resolution); // 9
```

See [Getting started](articles/getting-started.md) for more, and the
<xref:H3.NET.Native> API reference for the full type list.

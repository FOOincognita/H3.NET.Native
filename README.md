# H3.NET.Native

[![NuGet](https://img.shields.io/nuget/v/H3.NET.Native.svg)](https://www.nuget.org/packages/H3.NET.Native/)
[![License: Apache 2.0](https://img.shields.io/badge/License-Apache_2.0-blue.svg)](LICENSE)
[![CI](https://github.com/FOOincognita/H3.NET.Native/actions/workflows/ci.yml/badge.svg)](https://github.com/FOOincognita/H3.NET.Native/actions/workflows/ci.yml)
[![Uber H3](https://img.shields.io/badge/Uber%20H3-v4.5.0-blueviolet)](https://github.com/uber/h3/releases/tag/v4.5.0)

A thin, idiomatic P/Invoke binding over [Uber H3](https://h3geo.org) v4.5.0 for .NET 8 and .NET 10 (`net8.0` / `net10.0`). H3.NET.Native exposes the H3 hexagonal hierarchical geospatial indexing system through a clean, managed .NET API. The native `libh3` is built from a pinned upstream source revision and bundled directly in the NuGet package, so consumers need no C compiler, CMake, or other native toolchain to use it.

## Status

First preview release on the `0.x` line. The full Uber H3 v4.5.0 public surface (roughly seventy functions across inspection, hierarchy, grid traversal, directed edges, vertices, measures/units, and regions) is implemented and validated. As a `0.x` preview the API is largely stable but may still receive minor refinements before `1.0`.

## Installation

```sh
dotnet add package H3.NET.Native
```

The bundled native assets are delivered through NuGet's `runtimes/{rid}/native/` convention. This means H3.NET.Native must be consumed as a **`PackageReference`** (from nuget.org or another feed) so the native libraries propagate to your build and publish output. A bare **`ProjectReference`** to the source project does **not** carry the bundled native libraries, and your application will fail to load `libh3` at runtime.

## Supported platforms

| Aspect | Support |
| --- | --- |
| Target frameworks | `net10.0`, `net8.0` |
| Runtime identifiers | `linux-x64`, `linux-arm64`, `linux-musl-x64`, `osx-arm64`, `win-x64` |

Intel macOS (`osx-x64`) is not currently bundled (GitHub retired the hosted Intel runner used to load-test it). There is currently **no Native AOT requirement or support**, which is out of scope for this initial release and may be considered later.

## Quickstart

The following snippet illustrates the public API. All latitude and longitude values are in **degrees** (radians are internal only), matching `h3-py` and `h3-go`. On the `0.x` line the API may still see minor refinements before `1.0`.

```csharp
using H3.NET.Native;

var cell = H3Index.FromLatLng(new LatLng(37.7752, -122.4188), resolution: 9);
Console.WriteLine(cell);                 // H3 index

var center = cell.ToLatLng();            // back to LatLng (degrees)

foreach (var neighbor in cell.GridDisk(1))
    Console.WriteLine(neighbor);
```

## Versioning

The library follows [Semantic Versioning](https://semver.org). Versions are derived from annotated git tags prefixed with `v` (for example `v0.1.0`) via [MinVer](https://github.com/adamralph/minver). The library version is **independent of** the bundled Uber H3 version but tracks it: the binding currently bundles and targets Uber H3 **v4.5.0**.

## Correctness and benchmarks

**Correctness** is validated against the reference implementation, not against other managed ports: the differential test corpus is generated from the official **Uber H3 C library** (via `h3-py` ≥ 4, pinned to the bundled v4.5.0), with **h3-go** available as a tiebreaker and a pure-C `valgrind` harness guarding native memory usage. Because the binding calls that C code directly rather than reimplementing it, its outputs *are* the reference outputs; the corpus (tens of thousands of assertions per run, zero tolerated failures) confirms the marshalling layer preserves them across every supported platform. Each release also ships a [CycloneDX](https://cyclonedx.org) SBOM and a signed build-provenance attestation, and is published to nuget.org via OIDC trusted publishing (no long-lived API keys).

**Performance** is measured with [BenchmarkDotNet](https://benchmarkdotnet.org). The benchmark project compares three implementations across the same workloads, point indexing (`latLngToCell`), `gridDisk`, and `polygonToCells`:

- **Raw libh3 C**: direct P/Invoke into the bundled native `libh3` with no idiomatic layer; the floor that isolates this binding's own marshalling overhead. It is the per-category baseline for `latLngToCell` and `gridDisk`.
- **H3.NET.Native**: this binding.
- **[pocketken.H3](https://github.com/pocketken/H3.net)**: the fully managed (NetTopologySuite-based) port, the managed library many teams run today.

`polygonToCells` has no raw baseline (its C entry takes a `GeoPolygon*` whose marshalling would just duplicate the binding), so the binding is the baseline there and pocketken is measured against it.

Run the full suite with:

```sh
dotnet run --project tests/H3.NET.Native.Benchmarks -c Release -- --filter '*'
```

Representative results (Apple M3 Pro, .NET 10, BenchmarkDotNet 0.15.8; absolute timings vary by hardware, ratios are the stable signal):

| Method | Category | Mean | Ratio | Allocated |
| --- | --- | ---: | ---: | ---: |
| raw libh3 latLngToCell | LatLngToCell | 195 ns | 1.00 | – |
| H3.NET.Native FromLatLng | LatLngToCell | 196 ns | 1.01 | – |
| pocketken.H3 FromLatLng | LatLngToCell | 243 ns | 1.25 | 376 B |
| raw libh3 gridDisk | GridDisk | 1,025 ns | 1.00 | – |
| H3.NET.Native GridDisk | GridDisk | 1,115 ns | 1.09 | 1,504 B |
| pocketken.H3 GridDiskDistances | GridDisk | 1,709 ns | 1.67 | 6,576 B |
| H3.NET.Native ToCells | PolygonToCells\* | 98.8 µs | 1.00 | 608 B |
| pocketken.H3 Polyfill.Fill | PolygonToCells\* | 35.2 µs | 0.36 | 28,232 B |

Indexing and traversal sit essentially on the raw-C floor (`latLngToCell` 1.01x, `gridDisk` 1.09x) with far less managed allocation than pocketken.H3, and none at all on the indexing path.

\* The `polygonToCells` row is a **small** polygon (a res-9, ~55-cell triangle), the one regime where the native binding loses: stable libh3 sizes its working buffer from the polygon's *bounding box*, not its cell count, so a small fill pays a fixed setup cost. That cost amortizes as the fill grows: **the native binding overtakes pocketken.H3 past ~650 output cells and leads by ~1.4–1.6x at scale**, while allocating ~19–46x less throughout the sweep:

![polygonToCells time vs output cell count; H3.NET.Native overtakes pocketken.H3 past ~650 cells and leads 1.4-1.6x at scale](https://raw.githubusercontent.com/FOOincognita/H3.NET.Native/main/docs/articles/images/polygon-crossover.png)

Full methodology, the resolution sweep, per-operation allocation charts, and the correctness/provenance detail: **[Benchmarks](https://FOOincognita.github.io/H3.NET.Native/articles/benchmarks.html)**.

Benchmarks are informational, never gate CI (a tiny dry-run smoke runs there to keep them building and runnable), and their shapes may change while the binding is in preview.

## Links

- API documentation: https://FOOincognita.github.io/H3.NET.Native/
- [Benchmarks](https://FOOincognita.github.io/H3.NET.Native/articles/benchmarks.html)
- Uber H3: https://h3geo.org
- [Contributing](CONTRIBUTING.md)
- [Security policy](SECURITY.md)
- [Code of conduct](CODE_OF_CONDUCT.md)
- [Changelog](CHANGELOG.md)

## License

Licensed under the [Apache License, Version 2.0](LICENSE). Copyright 2026 FOOincognita.

Uber H3 is a separate project, also licensed under Apache-2.0, copyright Uber Technologies, Inc. See https://github.com/uber/h3.

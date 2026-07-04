# Benchmarks

These numbers exist to answer one question for teams evaluating H3 on .NET: **what
do you give up, and what do you gain, by binding the official Uber H3 C library
instead of running a managed port?**

Every measurement below is produced by the in-repo benchmark project with
[BenchmarkDotNet](https://benchmarkdotnet.org), comparing three implementations on
identical inputs:

- **Raw libh3 C**: direct P/Invoke into the bundled native `libh3`, no idiomatic
  layer. It is the *floor*: the fastest the C code can be called from .NET, and the
  baseline that isolates this binding's own marshalling overhead.
- **H3.NET.Native**: this binding.
- **[pocketken.H3](https://github.com/pocketken/H3.net)** 4.0.0: the fully managed
  (NetTopologySuite-based) port, the managed library many teams run today.

Absolute timings vary by hardware; **ratios are the stable signal**. The runs shown
here are Apple M3 Pro, .NET 10, BenchmarkDotNet 0.15.8, `DefaultJob`, against Uber H3
v4.5.0.

## Indexing and grid traversal run at the raw-C floor

| Operation | Implementation | Mean | vs raw libh3 | Allocated |
| --- | --- | ---: | ---: | ---: |
| `latLngToCell` | raw libh3 | 195 ns | 1.00x | – |
| `latLngToCell` | **H3.NET.Native** | 196 ns | **1.01x** | **0 B** |
| `latLngToCell` | pocketken.H3 | 243 ns | 1.25x | 376 B |
| `gridDisk` | raw libh3 | 1,025 ns | 1.00x | – |
| `gridDisk` | **H3.NET.Native** | 1,115 ns | **1.09x** | 1,504 B |
| `gridDisk` | pocketken.H3 | 1,709 ns | 1.67x | 6,576 B |

The binding adds 1–9% over calling the C directly (the cost of the safe, idiomatic,
exception-mapped surface) and stays well ahead of the managed port, which pays
25–67% more and allocates on every call.

![Indexing and traversal run within 1-9% of the raw libh3 C floor; pocketken.H3 pays 25-67% more](images/overhead-vs-raw.svg)

## Batched hierarchy operations: `cellToParent` and `cellToChildren`

The hierarchy calls are the smallest unit of work in the library, so a fair comparison
batches them. The harness takes 100 res-8 cells from a sorted San Francisco k=6 grid
disk; they reduce to 19 unique res-7 parents and expand back out to 133 res-8 children.

| Operation | Implementation | Mean | Ratio | Allocated |
| --- | --- | ---: | ---: | ---: |
| `cellToParent` (100 cells) | **H3.NET.Native** | 296.6 ns | 1.00 | **0 B** |
| `cellToParent` (100 cells) | pocketken.H3 | 296.9 ns | 1.00 | 2,400 B |
| `cellToChildren` (19 parents) | **H3.NET.Native** `CellToChildren` | 724 ns | 1.00 | 3,040 B |
| `cellToChildren` (19 parents) | **H3.NET.Native** `CellToChildrenInto` | 508 ns | **0.70** | **0 B** |
| `cellToChildren` (19 parents) | pocketken.H3 | 1,008 ns | 1.39 | 5,016 B |

On `cellToParent` the two libraries are a dead heat on wall clock (296.6 ns versus
296.9 ns for the 100 calls, about 2.97 ns per call), but the binding does it with zero
managed allocation while pocketken.H3 allocates 2,400 B (24 B per call). The binding
reaches roughly 3 ns per call even though every call goes through the `LibraryImport`
source-generated marshalling stub and does not yet apply `SuppressGCTransition`
(deferred by design), so there is still headroom below this number.

On `cellToChildren` the binding leads on both axes. Expanding the 19 parents to their
res-8 children takes 724 ns returning a fresh `H3Index[]` (`CellToChildren`, 3,040 B),
or 508 ns writing into a caller-supplied span (`CellToChildrenInto`, zero allocation),
against 1,008 ns and 5,016 B for pocketken.H3's `GetChildrenForResolution`. That is
1.4x faster allocating a new array, and 2.0x faster with zero allocation through the
span overload.

> Note: these hierarchy figures come from a BenchmarkDotNet ShortRun on the same Apple
> M3 Pro and .NET 10 as the rest of this page. As everywhere else here, read the ratios
> rather than the absolute nanoseconds as the signal.

## Filling polygons: `polygonToCells`

This is the one operation where a single headline number misleads. On a **small**
polygon the native binding loses, but the reason is structural, and it inverts as
the polygon grows.

Stable libh3 sizes its internal working buffer from the polygon's **bounding box**,
not from the number of cells produced, and zeroes that buffer before filling. On a
tiny polygon that fixed setup dominates, so a res-9, ~55-cell triangle looks like
this:

| Polygon | Implementation | Mean | Ratio | Allocated |
| --- | --- | ---: | ---: | ---: |
| res-9 triangle (~55 cells) | H3.NET.Native | 98.8 µs | 1.00 | 608 B |
| res-9 triangle (~55 cells) | pocketken.H3 | 35.2 µs | 0.36 | 28,232 B |

But that fixed cost amortizes as the fill grows. Sweeping one fixed ~0.5° box over
increasing resolution (so the output climbs from 1 cell to 156k) shows the real
picture: pocketken.H3 wins on tiny outputs, the two cross at roughly **650 cells**,
and the native binding leads by **~1.4–1.6x** from there on, while allocating
**~19–46x less at every point**.

| Resolution | Output cells | H3.NET.Native | pocketken.H3 | Faster | Native alloc | pocketken alloc |
| ---: | ---: | ---: | ---: | :--- | ---: | ---: |
| 4 | 1 | 9.3 µs | 1.9 µs | pocketken 4.9x | 192 B | 3.6 KB |
| 5 | 9 | 20.1 µs | 6.1 µs | pocketken 3.3x | 256 B | 7.6 KB |
| 6 | 65 | 46.0 µs | 29.4 µs | pocketken 1.6x | 704 B | 31 KB |
| 7 | 455 | 200.2 µs | 187.1 µs | pocketken 1.1x | 3.8 KB | 174 KB |
| 8 | 3,189 | 946.1 µs | 1,286.8 µs | **native 1.4x** | 25 KB | 1.1 MB |
| 9 | 22,334 | 6,150 µs | 9,842 µs | **native 1.6x** | 179 KB | 7.8 MB |
| 10 | 156,334 | 44,132 µs | 69,463 µs | **native 1.6x** | 1.2 MB | 57 MB |

![polygonToCells time versus output cell count; H3.NET.Native overtakes pocketken.H3 past about 650 cells and leads 1.4-1.6x at scale](images/polygon-crossover.svg)

Most real region-fill workloads (covering a neighborhood, tile, or service area at a
useful resolution) sit well above the crossover: the regime where the native binding
is both faster and dramatically lighter on the GC.

> Note: pocketken.H3 and libh3 do not always agree on the exact cells of a fill;
> the divergence is measured and quantified under
> [Correctness and provenance](#correctness-and-provenance) below. The sweep above
> compares wall-clock on the same input, not cell-set equality.

### The small-fill gap is the price of exactness

The crossover above has a structural cause, and it was measured directly rather than
assumed. Stable libh3 finds cells by tracing the entire polygon boundary with cells and
point-in-polygon testing every boundary cell plus its immediate neighbors, so its work
scales with the perimeter measured in cells, not with the output size. Instrumenting a
bit-identical reimplementation of the algorithm makes the imbalance concrete on the sweep
box above: at res 4 the fill emits 1 cell but performs 43 point-in-polygon tests, at res 5
it emits 9 cells for 92 tests, and the ratio only falls toward 1 as area outgrows
perimeter (1.4 tests per emitted cell by res 8).

Two prototypes were built to test whether that gap could be closed without giving up
exactness. A managed orchestration of the identical algorithm (calling the same native
primitives per cell, proven bit-identical to `polygonToCells` across every shape in the
divergence corpus plus 600 fuzzed polygons) sheds the native path's fixed setup and runs
19-32% faster than the binding on fills under a few tens of cells, but it stays 1.2-3.2x
behind pocketken.H3 below the crossover: the candidate work is inherent to the algorithm,
not to where it runs. The only approach that beat pocketken.H3 at every size was the one
pocketken itself uses, flood fill from a single interior seed, and even with an exact
point-in-polygon test it silently dropped cells on 6 of 30 fixed shapes and 3 of 600
fuzzed polygons (thin features, holes, and antimeridian-crossing shapes defeat single-seed
reachability).

As far as we can measure, beating pocketken.H3 on small fills and returning the reference
cell set are mutually exclusive. This binding keeps the reference cell set: neither
prototype ships in the package, and `ToCells` remains a thin call into libh3.

### `ToCellsExperimental` for large fills

H3 v4.5.0 also ships an experimental `polygonToCellsExperimental` that honors every
containment mode; in center mode it targets the same containment predicate as stable
`polygonToCells` by a faster interior-fill algorithm. The binding exposes it as
`H3Polygon.ToCellsExperimental(polygon, resolution, ContainmentMode.Center)`, gated
behind the `[Experimental]` attribute (diagnostic `H3NET0001`): upstream reserves the
right to change its behavior in any future minor H3 version, so it is an explicit
opt-in, never the default path.

On the same fixed ~0.5 degree sweep box used above, the two produce identical cell sets
at every resolution tested (4 through 8), a match the benchmark asserts in its
`GlobalSetup`. Where they differ is cost, and the sign of the difference flips with fill
size:

| Resolution | Output cells | Stable `ToCells` | `ToCellsExperimental` | Faster |
| ---: | ---: | ---: | ---: | :--- |
| 4 | 1 | 8.7 µs | 47.7 µs | stable 5.5x |
| 8 | 3,189 | 890.1 µs | 326.0 µs | **experimental 2.7x** |

The experimental fill sheds the perimeter-tracing overhead that dominates small stable
fills, so it pulls ahead only once the output is large; below roughly a thousand cells
its own fixed cost leaves it several times slower. Reach for `ToCellsExperimental`
deliberately on large fills (thousands of cells and up), where its ~2.7x edge is real,
and keep stable `ToCells` for everything else. Stable `ToCells` remains the default and
is not rerouted to the experimental path: the choice is the caller's, not automatic
dispatch.

> Note: both columns come from the same BenchmarkDotNet ShortRun, so the ratio is
> measured on one run; the stable figures here differ slightly from the `DefaultJob`
> numbers in the sweep table above for that reason, not because the fill changed.

## Allocation

Managed allocation is where the native path wins universally: raw libh3 and the
binding's indexing path allocate **nothing**, and the pooled polygon path drops to
608 B (down 25x from a prior naive buffer). pocketken.H3 allocates on every operation.

![Allocation per operation on a log scale; H3.NET.Native allocates up to 46x less than pocketken.H3, and zero on the indexing path](images/allocation.svg)

## Correctness and provenance

Performance only matters if the results are right. Because the binding calls the
official Uber H3 C code directly rather than reimplementing it, its outputs *are* the
reference outputs. A differential test corpus, generated from the official library
via `h3-py` ≥ 4 (pinned to the bundled v4.5.0), with `h3-go` as a tiebreaker,
confirms the marshalling layer preserves them across every supported platform:
**37,454 assertions at time of writing, zero tolerated failures**. Cell and index
results match exactly; geometric measures (areas, lengths) match within a tight
floating-point tolerance that accounts for per-platform `libm`. A pure-C `valgrind`
harness guards native memory usage.

### Polyfill divergence, measured

The fill-agreement caveat above is quantified, not assumed. A 27-case matrix, six
shapes (the benchmark triangle and sweep box, a concave L, a box with a hole, an
antimeridian-crossing quad, and a disjoint two-box multipolygon) swept across
resolutions 4–11, was filled by three implementations on identical inputs and
compared by SHA-256 of the sorted cell sets. The oracle is `h3-py` 4.5.0, whose
wheel bundles upstream libh3 4.5.0 exactly.

| Implementation | Identical to upstream | Mismatches |
| --- | --- | --- |
| **H3.NET.Native** | **27 / 27 cases** | none |
| pocketken.H3 4.0.0 | 20 / 27 cases | 7: every one a strict subset (cells missed, never added) |

The seven pocketken misses fall into two structural modes:

- **Disjoint multipolygons are silently truncated.** A single `Polyfill.Fill` over
  an NTS `MultiPolygon` of two disjoint boxes returns only one component (12 of 23
  cells at res 8; 564 of 1,128 at res 10): the fill flood-seeds from one interior
  point and expands only through grid-adjacent cells, so it can never reach the
  second component. Filling each polygon separately and unioning the results is
  correct and matched the oracle exactly.
- **Near-boundary cells are lost along slanted edges.** On the res-9 benchmark
  triangle pocketken returns 51 cells to the oracle's 55 (at res 8, 1 of 7); every
  missing cell lies within one cell circumradius of a polygon edge. Its
  strict-interior point-in-polygon test over straight lines in lng/lat space
  rejects near-edge cell centers that libh3's containment convention includes.
  Axis-aligned shapes (the boxes, the concave L, the hole, even the antimeridian
  quad) matched the oracle exactly at every resolution.

The per-case counts, set hashes, and exact differing cell IDs, plus both
generators (the .NET harness that fills through this binding and pocketken.H3,
and the `h3-py` oracle script), are committed under `tools/divergence-evidence/`.

For supply-chain review, each release ships a [CycloneDX](https://cyclonedx.org) SBOM
and a signed build-provenance attestation, and is published to nuget.org via **OIDC
trusted publishing** (no long-lived API keys).

## Reproducing these numbers

Run the full suite (all categories, `DefaultJob`):

```sh
dotnet run --project tests/H3.NET.Native.Benchmarks -c Release -- --filter '*'
```

BenchmarkDotNet writes CSV and Markdown reports under
`BenchmarkDotNet.Artifacts/results/`. The charts on this page are regenerated from
those results by `tools/gen-benchmark-charts/generate_charts.py` (see its header for
the exact steps). Benchmarks are informational and never gate CI (a tiny dry-run
smoke runs there only to keep them building and runnable), and their shapes may
change while the binding is in preview.

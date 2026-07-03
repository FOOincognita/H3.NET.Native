<!-- SPDX-License-Identifier: Apache-2.0 -->
# H3.NET.Native.MemoryTests

Two complementary leak gates for the native binding.

## Managed soak (`SoakTests.cs`)

xUnit v3 tests (all traited `[Trait("Category", "Soak")]`) that drive the binding in
tight loops and assert that managed heap and process working set (RSS) stay **bounded**
across a long run. They cover:

- the hot scalar paths (`FromLatLng` / `ToLatLng` / `GetBoundary` / `GridDisk`);
- the native **heap-owning** success path, `H3Polygon.FromCells`, which allocates the
  `LinkedGeoPolygon` via a `SafeHandle` and tears it down (`cellsToLinkedMultiPolygon`
  + `destroyLinkedMultiPolygon`);
- the **exception** path of that heap-owning code (invalid input), proving the
  `SafeHandle` releases when the native call throws, that it always raises a typed
  `H3Exception`, and that it never segfaults.

This is the managed gate: it proves bounded RSS including the native heap-ownership and
exception cleanup paths. It is intentionally robust to GC/allocator noise (see below),
so it catches *unbounded monotonic growth*, not byte-level leaks.

### Tuning

Iteration count comes from the `H3_SOAK_ITERS` env var (default `200000`, a few seconds
locally). CI can raise it for a longer soak:

```sh
H3_SOAK_ITERS=5000000 dotnet test --filter "Category=Soak"
```

## Native valgrind harness (`native-harness/`)

The pure-C program under `native-harness/` (built with its own `CMakeLists.txt`) is the
**authoritative** byte-level leak gate, run under valgrind in CI (Linux only). It is not
part of the .NET build and is excluded from this project's compilation. See
`native-harness/README.md`.

## ASan/LSan gate (`lsan.supp`) — libh3 as loaded by the dotnet host

The `asan-linux` CI job rebuilds libh3 with `-fsanitize=address`
(`build/build-native.sh linux-x64 --asan`, which uses a separate
`external/h3/build-asan` tree) and runs this soak with gcc's `libasan`
`LD_PRELOAD`ed into the dotnet host, so ASan instruments the whole process. This
complements the valgrind harness: valgrind checks the *pure-C usage pattern*,
while this checks libh3 **as actually loaded and driven through the P/Invoke
layer**.

- **Buffer overflow / use-after-free / double-free** in libh3 or the binding path
  are ASan errors: they fail immediately (`exitcode=1`) and are **not**
  suppressible.
- **Leaks** are filtered by `lsan.supp`. Because `libasan` is preloaded, LSan sees
  every allocation in the process, so the file suppresses the .NET runtime, ICU,
  and glibc loader/TLS process-lifetime allocations. `libh3` is deliberately never
  suppressed, and ASan's default `malloc_context_size` keeps a libh3 leak's stack
  topped by `libh3.so` frames, so real binding leaks are still reported. See the
  header of `lsan.supp` for the full matching rationale.
- Required env: `ASAN_OPTIONS` defers signal handling to the CLR
  (`handle_segv=0`, `allow_user_segv_handler=1`, `use_sigaltstack=0`) with
  `detect_leaks=1`; `LSAN_OPTIONS=suppressions=.../lsan.supp`. See the `asan-linux`
  job in `.github/workflows/ci.yml`.

The job is `continue-on-error` (non-gating) until it proves stable on real CI, then
graduates to a hard gate. The overflow half is authoritative immediately; only
leak-gating precision needs the soak-in period.

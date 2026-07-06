# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [1.0.0] - 2026-07-06

First stable release of H3.NET.Native. The public API is declared stable
under Semantic Versioning; it is unchanged from `0.2.0`.

### Added

- Native RIDs `linux-arm64` and `win-x64`, bringing the bundled runtime
  matrix to five RIDs: `linux-x64`, `linux-arm64`, `linux-musl-x64`,
  `osx-arm64`, and `win-x64`. Intel macOS (`osx-x64`) is not bundled.

## [0.2.0] - 2026-07-03

### Changed

- BREAKING: validity checks are now methods, `IsValidCell()` and
  `IsValidIndex()` (the previous `IsValid` properties/alias were removed).
- BREAKING: `GetOrigin()` and `GetDestination()` methods replace the
  `Origin` and `Destination` properties on directed edges.
- BREAKING: public method parameters are spelled out (`parentResolution`,
  `childResolution`, `childPosition`, `vertexNumber`, `resolution`).
- BREAKING: `H3InvalidCellException` renamed to `H3InvalidIndexException`.

### Added

- `LatLng.DegsToRads` and `LatLng.RadsToDegs` are now public.
- Line and branch coverage are a hard CI gate at 80 percent.

### Documentation

- Documented the omission of the four `*Unsafe` grid variants.

## [0.1.1] - 2026-07-03

### Added

- AddressSanitizer/LeakSanitizer (`asan-linux`) promoted to a hard CI gate
  for the native library.

## [0.1.0] - 2026-06-29

First public preview release of H3.NET.Native, a thin idiomatic P/Invoke binding
over Uber H3 v4.5.0 for `net8.0` and `net10.0`.

### Added

- Full Uber H3 v4.5.0 public surface (~70 functions) across inspection,
  hierarchy, grid traversal, directed edges, vertices, measures/units, and
  regions, with a degrees-based lat/lng public surface.
- Idiomatic P/Invoke binding bundling native `libh3` for `linux-x64`,
  `linux-musl-x64`, and `osx-arm64` via NuGet's `runtimes/<rid>/native/`
  convention, so consumers need no native toolchain.
- Target frameworks `net8.0` and `net10.0`.
- Public-API snapshot lock: a `PublicApiAnalyzers` baseline
  (`PublicAPI.Shipped.txt`) capturing the entire public surface, guarding
  against accidental SemVer-breaking changes in future releases.
- Continuous integration workflow (build, test, pack) and a CI status badge.
- API documentation site published at https://FOOincognita.github.io/H3.NET.Native/.
- Project governance: `README`, `CONTRIBUTING`, `CODE_OF_CONDUCT`, `SECURITY`,
  and this changelog.
- Native build pipeline: Uber H3 pinned as a git submodule at tag `v4.5.0`
  (`external/h3`) with a per-RID native build script producing
  `runtimes/<rid>/native/libh3.{so,dylib}`.
- Test fixture tooling: pinned Python venv with `h3>=4` for generating reference
  fixtures.

[Unreleased]: https://github.com/FOOincognita/H3.NET.Native/compare/v1.0.0...HEAD
[1.0.0]: https://github.com/FOOincognita/H3.NET.Native/compare/v0.2.0...v1.0.0
[0.2.0]: https://github.com/FOOincognita/H3.NET.Native/compare/v0.1.1...v0.2.0
[0.1.1]: https://github.com/FOOincognita/H3.NET.Native/compare/v0.1.0...v0.1.1
[0.1.0]: https://github.com/FOOincognita/H3.NET.Native/releases/tag/v0.1.0

# Contributing to H3.NET.Native

Thank you for your interest in contributing. H3.NET.Native is a thin, idiomatic P/Invoke binding over [Uber H3](https://h3geo.org) v4.5.0 for .NET 10+. This document covers local setup, the native build, test fixtures, code style, and the git workflow.

This repository is an early-stage scaffold. Some files and scripts referenced below describe the intended layout and may not all exist yet during scaffolding.

## Prerequisites

- **.NET SDK 10** (the build targets `net10.0` and `net8.0`).
- **CMake** (to configure and build the native `libh3`).
- **A C toolchain** (`clang` or `gcc`).
- **Python 3.12** (used to generate test fixtures from the reference `h3-py` implementation).

## Repository layout and the H3 submodule

The upstream H3 C source is consumed as a git submodule pinned to tag **`v4.5.0`** under `external/h3`. After cloning, initialize submodules:

```sh
git submodule update --init --recursive
```

Do not bump the submodule outside of an intentional, reviewed upgrade PR. The bundled native version and the binding's documented H3 version must stay in sync.

## Building the native library

The native `libh3` is built per runtime identifier:

```sh
build/build-native.sh <rid>
```

This produces `runtimes/<rid>/native/libh3.{so,dylib}` (`.so` on Linux, `.dylib` on macOS) or `h3.dll` on Windows, which is then packed into the NuGet package. Supported RIDs are `linux-x64`, `linux-arm64`, `linux-musl-x64`, `osx-arm64`, and `win-x64`.

## Test fixtures

Tests are validated against fixtures generated from the reference Python implementation. Use a **pinned virtual environment** with `h3>=4`:

```sh
python3.12 -m venv .venv
source .venv/bin/activate
pip install -r tools/gen-fixtures/requirements.txt
```

Note: a developer machine's system `h3-py` may be **v3**, which exposes a different (incompatible) API. Always use the pinned venv from `tools/gen-fixtures/requirements.txt` so fixtures are generated against the v4 API that matches the bundled native library.

## Code coverage

The suite runs on Microsoft.Testing.Platform (xUnit v3), so coverage uses the MTP-native extension (`Microsoft.Testing.Extensions.CodeCoverage`), not the VSTest coverlet collector (which MTP ignores). Collect a cobertura report locally with:

```sh
dotnet test tests/H3.NET.Native.Tests/H3.NET.Native.Tests.csproj -c Release -f net10.0 \
  -- --coverage --coverage-output-format cobertura --coverage-output h3.cobertura.xml
```

The report is written under the test project's `TestResults/`. CI collects it on the `linux-x64` leg and uploads it as the `coverage-linux-x64` artifact. Coverage is a hard gate: the job fails if either the overall line-rate or branch-rate drops below 80 percent, so new code must land with tests that keep both metrics above the bar.

## Code style

The following are enforced by the build and `.editorconfig`:

- **Nullable reference types** are enabled.
- **Warnings are treated as errors.**
- **XML documentation is required on every public member.** Missing docs (`CS1591`) are an error.
- Every `.cs` file begins with the SPDX header:

  ```csharp
  // SPDX-License-Identifier: Apache-2.0
  ```

- Formatting and analyzer rules are enforced via `.editorconfig`.

## Git workflow

- **Always work on a feature branch and open a pull request into `main`. Never push to `main` directly.**
- Keep commits focused and use clear, conventional commit messages.
- Ensure the solution builds with warnings-as-errors and that tests pass before requesting review.

## License of contributions

By contributing, you agree that your contributions are licensed under the Apache License, Version 2.0, consistent with the rest of the project.

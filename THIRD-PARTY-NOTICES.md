# Third-Party Notices

H3.NET.Native (NuGet PackageId: `H3.NET.Native`; repository: `H3.NET.Native`) incorporates
and acknowledges third-party material. This document distinguishes two distinct
categories that must not be conflated:

1. Bundled / distributed components, whose binaries we compile and ship inside
   the NuGet package.
2. Prior-art / approach references, which informed the design but whose source
   code is neither copied nor bundled.

---

## 1. Bundled / Distributed Components

These components are redistributed in binary form as part of the NuGet package.

### Uber H3

- Component: H3 -- Hexagonal hierarchical geospatial indexing system
- Source: https://github.com/uber/h3
- Documentation: https://h3geo.org
- Pinned version: v4.5.0 (consumed as a git submodule at `external/h3` and
  compiled from source)
- Distributed form: the native library `libh3`, bundled under
  `runtimes/{rid}/native/` for RIDs `linux-x64`, `linux-arm64`,
  `linux-musl-x64`, `osx-arm64`, and `win-x64`.
- License: Apache License, Version 2.0
  (https://github.com/uber/h3/blob/v4.5.0/LICENSE)
- Copyright: Copyright 2017-2021 Uber Technologies, Inc.

The upstream NOTICE for this component is reproduced in this repository's
[`NOTICE`](./NOTICE) file, as required by Apache-2.0 section 4(d). A full copy of
the Apache License, Version 2.0 is included in this repository's
[`LICENSE`](./LICENSE) file.

---

## 2. Prior-Art / Approach References

The following projects are acknowledged as prior C# work that informed the design
and API conventions of H3.NET.Native. They are listed for attribution and context
only. No source code, binaries, or other material from these projects is copied,
derived from, or bundled in H3.NET.Native.

### pocketken/H3.net

- Source: https://github.com/pocketken/H3.net
- License: Apache License, Version 2.0
- Nature of reference: a managed (pure C#) reimplementation of the H3 algorithms.
  Reviewed as prior art for .NET API ergonomics and naming. No code included.

### entrepreneur-interet-general/H3.Standard

- Source: https://github.com/entrepreneur-interet-general/H3.Standard
- License: Apache License, Version 2.0
- Nature of reference: an earlier .NET binding effort over H3. Reviewed as prior
  art for binding approach. No code included.

H3.NET.Native is an independent, thin P/Invoke binding over the upstream Uber H3 C
library (v4.5.0). Its public surface and runtime behavior derive from upstream
H3, not from the projects listed in this section.

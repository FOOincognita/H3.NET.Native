#!/usr/bin/env bash
# SPDX-License-Identifier: Apache-2.0
#
# build-native.sh — Compile libh3 from the pinned external/h3 submodule (v4.5.0)
# and stage the unversioned shared library into runtimes/<rid>/native/.
#
# Usage:
#   build-native.sh <rid> [--clean] [--asan]
#
#   <rid>     One of: linux-x64, linux-arm64, linux-musl-x64, osx-arm64, osx-x64
#   --clean   Remove the CMake build directory before configuring.
#   --asan    Instrument libh3 with AddressSanitizer/LeakSanitizer. Uses a
#             SEPARATE build directory (external/h3/build-asan) so the normal
#             Release cache/artifact is never disturbed. The resulting .so has
#             UNRESOLVED asan symbols (H3 does not link with -Wl,--no-undefined)
#             that a preloaded libasan supplies at runtime; consumers MUST
#             LD_PRELOAD the matching libasan (see the asan-linux CI job).
#
# This script natively builds the host RID (osx-arm64 on the dev box). The two
# linux RIDs are produced via build/docker/Dockerfile.manylinux and
# Dockerfile.alpine; do NOT attempt to cross-compile from this script.

set -euo pipefail

readonly EXPECTED_H3_TAG="v4.5.0"
readonly VALID_RIDS=("linux-x64" "linux-arm64" "linux-musl-x64" "osx-arm64" "osx-x64")

die() {
    printf 'error: %s\n' "$*" >&2
    exit 1
}

warn() {
    printf 'warning: %s\n' "$*" >&2
}

info() {
    printf '==> %s\n' "$*"
}

usage() {
    cat >&2 <<'EOF'
Usage: build-native.sh <rid> [--clean] [--asan]
  <rid>     One of: linux-x64, linux-arm64, linux-musl-x64, osx-arm64, osx-x64
  --clean   Remove the CMake build directory before configuring.
  --asan    Instrument libh3 with AddressSanitizer/LeakSanitizer (separate
            build dir; requires LD_PRELOAD of libasan at runtime).
EOF
    exit 2
}

# --- Parse arguments ---------------------------------------------------------

RID=""
CLEAN=0
ASAN=0

for arg in "$@"; do
    case "$arg" in
        --clean) CLEAN=1 ;;
        --asan) ASAN=1 ;;
        -h|--help) usage ;;
        -*) die "unknown option: $arg" ;;
        *)
            [ -z "$RID" ] || die "unexpected extra argument: $arg"
            RID="$arg"
            ;;
    esac
done

[ -n "$RID" ] || usage

is_valid_rid=0
for r in "${VALID_RIDS[@]}"; do
    [ "$r" = "$RID" ] && is_valid_rid=1 && break
done
[ "$is_valid_rid" -eq 1 ] || die "invalid rid '$RID'; must be one of: ${VALID_RIDS[*]}"

# --- Resolve repo root relative to this script -------------------------------

SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" >/dev/null 2>&1 && pwd -P)"
ROOT="$(cd -- "${SCRIPT_DIR}/.." >/dev/null 2>&1 && pwd -P)"

readonly H3_SRC="${ROOT}/external/h3"
# ASan uses a dedicated build tree so its instrumented CMake cache never clobbers
# the normal Release build (external/h3/build) shared by the shipping artifacts.
if [ "$ASAN" -eq 1 ]; then
    readonly H3_BUILD="${H3_SRC}/build-asan"
else
    readonly H3_BUILD="${H3_SRC}/build"
fi
readonly OUT_DIR="${ROOT}/runtimes/${RID}/native"

[ -d "$H3_SRC" ] || die "submodule not found at ${H3_SRC} (did you run 'git submodule update --init'?)"
[ -f "${H3_SRC}/CMakeLists.txt" ] || die "${H3_SRC} exists but has no CMakeLists.txt (submodule not initialized?)"

# --- Verify submodule tag ----------------------------------------------------

if command -v git >/dev/null 2>&1; then
    actual_tag="$(git -C "$H3_SRC" describe --tags 2>/dev/null || true)"
    if [ "$actual_tag" != "$EXPECTED_H3_TAG" ]; then
        warn "external/h3 is at '${actual_tag:-<unknown>}', expected ${EXPECTED_H3_TAG}"
    fi
else
    warn "git not found; skipping submodule tag verification"
fi

# --- Tooling check -----------------------------------------------------------

command -v cmake >/dev/null 2>&1 || die "cmake not found on PATH"
command -v nm    >/dev/null 2>&1 || die "nm not found on PATH (needed for export verification)"

# --- Determine shared-library extension by rid -------------------------------

case "$RID" in
    osx-*)   EXT="dylib" ;;
    linux-*) EXT="so" ;;
    *)       die "unhandled rid '$RID'" ;;
esac
readonly EXT

# --- Clean if requested ------------------------------------------------------

if [ "$CLEAN" -eq 1 ] && [ -d "$H3_BUILD" ]; then
    info "removing existing build directory: ${H3_BUILD}"
    rm -rf "$H3_BUILD"
fi

# --- Configure (out-of-source) -----------------------------------------------

# ASan flags are passed via the GLOBAL C/linker flag caches. H3 applies its own
# warning flags per-target (target_compile_options), so overriding CMAKE_C_FLAGS
# here augments the build without disturbing H3's flags. Frame pointers are kept
# for readable sanitizer stacks. The runtime is intentionally NOT linked into the
# .so (no -static-libasan): a preloaded libasan supplies it at load time.
CMAKE_EXTRA_ARGS=()
if [ "$ASAN" -eq 1 ]; then
    info "AddressSanitizer instrumentation ENABLED (build dir: ${H3_BUILD})"
    CMAKE_EXTRA_ARGS+=(
        "-DCMAKE_C_FLAGS=-fsanitize=address -fno-omit-frame-pointer"
        "-DCMAKE_SHARED_LINKER_FLAGS=-fsanitize=address"
        "-DCMAKE_EXE_LINKER_FLAGS=-fsanitize=address"
    )
fi

# Pin the macOS target architecture explicitly per RID. This lets osx-x64
# cross-compile x86_64 from an arm64 runner (best-practice thin per-RID asset,
# not a fat universal2 binary) and pins osx-arm64 to its native slice. Appended
# after the ASan block so sanitizer flags and the target arch coexist.
case "$RID" in
    osx-arm64) CMAKE_EXTRA_ARGS+=("-DCMAKE_OSX_ARCHITECTURES=arm64") ;;
    osx-x64)   CMAKE_EXTRA_ARGS+=("-DCMAKE_OSX_ARCHITECTURES=x86_64") ;;
esac

info "configuring CMake (Release, shared) for rid=${RID}"
cmake -S "$H3_SRC" -B "$H3_BUILD" \
    -DCMAKE_BUILD_TYPE=Release \
    -DBUILD_SHARED_LIBS=ON \
    -DCMAKE_POSITION_INDEPENDENT_CODE=ON \
    -DBUILD_TESTING=OFF \
    -DBUILD_BENCHMARKS=OFF \
    -DBUILD_FUZZERS=OFF \
    -DBUILD_FILTERS=OFF \
    -DBUILD_GENERATORS=OFF \
    -DENABLE_DOCS=OFF \
    -DENABLE_FORMAT=OFF \
    -DENABLE_LINTING=OFF \
    "${CMAKE_EXTRA_ARGS[@]+"${CMAKE_EXTRA_ARGS[@]}"}"

# --- Build only the 'h3' shared-library target -------------------------------

info "building target 'h3'"
cmake --build "$H3_BUILD" --target h3 --config Release -j

# --- Locate the REAL built shared library ------------------------------------
#
# With SOVERSION=1 set on the target, CMake emits a versioned real file plus an
# unversioned symlink:
#   macOS: libh3.1.dylib (real) + libh3.dylib (symlink)  [also seen: libh3.<ver>.dylib]
#   linux: libh3.so.1     (real) + libh3.so     (symlink)
# We resolve to the unversioned probe name (libh3.<ext>) if present, then
# dereference it with `cp -L` so the staged file is the actual library, never a
# symlink (symlinks are unreliable when packed into a .nupkg zip).

info "locating built shared library (*.${EXT}) under ${H3_BUILD}"

# Prefer the canonical unversioned name; fall back to any libh3*.<ext>* match.
SRC_LIB=""
if [ -e "${H3_BUILD}/lib/libh3.${EXT}" ]; then
    SRC_LIB="${H3_BUILD}/lib/libh3.${EXT}"
elif [ -e "${H3_BUILD}/libh3.${EXT}" ]; then
    SRC_LIB="${H3_BUILD}/libh3.${EXT}"
else
    # Search for any matching artifact (versioned real file or symlink).
    case "$EXT" in
        dylib) pattern='libh3*.dylib' ;;
        so)    pattern='libh3.so*' ;;
    esac
    # Prefer unversioned, then shortest path (typically the bare symlink target).
    found="$(find "$H3_BUILD" -type f -name "$pattern" 2>/dev/null | sort | head -n1 || true)"
    if [ -z "$found" ]; then
        # The unversioned name may be a symlink to a versioned real file.
        found="$(find "$H3_BUILD" \( -type f -o -type l \) -name "$pattern" 2>/dev/null | sort | head -n1 || true)"
    fi
    SRC_LIB="$found"
fi

[ -n "$SRC_LIB" ] && [ -e "$SRC_LIB" ] || die "could not locate a built libh3 shared library (*.${EXT}) under ${H3_BUILD}"

info "found: ${SRC_LIB}"

# --- Stage to runtimes/<rid>/native/libh3.<ext> (dereferenced) ---------------

mkdir -p "$OUT_DIR"
readonly DEST_LIB="${OUT_DIR}/libh3.${EXT}"

# cp -L dereferences symlinks so DEST_LIB is the real binary, not a link.
cp -L "$SRC_LIB" "$DEST_LIB"
info "staged: ${DEST_LIB}"

# --- Verify exports ----------------------------------------------------------
#
# Public H3 exports are bare names (latLngToCell, ...). On Mach-O, nm shows a
# leading underscore (_latLngToCell); on ELF the name is bare. Check both.

info "verifying export 'latLngToCell'"
symbol_present=0
case "$RID" in
    osx-*)
        if nm -gU "$DEST_LIB" 2>/dev/null | grep -qw '_latLngToCell' \
           || nm -gU "$DEST_LIB" 2>/dev/null | grep -qw 'latLngToCell'; then
            symbol_present=1
        fi
        ;;
    linux-*)
        if nm -D "$DEST_LIB" 2>/dev/null | grep -qw 'latLngToCell'; then
            symbol_present=1
        fi
        ;;
esac
[ "$symbol_present" -eq 1 ] || die "required export 'latLngToCell' not found in ${DEST_LIB}"

# --- glibc baseline check (linux-x64 only) -----------------------------------

GLIBC_MAX=""
# Skip under --asan: the sanitizer runtime's own glibc dependencies inflate the
# max version and would misrepresent the shipping artifact's baseline.
if [ "$RID" = "linux-x64" ] && [ "$ASAN" -eq 0 ]; then
    if command -v objdump >/dev/null 2>&1; then
        GLIBC_MAX="$(objdump -T "$DEST_LIB" 2>/dev/null \
            | grep -oE 'GLIBC_[0-9.]+' \
            | sort -uV \
            | tail -n1 || true)"
        info "max required glibc version: ${GLIBC_MAX:-<none detected>}"
    else
        warn "objdump not found; skipping glibc baseline check"
    fi
fi

# --- Success summary ---------------------------------------------------------

# Portable byte-size of the staged file.
if size_bytes="$(wc -c < "$DEST_LIB" 2>/dev/null)"; then
    size_bytes="$(printf '%s' "$size_bytes" | tr -d '[:space:]')"
else
    size_bytes="?"
fi

cat <<EOF

----------------------------------------------------------------------
  libh3 build OK
----------------------------------------------------------------------
  rid          : ${RID}
  source lib   : ${SRC_LIB}
  output       : ${DEST_LIB}
  size (bytes) : ${size_bytes}
  key symbol   : latLngToCell present
EOF
if [ "$ASAN" -eq 1 ]; then
    printf '  asan         : ENABLED (LD_PRELOAD libasan at runtime)\n'
fi
if [ "$RID" = "linux-x64" ] && [ -n "$GLIBC_MAX" ]; then
    printf '  glibc max    : %s\n' "$GLIBC_MAX"
fi
printf -- '----------------------------------------------------------------------\n'

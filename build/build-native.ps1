# SPDX-License-Identifier: Apache-2.0
#
# build-native.ps1 — PowerShell port of build-native.sh for Windows-based
# contributors (best-effort; production ships only linux/macOS RIDs).
#
# Compiles libh3 from the pinned external/h3 submodule (v4.5.0) and stages the
# unversioned shared library into runtimes/<rid>/native/.
#
# Usage:
#   ./build-native.ps1 -Rid <rid> [-Clean]
#
#   -Rid    One of: linux-x64, linux-arm64, linux-musl-x64, osx-arm64, osx-x64
#   -Clean  Remove the CMake build directory before configuring.
#
# NOTE: Windows (win-x64) is NOT a shipped RID for H3.NET.Native. This script is
# provided so Windows contributors can build the macOS/linux artifacts under an
# appropriate toolchain (e.g. WSL) or validate the pipeline locally.

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true, Position = 0)]
    [ValidateSet('linux-x64', 'linux-arm64', 'linux-musl-x64', 'osx-arm64', 'osx-x64')]
    [string]$Rid,

    [switch]$Clean
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$ExpectedH3Tag = 'v4.5.0'

function Write-Info { param([string]$Message) Write-Host "==> $Message" }
function Write-WarnMsg { param([string]$Message) Write-Warning $Message }

# --- Resolve repo root relative to this script -------------------------------

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$Root      = (Resolve-Path (Join-Path $ScriptDir '..')).Path

$H3Src   = Join-Path $Root 'external/h3'
$H3Build = Join-Path $H3Src 'build'
$OutDir  = Join-Path $Root "runtimes/$Rid/native"

if (-not (Test-Path $H3Src)) {
    throw "submodule not found at $H3Src (did you run 'git submodule update --init'?)"
}
if (-not (Test-Path (Join-Path $H3Src 'CMakeLists.txt'))) {
    throw "$H3Src exists but has no CMakeLists.txt (submodule not initialized?)"
}

# --- Verify submodule tag ----------------------------------------------------

if (Get-Command git -ErrorAction SilentlyContinue) {
    $actualTag = (& git -C $H3Src describe --tags 2>$null)
    if ($actualTag -ne $ExpectedH3Tag) {
        Write-WarnMsg "external/h3 is at '$actualTag', expected $ExpectedH3Tag"
    }
}
else {
    Write-WarnMsg 'git not found; skipping submodule tag verification'
}

# --- Tooling check -----------------------------------------------------------

if (-not (Get-Command cmake -ErrorAction SilentlyContinue)) {
    throw 'cmake not found on PATH'
}

# --- Determine shared-library extension by rid -------------------------------

switch -Wildcard ($Rid) {
    'osx-*'   { $Ext = 'dylib' }
    'linux-*' { $Ext = 'so' }
    default   { throw "unhandled rid '$Rid'" }
}

# --- Clean if requested ------------------------------------------------------

if ($Clean -and (Test-Path $H3Build)) {
    Write-Info "removing existing build directory: $H3Build"
    Remove-Item -Recurse -Force $H3Build
}

# --- Configure (out-of-source) -----------------------------------------------

# Pin the macOS target architecture explicitly per RID (parity with
# build-native.sh): osx-x64 cross-compiles x86_64 from an arm64 host, osx-arm64
# pins its native slice. Splatted so non-macOS RIDs pass no extra arg.
$ExtraArgs = @()
switch -Wildcard ($Rid) {
    'osx-arm64' { $ExtraArgs += '-DCMAKE_OSX_ARCHITECTURES=arm64' }
    'osx-x64'   { $ExtraArgs += '-DCMAKE_OSX_ARCHITECTURES=x86_64' }
}

Write-Info "configuring CMake (Release, shared) for rid=$Rid"
& cmake -S $H3Src -B $H3Build `
    -DCMAKE_BUILD_TYPE=Release `
    -DBUILD_SHARED_LIBS=ON `
    -DCMAKE_POSITION_INDEPENDENT_CODE=ON `
    -DBUILD_TESTING=OFF `
    -DBUILD_BENCHMARKS=OFF `
    -DBUILD_FUZZERS=OFF `
    -DBUILD_FILTERS=OFF `
    -DBUILD_GENERATORS=OFF `
    -DENABLE_DOCS=OFF `
    -DENABLE_FORMAT=OFF `
    -DENABLE_LINTING=OFF `
    @ExtraArgs
if ($LASTEXITCODE -ne 0) { throw "cmake configure failed (exit $LASTEXITCODE)" }

# --- Build only the 'h3' shared-library target -------------------------------

Write-Info "building target 'h3'"
& cmake --build $H3Build --target h3 --config Release -j
if ($LASTEXITCODE -ne 0) { throw "cmake build failed (exit $LASTEXITCODE)" }

# --- Locate the REAL built shared library ------------------------------------
#
# With SOVERSION=1, CMake emits a versioned real file plus an unversioned
# symlink (libh3.1.dylib/libh3.dylib; libh3.so.1/libh3.so). Prefer the
# unversioned probe name; fall back to any libh3*.<ext> match. We copy the
# resolved real target so the staged file is never a symlink.

Write-Info "locating built shared library (*.$Ext) under $H3Build"

$candidates = @(
    (Join-Path $H3Build "lib/libh3.$Ext"),
    (Join-Path $H3Build "libh3.$Ext")
)
$srcLib = $candidates | Where-Object { Test-Path $_ } | Select-Object -First 1

if (-not $srcLib) {
    $pattern = if ($Ext -eq 'dylib') { 'libh3*.dylib' } else { 'libh3.so*' }
    $srcLib = Get-ChildItem -Path $H3Build -Recurse -Filter $pattern -ErrorAction SilentlyContinue |
        Sort-Object FullName |
        Select-Object -First 1 -ExpandProperty FullName
}

if (-not $srcLib -or -not (Test-Path $srcLib)) {
    throw "could not locate a built libh3 shared library (*.$Ext) under $H3Build"
}
Write-Info "found: $srcLib"

# --- Stage to runtimes/<rid>/native/libh3.<ext> (dereferenced) ---------------
#
# Resolve any symlink to its real target before copying so the staged file is
# the actual binary (symlinks pack unreliably into a .nupkg zip).

if (-not (Test-Path $OutDir)) {
    New-Item -ItemType Directory -Path $OutDir -Force | Out-Null
}
$destLib = Join-Path $OutDir "libh3.$Ext"

$resolved = (Get-Item $srcLib)
if ($resolved.LinkType -eq 'SymbolicLink' -and $resolved.Target) {
    $linkTarget = $resolved.Target | Select-Object -First 1
    if (-not [System.IO.Path]::IsPathRooted($linkTarget)) {
        $linkTarget = Join-Path (Split-Path -Parent $srcLib) $linkTarget
    }
    $srcLib = (Resolve-Path $linkTarget).Path
}
Copy-Item -Path $srcLib -Destination $destLib -Force
Write-Info "staged: $destLib"

# --- Verify exports ----------------------------------------------------------
#
# Prefer nm if available (e.g. under WSL / MSYS / LLVM). On native Windows nm is
# typically absent; emit a warning rather than failing since win-* is unshipped.

$symbolChecked = $false
if (Get-Command nm -ErrorAction SilentlyContinue) {
    $nmOut = & nm -D $destLib 2>$null
    if (-not $nmOut) { $nmOut = & nm -gU $destLib 2>$null }
    if ($nmOut -match '(^|\s)_?latLngToCell(\s|$)') {
        $symbolChecked = $true
        Write-Info "verified export 'latLngToCell'"
    }
    else {
        throw "required export 'latLngToCell' not found in $destLib"
    }
}
if (-not $symbolChecked) {
    Write-WarnMsg "nm not available; skipped symbol verification for $destLib"
}

# --- Success summary ---------------------------------------------------------

$sizeBytes = (Get-Item $destLib).Length
Write-Host ''
Write-Host '----------------------------------------------------------------------'
Write-Host '  libh3 build OK'
Write-Host '----------------------------------------------------------------------'
Write-Host "  rid          : $Rid"
Write-Host "  source lib   : $srcLib"
Write-Host "  output       : $destLib"
Write-Host "  size (bytes) : $sizeBytes"
Write-Host "  key symbol   : latLngToCell$(if ($symbolChecked) { ' present' } else { ' (unverified)' })"
Write-Host '----------------------------------------------------------------------'

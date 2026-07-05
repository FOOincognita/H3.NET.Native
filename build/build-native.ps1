# SPDX-License-Identifier: Apache-2.0
#
# build-native.ps1 (PowerShell port of build-native.sh).
#
# Compiles libh3 from the pinned external/h3 submodule (v4.5.0) and stages the
# unversioned shared library into runtimes/<rid>/native/.
#
# Usage:
#   ./build-native.ps1 -Rid <rid> [-Clean]
#
#   -Rid    One of: linux-x64, linux-arm64, linux-musl-x64, osx-arm64, win-x64
#   -Clean  Remove the CMake build directory before configuring.
#
# NOTE: win-x64 IS a shipped RID. On Windows this script builds a native h3.dll
# via MSVC (multi-config Visual Studio generator, --config Release) and stages it
# to runtimes/win-x64/native/h3.dll (no lib prefix; .NET probing resolves the
# LibraryImport name "h3" to h3.dll). Windows contributors can still build the
# linux/macOS artifacts under an appropriate toolchain (e.g. WSL) instead.

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true, Position = 0)]
    [ValidateSet('linux-x64', 'linux-arm64', 'linux-musl-x64', 'osx-arm64', 'win-x64')]
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
    'win-*'   { $Ext = 'dll' }
    default   { throw "unhandled rid '$Rid'" }
}

# --- Clean if requested ------------------------------------------------------

if ($Clean -and (Test-Path $H3Build)) {
    Write-Info "removing existing build directory: $H3Build"
    Remove-Item -Recurse -Force $H3Build
}

# --- Configure (out-of-source) -----------------------------------------------

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
    -DENABLE_LINTING=OFF
if ($LASTEXITCODE -ne 0) { throw "cmake configure failed (exit $LASTEXITCODE)" }

# --- Build only the 'h3' shared-library target -------------------------------

Write-Info "building target 'h3'"
& cmake --build $H3Build --target h3 --config Release -j
if ($LASTEXITCODE -ne 0) { throw "cmake build failed (exit $LASTEXITCODE)" }

# --- Determine staged filename by rid ----------------------------------------
#
# .NET probing resolves the LibraryImport name "h3" to "h3.dll" on Windows (no
# lib prefix) and to "libh3.<ext>" on linux/macOS. The staged file MUST use the
# platform's probe name.

$destName = if ($Rid -like 'win-*') { 'h3.dll' } else { "libh3.$Ext" }

# --- Locate the REAL built shared library ------------------------------------
#
# On linux/macOS, with SOVERSION=1 CMake emits a versioned real file plus an
# unversioned symlink (libh3.1.dylib/libh3.dylib; libh3.so.1/libh3.so). Prefer
# the unversioned probe name; fall back to any libh3*.<ext> match. We copy the
# resolved real target so the staged file is never a symlink. On Windows the
# multi-config Visual Studio generator drops h3.dll under a config subdir (e.g.
# bin/Release/h3.dll), so find it recursively; Windows has no symlink concern.

Write-Info "locating built shared library ($destName) under $H3Build"

if ($Rid -like 'win-*') {
    $srcLib = Get-ChildItem -Path $H3Build -Recurse -Filter 'h3.dll' -ErrorAction SilentlyContinue |
        Sort-Object FullName |
        Select-Object -First 1 -ExpandProperty FullName
    if (-not $srcLib -or -not (Test-Path $srcLib)) {
        throw "could not locate a built h3.dll under $H3Build"
    }
}
else {
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
}
Write-Info "found: $srcLib"

# --- Stage to runtimes/<rid>/native/<destName> (dereferenced) ----------------
#
# On linux/macOS, resolve any symlink to its real target before copying so the
# staged file is the actual binary (symlinks pack unreliably into a .nupkg zip).
# On Windows the located file is already a real DLL.

if (-not (Test-Path $OutDir)) {
    New-Item -ItemType Directory -Path $OutDir -Force | Out-Null
}
$destLib = Join-Path $OutDir $destName

if ($Rid -notlike 'win-*') {
    $resolved = (Get-Item $srcLib)
    if ($resolved.LinkType -eq 'SymbolicLink' -and $resolved.Target) {
        $linkTarget = $resolved.Target | Select-Object -First 1
        if (-not [System.IO.Path]::IsPathRooted($linkTarget)) {
            $linkTarget = Join-Path (Split-Path -Parent $srcLib) $linkTarget
        }
        $srcLib = (Resolve-Path $linkTarget).Path
    }
}
Copy-Item -Path $srcLib -Destination $destLib -Force
Write-Info "staged: $destLib"

# --- Verify exports ----------------------------------------------------------
#
# On linux/macOS prefer nm (also available under WSL / MSYS / LLVM). On native
# Windows nm is absent, so the export check is BEST-EFFORT: try dumpbin /exports
# if the MSVC dev environment is on PATH, otherwise skip with an informational
# message. The real proof is the managed-test load leg, so a missing tool must
# NOT fail the build.

$symbolChecked = $false
if ($Rid -like 'win-*') {
    if (Get-Command dumpbin -ErrorAction SilentlyContinue) {
        $dumpOut = & dumpbin /exports $destLib 2>$null
        if ($dumpOut -match 'latLngToCell') {
            $symbolChecked = $true
            Write-Info "verified export 'latLngToCell' via dumpbin"
        }
        else {
            Write-WarnMsg "dumpbin did not report 'latLngToCell' in $destLib; relying on the managed-test load leg"
        }
    }
    else {
        Write-Info "dumpbin not on PATH; skipping export check (the managed-test load leg is the real proof)"
    }
}
elseif (Get-Command nm -ErrorAction SilentlyContinue) {
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
    Write-WarnMsg "symbol verification skipped for $destLib"
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

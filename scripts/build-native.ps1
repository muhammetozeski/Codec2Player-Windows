#!/usr/bin/env pwsh
<#
.SYNOPSIS
    (Re)build the native libcodec2.dll used by Codec2Player from the codec2 submodule.

.DESCRIPTION
    Builds a self-contained, 450-capable libcodec2.dll with MinGW-w64 GCC + Ninja and
    copies it to src/native/win-x64/libcodec2.dll. A prebuilt copy is committed, so you
    only need this if you want to rebuild the decoder yourself.

    MSVC cannot build Codec 2 (it uses C99 VLAs and native _Complex), so a MinGW-w64
    GCC is required. CMake and Ninja are looked up on PATH, then in Scoop / Visual
    Studio locations.
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent

function Get-ScoopRoots {
    $roots = [System.Collections.Generic.List[string]]::new()
    if ($env:SCOOP)        { $roots.Add($env:SCOOP) }
    $scoop = Get-Command scoop -ErrorAction SilentlyContinue
    if ($scoop) { $roots.Add((Split-Path (Split-Path $scoop.Source -Parent) -Parent)) }
    $roots.Add("$env:USERPROFILE\scoop")
    $roots.Add('C:\ProgramData\scoop')
    return $roots | Where-Object { $_ } | Select-Object -Unique
}

function Resolve-Tool {
    param([string]$Name, [string[]]$Globs = @())
    $onPath = Get-Command $Name -ErrorAction SilentlyContinue
    if ($onPath) { return $onPath.Source }
    foreach ($root in Get-ScoopRoots) {
        foreach ($sub in "apps\$Name\current\bin\$Name.exe", "apps\$Name\current\$Name.exe") {
            $p = Join-Path $root $sub
            if (Test-Path $p) { return (Resolve-Path $p).Path }
        }
    }
    foreach ($g in $Globs) {
        $hit = Get-ChildItem -Path $g -ErrorAction SilentlyContinue | Sort-Object FullName -Descending | Select-Object -First 1
        if ($hit) { return $hit.FullName }
    }
    return $null
}

Write-Host "==> Fetching Codec 2 submodule" -ForegroundColor Cyan
git -C $repo submodule update --init --recursive

$gcc = Resolve-Tool 'gcc' @("C:\msys64\mingw64\bin\gcc.exe")
if (-not $gcc) { throw "MinGW-w64 gcc not found. Install it ('scoop install gcc' or MSYS2)." }
$cmake = Resolve-Tool 'cmake' @("${env:ProgramFiles}\Microsoft Visual Studio\*\*\Common7\IDE\CommonExtensions\Microsoft\CMake\CMake\bin\cmake.exe")
if (-not $cmake) { throw "cmake not found ('scoop install cmake')." }
$ninja = Resolve-Tool 'ninja' @("${env:ProgramFiles}\Microsoft Visual Studio\*\*\Common7\IDE\CommonExtensions\Microsoft\CMake\Ninja\ninja.exe")
if (-not $ninja) { throw "ninja not found ('scoop install ninja')." }

$env:PATH = "$(Split-Path $gcc -Parent);$env:PATH"
$build = Join-Path $repo 'build-native'

Write-Host "==> Configuring libcodec2 (shared, self-contained)" -ForegroundColor Cyan
& $cmake -S "$repo/codec2" -B $build -G Ninja `
    -DCMAKE_MAKE_PROGRAM="$ninja" `
    -DCMAKE_C_COMPILER="$gcc" `
    -DCMAKE_BUILD_TYPE=Release `
    -DBUILD_SHARED_LIBS=ON `
    -DCMAKE_SHARED_LINKER_FLAGS="-static -static-libgcc"
if ($LASTEXITCODE -ne 0) { throw "CMake configure failed." }

Write-Host "==> Building codec2" -ForegroundColor Cyan
& $cmake --build $build --target codec2
if ($LASTEXITCODE -ne 0) { throw "Build failed." }

$dest = Join-Path $repo 'src/native/win-x64'
New-Item -ItemType Directory -Force -Path $dest | Out-Null
Copy-Item "$build/src/libcodec2.dll" "$dest/libcodec2.dll" -Force
Write-Host "==> Updated $dest\libcodec2.dll" -ForegroundColor Green

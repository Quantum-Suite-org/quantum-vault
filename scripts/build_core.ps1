# build_core.ps1 - Build qvault_core.dll (C++ backend)
# Run from Quantum-Vault root directory

param(
    [string]$Config = "Release",
    [switch]$Clean
)

$ErrorActionPreference = "Stop"
$Root    = $PSScriptRoot
$SrcDir  = Join-Path $Root "src\core"
$OutDir  = Join-Path $Root "build\core"
$BinDir  = Join-Path $Root "build\bin"

# Enter VS Dev Shell
Import-Module "C:\Program Files\Microsoft Visual Studio\18\Community\Common7\Tools\Microsoft.VisualStudio.DevShell.dll"
Enter-VsDevShell 9f9b4514 -SkipAutomaticLocation -DevCmdArguments "-arch=x64 -no_logo"

if ($Clean -and (Test-Path $OutDir)) { Remove-Item $OutDir -Recurse -Force }

New-Item -ItemType Directory -Force $OutDir | Out-Null
New-Item -ItemType Directory -Force $BinDir | Out-Null

$CppFlags = @(
    "/nologo",
    "/std:c++17",
    "/EHsc",
    "/W3",
    "/WX-",
    "/DQVAULT_CORE_EXPORTS",
    "/DUNICODE",
    "/D_UNICODE",
    "/D_WIN32_WINNT=0x0A00",  # Windows 10+
    "/LD",                     # Build as DLL
    "/MD",
    "/O2",
    "/Zi",
    "/Fd:`"$OutDir\qvault_core.pdb`"",
    "/Fo:`"$OutDir\\`""
)

$Libs = @(
    "bcrypt.lib",
    "crypt32.lib",
    "advapi32.lib",
    "shell32.lib",
    "kernel32.lib"
)

$Source = Join-Path $SrcDir "qvault_core.cpp"
$Out    = Join-Path $BinDir "qvault_core.dll"

Write-Host "Building qvault_core.dll ($Config)..." -ForegroundColor Cyan

$cmd = "cl $($CppFlags -join ' ') `"$Source`" /link $($Libs -join ' ') /DLL /OUT:`"$Out`" /IMPLIB:`"$OutDir\qvault_core.lib`" /DEBUG"

Write-Host $cmd -ForegroundColor DarkGray
Invoke-Expression $cmd

if ($LASTEXITCODE -ne 0) {
    Write-Host "BUILD FAILED" -ForegroundColor Red
    exit 1
}

# Copy header to build/bin so consumers can find it
Copy-Item (Join-Path $SrcDir "qvault_core.h") $BinDir -Force

Write-Host "qvault_core.dll -> $Out" -ForegroundColor Green
Write-Host "Core build complete." -ForegroundColor Green

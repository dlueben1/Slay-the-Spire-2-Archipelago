<#
.SYNOPSIS
  Update version across the Slay the Spire II Archipelago codebase.

.DESCRIPTION
  Takes a version string (e.g., "alpha-0.2.1" or "0.3.0"), extracts the semver
  part (major.minor.patch), and updates:
    - StS2AP.csproj:               ModVersion property
    - world/spire2/archipelago.json: world_version field
    - client/StS2AP/Archipelago.json: version field

.PARAMETER Version
  Version string to use. Can include a prefix (e.g., "alpha-0.2.1").
  The semver (X.Y.Z) will be extracted.

.EXAMPLE
  .\scripts\release.ps1 -Version "0.3.0"
  .\scripts\release.ps1 -Version "alpha-0.2.1"
#>

param(
    [Parameter(Mandatory = $true)]
    [string]$Version
)

$ErrorActionPreference = "Stop"

# Resolve repo root
$RepoRoot = Resolve-Path (Join-Path $PSScriptRoot "..") | Select-Object -ExpandProperty Path

# Extract semver (X.Y.Z) from the input version string
if ($Version -match '(\d+\.\d+\.\d+)') {
    $SemVer = $matches[1]
    Write-Host "Input version: $Version" -ForegroundColor Cyan
    Write-Host "Extracted semver: $SemVer" -ForegroundColor Green
} else {
    Write-Error "Version '$Version' does not contain a valid semver pattern (X.Y.Z)"
    exit 1
}

Write-Host "`nUpdating files..." -ForegroundColor Cyan

# ─ Update StS2AP.csproj ModVersion ─
$csprojPath = Join-Path $RepoRoot "client\StS2AP\StS2AP.csproj"
if (-not (Test-Path $csprojPath)) {
    Write-Error "File not found: $csprojPath"
    exit 1
}
$csprojContent = Get-Content $csprojPath -Raw
$csprojPattern = '<ModVersion Condition=".*?">[^<]*</ModVersion>'
$csprojReplacement = "<ModVersion Condition=`"'`$(ModVersion)' == ''`">$SemVer</ModVersion>"
$csprojNew = $csprojContent -replace $csprojPattern, $csprojReplacement
if ($csprojNew -ne $csprojContent) {
    Set-Content $csprojPath -Value $csprojNew -NoNewline
    Write-Host "  Updated: StS2AP.csproj (ModVersion)" -ForegroundColor Green
} elseif ($csprojContent -match $csprojPattern) {
    Write-Host "  Already up to date: StS2AP.csproj (ModVersion)" -ForegroundColor Yellow
} else {
    Write-Warning "  No match found in StS2AP.csproj"
}

# ─ Update world/spire2/archipelago.json world_version ─
$worldJsonPath = Join-Path $RepoRoot "world\spire2\archipelago.json"
if (-not (Test-Path $worldJsonPath)) {
    Write-Error "File not found: $worldJsonPath"
    exit 1
}
$worldJsonContent = Get-Content $worldJsonPath -Raw
$worldJsonPattern = '"world_version"\s*:\s*"[^"]+"'
$worldJsonReplacement = "`"world_version`": `"$SemVer`""
$worldJsonNew = $worldJsonContent -replace $worldJsonPattern, $worldJsonReplacement
if ($worldJsonNew -ne $worldJsonContent) {
    Set-Content $worldJsonPath -Value $worldJsonNew -NoNewline
    Write-Host "  Updated: world/spire2/archipelago.json (world_version)" -ForegroundColor Green
} elseif ($worldJsonContent -match $worldJsonPattern) {
    Write-Host "  Already up to date: world/spire2/archipelago.json (world_version)" -ForegroundColor Yellow
} else {
    Write-Warning "  No match found in world/spire2/archipelago.json"
}

# ─ Update client/StS2AP/Archipelago.json version ─
$clientJsonPath = Join-Path $RepoRoot "client\StS2AP\Archipelago.json"
if (-not (Test-Path $clientJsonPath)) {
    Write-Error "File not found: $clientJsonPath"
    exit 1
}
$clientJsonContent = Get-Content $clientJsonPath -Raw
$clientJsonPattern = '"version"\s*:\s*"[^"]+"'
$clientJsonReplacement = "`"version`": `"$SemVer`""
$clientJsonNew = $clientJsonContent -replace $clientJsonPattern, $clientJsonReplacement
if ($clientJsonNew -ne $clientJsonContent) {
    Set-Content $clientJsonPath -Value $clientJsonNew -NoNewline
    Write-Host "  Updated: client/StS2AP/Archipelago.json (version)" -ForegroundColor Green
} elseif ($clientJsonContent -match $clientJsonPattern) {
    Write-Host "  Already up to date: client/StS2AP/Archipelago.json (version)" -ForegroundColor Yellow
} else {
    Write-Warning "  No match found in client/StS2AP/Archipelago.json"
}

# ─ Build C# client ─
Write-Host "`nBuilding C# client (Release)..." -ForegroundColor Cyan
$csprojPath = Join-Path $RepoRoot "client\StS2AP\StS2AP.csproj"
$buildResult = dotnet build $csprojPath -c Release 2>&1
$buildExitCode = $LASTEXITCODE

if ($buildExitCode -ne 0) {
    Write-Host ($buildResult | Out-String) -ForegroundColor Red
    Write-Error "Build failed (exit code $buildExitCode). Check that local.props is configured."
    exit 1
}
Write-Host "  Build succeeded." -ForegroundColor Green

# ─ Locate the .pck file from the mods output directory ─
$msbuildJson = dotnet msbuild $csprojPath -getProperty:ModsOutputDir -getProperty:ModName 2>$null | ConvertFrom-Json
$modsOutputDir = $msbuildJson.Properties.ModsOutputDir
$modName = $msbuildJson.Properties.ModName
$pckPath = Join-Path $modsOutputDir "$modName.pck"

if (-not (Test-Path $pckPath)) {
    Write-Warning "  .pck file not found at $pckPath - it will not be included in the zip."
    Write-Warning "  Ensure Godot is installed and GodotExePath is set in local.props."
}

# ─ Zip the build output into sts2-client.zip ─
Write-Host "`nPackaging sts2-client.zip..." -ForegroundColor Cyan
$outputDir = Join-Path $RepoRoot "client\StS2AP\bin\Release\net9.0"
$distDir = Join-Path $RepoRoot "dist"
$zipPath = Join-Path $distDir "sts2-client.zip"

if (-not (Test-Path $outputDir)) {
    Write-Error "Build output directory not found: $outputDir"
    exit 1
}

New-Item -ItemType Directory -Force -Path $distDir | Out-Null

if (Test-Path $zipPath) {
    Remove-Item $zipPath -Force
}

# Collect DLLs and config from build output, excluding debug artifacts and game-provided DLLs
$filesToZip = Get-ChildItem -Path $outputDir -File | Where-Object {
    $_.Extension -notin @('.pdb', '.xml') -and
    $_.Name -notlike '*.deps.json' -and
    $_.Name -notin @('sts2.dll', '0Harmony.dll', 'GodotSharp.dll')
}

# Include the .pck from the mods output directory
if (Test-Path $pckPath) {
    $filesToZip = @($filesToZip) + @(Get-Item $pckPath)
}

if (-not $filesToZip) {
    Write-Error "No files found to zip in $outputDir"
    exit 1
}

# Create a temporary directory with Archipelago subfolder for zipping
$tempDir = Join-Path $env:TEMP "sts2release-$(Get-Random)"
$archDir = Join-Path $tempDir "Archipelago"
New-Item -ItemType Directory -Force -Path $archDir | Out-Null

try {
    # Copy files into Archipelago folder
    foreach ($file in $filesToZip) {
        Copy-Item -Path $file.FullName -Destination $archDir -Force
    }

    # Zip the Archipelago folder directly (so zip contains Archipelago > files)
    Compress-Archive -Path $archDir -DestinationPath $zipPath
    $fileCount = $filesToZip.Count
    Write-Host "  Created: $zipPath [$fileCount files]" -ForegroundColor Green
} finally {
    # Clean up temp directory
    Remove-Item -Recurse -Force $tempDir -ErrorAction SilentlyContinue
}

Write-Host "`nDone!" -ForegroundColor Green
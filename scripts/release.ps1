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
} else {
    Write-Warning "  No match found in client/StS2AP/Archipelago.json"
}

Write-Host "`nDone!" -ForegroundColor Green

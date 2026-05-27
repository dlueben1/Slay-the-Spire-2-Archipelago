<#
.SYNOPSIS
  Build the Slay the Spire II Archipelago world (.apworld file).

.DESCRIPTION
  Syncs the world source code into the Archipelago repository,
  builds the .apworld file using the Archipelago Launcher,
  and copies the result to the dist directory.

.EXAMPLE
  .\scripts\build_world.ps1
#>

$ErrorActionPreference = "Stop"

# Resolve repo root
$RepoRoot = Resolve-Path (Join-Path $PSScriptRoot "..") | Select-Object -ExpandProperty Path

# ~ Sync world source into Archipelago repo ~
Write-Host "`nSyncing world source into Archipelago repo..." -ForegroundColor Cyan
$archRepoRoot = Resolve-Path (Join-Path $RepoRoot "..") | Select-Object -ExpandProperty Path
$archWorldsDir = Join-Path $archRepoRoot "Archipelago\worlds"
$archSpire2Dir = Join-Path $archWorldsDir "spire2"

if (Test-Path $archSpire2Dir) {
    Remove-Item -Recurse -Force $archSpire2Dir
    Write-Host "  Deleted: $archSpire2Dir" -ForegroundColor Green
}

$localSpire2Dir = Join-Path $RepoRoot "world\spire2"
Copy-Item -Path $localSpire2Dir -Destination $archWorldsDir -Recurse -Force
Write-Host "  Copied: world\spire2 -> $archWorldsDir" -ForegroundColor Green

# ~ Build APWorld ~
Write-Host "`nBuilding APWorld..." -ForegroundColor Cyan
$launcherPath = Join-Path $archRepoRoot "Archipelago\Launcher.py"
if (-not (Test-Path $launcherPath)) {
    Write-Error "Launcher.py not found at $launcherPath"
    exit 1
}
# Temporarily lower error preference so Python stderr warnings don't abort the script
$prevPref = $ErrorActionPreference
$ErrorActionPreference = "Continue"
$archDir = Join-Path $archRepoRoot "Archipelago"
Push-Location $archDir
py -3.13 $launcherPath "Build APWorlds" "Slay the Spire II"
$apworldExitCode = $LASTEXITCODE
Pop-Location
$ErrorActionPreference = $prevPref
if ($apworldExitCode -ne 0) {
    Write-Error "APWorld build failed (exit code $apworldExitCode)."
    exit 1
}
Write-Host "  APWorld build succeeded." -ForegroundColor Green

# ~ Copy spire2.apworld to dist ~
Write-Host "`nCopying apworld to dist..." -ForegroundColor Cyan
$distDir = Join-Path $RepoRoot "dist"
New-Item -ItemType Directory -Force -Path $distDir | Out-Null

$apworldSource = Join-Path $archRepoRoot "Archipelago\build\apworlds\spire2.apworld"
if (Test-Path $apworldSource) {
    Copy-Item -Path $apworldSource -Destination $distDir -Force
    Write-Host "  Copied: spire2.apworld to $distDir" -ForegroundColor Green
} else {
    Write-Error "spire2.apworld not found at $apworldSource"
    exit 1
}

Write-Host "`nDone! APWorld built and placed in dist/" -ForegroundColor Green

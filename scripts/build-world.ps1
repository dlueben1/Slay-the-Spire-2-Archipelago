$ErrorActionPreference = "Stop"

# Resolve repo root
$repoRoot = Resolve-Path "$PSScriptRoot\.."

$worldSourceDir = Join-Path $repoRoot "world"
$distDir        = Join-Path $repoRoot "dist"
$stagingRoot    = Join-Path $distDir "_build"
$packageName    = "slaythespire2"
$packageDir     = Join-Path $stagingRoot $packageName
$tempZip        = Join-Path $distDir "$packageName.zip"
$outputFile     = Join-Path $distDir "$packageName.apworld"

# Validate source folder
if (!(Test-Path $worldSourceDir)) {
    throw "World source folder not found: $worldSourceDir"
}

# Ensure dist exists
if (!(Test-Path $distDir)) {
    New-Item -ItemType Directory -Path $distDir | Out-Null
}

# Clean old outputs
if (Test-Path $stagingRoot) { Remove-Item $stagingRoot -Recurse -Force }
if (Test-Path $tempZip)     { Remove-Item $tempZip -Force }
if (Test-Path $outputFile)  { Remove-Item $outputFile -Force }

# Create staging folder structure
New-Item -ItemType Directory -Path $packageDir -Force | Out-Null

Write-Host "Copying world files into staging..."
Copy-Item -Path (Join-Path $worldSourceDir "*") -Destination $packageDir -Recurse -Force

Write-Host "Creating archive..."
Compress-Archive -Path $packageDir -DestinationPath $tempZip -Force

Rename-Item -Path $tempZip -NewName "$packageName.apworld"

# Clean staging
Remove-Item $stagingRoot -Recurse -Force

Write-Host "Built: $outputFile"
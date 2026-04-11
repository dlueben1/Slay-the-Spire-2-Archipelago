<#
.SYNOPSIS
  Update version across the Slay the Spire II Archipelago codebase.

.DESCRIPTION
  Takes a version string (e.g., "alpha-0.2.1" or "0.3.0"), extracts the semver
  part (major.minor.patch), and updates:
    - StS2AP.csproj:               ModVersion property
    - world/spire2/archipelago.json: world_version field
    - client/StS2AP/Archipelago.json: version field
    - world/spire2/world.py:       mod_compat_version field

.PARAMETER Version
  Version string to use. Can include a prefix (e.g., "alpha-0.2.1").
  The semver (X.Y.Z) will be extracted.

.EXAMPLE
  .\scripts\release.ps1 -Version "0.3.0"
  .\scripts\release.ps1 -Version "alpha-0.2.1"
  .\scripts\release.ps1 -Version "alpha-0.2.1" -skipGitHub
#>

param(
    [Parameter(Mandatory = $true)]
    [string]$Version,

    [switch]$skipGitHub
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

# ~ Verify we are on the main branch ~
Write-Host "`nChecking current branch..." -ForegroundColor Cyan
$currentBranch = git -C $RepoRoot rev-parse --abbrev-ref HEAD 2>&1
if ($LASTEXITCODE -ne 0) {
    Write-Error "Failed to determine current branch. Is this a git repository?"
    exit 1
}
if ($currentBranch -ne 'main') {
    Write-Error "You must be on the 'main' branch to create a release. Current branch: '$currentBranch'"
    exit 1
}
Write-Host "  On branch: main" -ForegroundColor Green

Write-Host "`nUpdating files..." -ForegroundColor Cyan

# ~ Update StS2AP.csproj ModVersion ~
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

# ~ Update local.props ModVersion ~
$localPropsPath = Join-Path $RepoRoot "client\StS2AP\local.props"
if (-not (Test-Path $localPropsPath)) {
    Write-Warning "  local.props not found at $localPropsPath - skipping."
} else {
    $localPropsContent = Get-Content $localPropsPath -Raw
    $localPropsPattern = '<ModVersion>[^<]*</ModVersion>'
    $localPropsReplacement = "<ModVersion>$SemVer</ModVersion>"
    $localPropsNew = $localPropsContent -replace $localPropsPattern, $localPropsReplacement
    if ($localPropsNew -ne $localPropsContent) {
        Set-Content $localPropsPath -Value $localPropsNew -NoNewline
        Write-Host "  Updated: local.props (ModVersion)" -ForegroundColor Green
    } elseif ($localPropsContent -match $localPropsPattern) {
        Write-Host "  Already up to date: local.props (ModVersion)" -ForegroundColor Yellow
    } else {
        Write-Warning "  No match found in local.props"
    }
}

# ~ Update world/spire2/archipelago.json world_version ~
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

# ~ Update client/StS2AP/Archipelago.json version ~
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

# ~ Update world/spire2/world.py mod_compat_version ~
$worldPyPath = Join-Path $RepoRoot "world\spire2\world.py"
if (-not (Test-Path $worldPyPath)) {
    Write-Error "File not found: $worldPyPath"
    exit 1
}
$worldPyContent = Get-Content $worldPyPath -Raw
$worldPyPattern = '(mod_compat_version\s*=\s*")[^"]+"'
$worldPyReplacement = "`${1}$SemVer`""
$worldPyNew = $worldPyContent -replace $worldPyPattern, $worldPyReplacement
if ($worldPyNew -ne $worldPyContent) {
    Set-Content $worldPyPath -Value $worldPyNew -NoNewline
    Write-Host "  Updated: world/spire2/world.py (mod_compat_version)" -ForegroundColor Green
} elseif ($worldPyContent -match $worldPyPattern) {
    Write-Host "  Already up to date: world/spire2/world.py (mod_compat_version)" -ForegroundColor Yellow
} else {
    Write-Warning "  No match found in world/spire2/world.py"
}

# ~ Commit version bump ~
# Stage only the files we just modified and create a commit titled with the version.
# This commit will be used as the tagged commit for the release.
Write-Host "`nCommitting version bump..." -ForegroundColor Cyan
git -C $RepoRoot add `
    "client/StS2AP/StS2AP.csproj" `
    "client/StS2AP/Archipelago.json" `
    "world/spire2/archipelago.json" `
    "world/spire2/world.py"
$gitAddExit = $LASTEXITCODE
if ($gitAddExit -ne 0) {
    Write-Error "git add failed (exit code $gitAddExit)."
    exit 1
}
git -C $RepoRoot commit --message $Version
$gitCommitExit = $LASTEXITCODE
if ($gitCommitExit -ne 0) {
    Write-Error "git commit failed (exit code $gitCommitExit). Are there changes to commit?"
    exit 1
}
Write-Host "  Committed: $Version" -ForegroundColor Green

# ~ Build C# client ~
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

# ~ Locate the .pck file from the mods output directory ~
$msbuildJson = dotnet msbuild $csprojPath -getProperty:ModsOutputDir -getProperty:ModName 2>$null | ConvertFrom-Json
$modsOutputDir = $msbuildJson.Properties.ModsOutputDir
$modName = $msbuildJson.Properties.ModName
$pckPath = Join-Path $modsOutputDir "$modName.pck"

if (-not (Test-Path $pckPath)) {
    Write-Warning "  .pck file not found at $pckPath - it will not be included in the zip."
    Write-Warning "  Ensure Godot is installed and GodotExePath is set in local.props."
}

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
$archDir2 = Join-Path $archRepoRoot "Archipelago"
Push-Location $archDir2
py -3.13 $launcherPath "Build APWorlds" "Slay the Spire II"
$apworldExitCode = $LASTEXITCODE
Pop-Location
$ErrorActionPreference = $prevPref
if ($apworldExitCode -ne 0) {
    Write-Error "APWorld build failed (exit code $apworldExitCode)."
    exit 1
}
Write-Host "  APWorld build succeeded." -ForegroundColor Green

# ~ Prepare release artifacts ~
Write-Host "`nPreparing files for the new release..." -ForegroundColor Cyan
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
    Compress-Archive -Path $archDir -DestinationPath $zipPath -Force
    if (-not (Test-Path $zipPath)) {
        Write-Error "Failed to create $zipPath"
        exit 1
    }
    $fileCount = $filesToZip.Count
    Write-Host "  Created: sts2-client.zip [$fileCount files]" -ForegroundColor Green
} finally {
    # Clean up temp directory
    Remove-Item -Recurse -Force $tempDir -ErrorAction SilentlyContinue
}

# ~ Copy spire2.apworld to dist ~
$apworldSource = Join-Path $archRepoRoot "Archipelago\build\apworlds\spire2.apworld"
if (Test-Path $apworldSource) {
    Copy-Item -Path $apworldSource -Destination $distDir -Force
    Write-Host "  Copied: spire2.apworld to $distDir" -ForegroundColor Green
} else {
    Write-Warning "  spire2.apworld not found at $apworldSource"
}

# ~ Tag the version commit ~
# Tag HEAD (the version-bump commit we just created) with the release version.
git -C $RepoRoot tag $Version HEAD
$gitTagExit = $LASTEXITCODE
if ($gitTagExit -ne 0) {
    Write-Error "git tag failed (exit code $gitTagExit). Does the tag '$Version' already exist?"
    exit 1
}
Write-Host "  Tagged HEAD as: $Version" -ForegroundColor Green

if ($skipGitHub) {
    Write-Host "`nSkipping GitHub push and release (-skipGitHub specified)." -ForegroundColor Yellow
    Write-Host "  Commit and tag '$Version' created locally only." -ForegroundColor Yellow
} else {
    # ~ Push commit and tag, then create GitHub Release ~
    Write-Host "`nPushing commit and tag to GitHub..." -ForegroundColor Cyan

    git -C $RepoRoot push origin main
    $gitPushExit = $LASTEXITCODE
    if ($gitPushExit -ne 0) {
        Write-Error "git push failed (exit code $gitPushExit)."
        exit 1
    }
    Write-Host "  Pushed: main" -ForegroundColor Green

    git -C $RepoRoot push origin $Version
    $gitPushTagExit = $LASTEXITCODE
    if ($gitPushTagExit -ne 0) {
        Write-Error "git push tag failed (exit code $gitPushTagExit)."
        exit 1
    }
    Write-Host "  Pushed: tag $Version" -ForegroundColor Green

    # ~ Create GitHub Release ~
    Write-Host "`nCreating GitHub release..." -ForegroundColor Cyan

    if (-not (Get-Command gh -ErrorAction SilentlyContinue)) {
        Write-Error "GitHub CLI (gh) is required. Install from https://cli.github.com/"
        exit 1
    }

    # Generate release notes from template
    $templatePath = Join-Path $PSScriptRoot "release-notes-template.md"
    if (-not (Test-Path $templatePath)) {
        Write-Error "Release notes template not found at $templatePath"
        exit 1
    }
    $releaseNotes = (Get-Content $templatePath -Raw) -replace '\{\{VERSION\}\}', $Version

    $releaseNotesFile = Join-Path $env:TEMP "sts2-release-notes-$(Get-Random).md"
    Set-Content $releaseNotesFile -Value $releaseNotes -NoNewline

    try {
        # Collect all files in dist to upload
        $distFiles = Get-ChildItem -Path $distDir -File
        $assetArgs = @()
        foreach ($f in $distFiles) {
            $assetArgs += $f.FullName
        }

        # Create the release
        gh release create $Version @assetArgs --title $Version --notes-file $releaseNotesFile --latest

        Write-Host "  Release '$Version' created and marked as latest." -ForegroundColor Green
        Write-Host "  Don't forget to update the Changelist in the release notes on GitHub!" -ForegroundColor Yellow
    } finally {
        Remove-Item $releaseNotesFile -ErrorAction SilentlyContinue
    }
}

Write-Host "`nDone!" -ForegroundColor Green
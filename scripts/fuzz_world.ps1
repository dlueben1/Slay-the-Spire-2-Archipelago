<#
.SYNOPSIS
  Run Slay the Spire II APWorld tests and Archipelago Index fuzz suites.

.DESCRIPTION
  Safely syncs world/spire2 into a sibling Archipelago checkout, validates that
  the checkout is the expected version, and runs one of four suites:

    Core  - APWorld and relevant Archipelago framework tests.
    Smoke - A short single-world fuzz run (100 runs by default).
    UT    - The Universal Tracker fuzz hook (500 runs by default).
    Full  - Core tests plus the complete 14,500-run Index fuzz matrix.

  Fuzz logs and a copy of every report.json are stored under artifacts/fuzz/.
  The sync never deletes files. If stale non-cache files exist in the sibling
  APWorld directory, the script stops and reports them.

.PARAMETER Suite
  Test suite to run. Defaults to Smoke.

.PARAMETER ArchipelagoPath
  Path to the sibling Archipelago checkout. Defaults to ../Archipelago.

.PARAMETER PythonPath
  Path to the Python executable. Defaults to the checkout's
  .venv-0.6.7/Scripts/python.exe.

.PARAMETER Runs
  Override the number of runs. For Full, this overrides every fuzz job and is
  useful for a quick matrix check. Omit it to use the Index CI run counts.

.PARAMETER Jobs
  Worker count for fuzz jobs. Defaults to 8. The Full determinism job uses 4
  unless Jobs is explicitly supplied.

.PARAMETER Timeout
  Per-generation timeout in seconds. Defaults to 10. The Full determinism job
  uses 30 unless Timeout is explicitly supplied.

.PARAMETER ExpectedArchipelagoBranch
  Required sibling checkout branch. Defaults to ap-0.6.7-fuzz.

.PARAMETER ExpectedArchipelagoRef
  Required Git ref at the sibling checkout's HEAD. Defaults to 0.6.7.

.PARAMETER ResultPath
  Output directory for logs and reports. Defaults to a timestamped directory
  under artifacts/fuzz/.

.PARAMETER SkipSync
  Do not copy world/spire2 into the sibling checkout.

.PARAMETER SkipVersionCheck
  Do not require the expected Archipelago branch and ref.

.EXAMPLE
  .\scripts\fuzz_world.ps1 -Suite Core

.EXAMPLE
  .\scripts\fuzz_world.ps1 -Suite Smoke

.EXAMPLE
  .\scripts\fuzz_world.ps1 -Suite UT

.EXAMPLE
  .\scripts\fuzz_world.ps1 -Suite Full

.EXAMPLE
  .\scripts\fuzz_world.ps1 -Suite Full -Runs 10
#>

[CmdletBinding()]
param(
    [ValidateSet("Core", "Smoke", "UT", "Full")]
    [string]$Suite = "Smoke",

    [string]$ArchipelagoPath,

    [string]$PythonPath,

    [ValidateRange(1, 1000000)]
    [int]$Runs,

    [ValidateRange(1, 128)]
    [int]$Jobs = 8,

    [ValidateRange(1, 3600)]
    [int]$Timeout = 10,

    [string]$ExpectedArchipelagoBranch = "ap-0.6.7-fuzz",

    [string]$ExpectedArchipelagoRef = "0.6.7",

    [string]$ResultPath,

    [switch]$SkipSync,

    [switch]$SkipVersionCheck
)

$ErrorActionPreference = "Stop"
$RunsWasSpecified = $PSBoundParameters.ContainsKey("Runs")
$JobsWasSpecified = $PSBoundParameters.ContainsKey("Jobs")
$TimeoutWasSpecified = $PSBoundParameters.ContainsKey("Timeout")

$RepoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
if (-not $ArchipelagoPath) {
    $ArchipelagoPath = Join-Path (Split-Path $RepoRoot -Parent) "Archipelago"
}
$ArchipelagoPath = [System.IO.Path]::GetFullPath($ArchipelagoPath)

if (-not $PythonPath) {
    $PythonPath = Join-Path $ArchipelagoPath ".venv-0.6.7\Scripts\python.exe"
}
$PythonPath = [System.IO.Path]::GetFullPath($PythonPath)

if (-not $ResultPath) {
    $timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
    $ResultPath = Join-Path $RepoRoot "artifacts\fuzz\$timestamp-$($Suite.ToLowerInvariant())"
}
$ResultPath = [System.IO.Path]::GetFullPath($ResultPath)

$SourceWorldPath = Join-Path $RepoRoot "world\spire2"
$DestinationWorldPath = Join-Path $ArchipelagoPath "worlds\spire2"
$FuzzReportPath = Join-Path $ArchipelagoPath "fuzz_output\report.json"
$results = [System.Collections.Generic.List[object]]::new()

function Assert-PathExists {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,

        [Parameter(Mandatory = $true)]
        [string]$Description,

        [ValidateSet("Any", "Container", "Leaf")]
        [string]$PathType = "Any"
    )

    if (-not (Test-Path -LiteralPath $Path -PathType $PathType)) {
        throw "$Description not found: $Path"
    }
}

function Invoke-CheckedGit {
    param(
        [string[]]$GitArguments
    )

    $output = & git -c "safe.directory=$($ArchipelagoPath.Replace('\', '/'))" -C $ArchipelagoPath @GitArguments 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "Git command failed in $ArchipelagoPath`n$output"
    }
    return $output
}

function Get-RelativeFileSet {
    param([string]$Root)

    $rootWithSeparator = $Root.TrimEnd('\', '/') + [System.IO.Path]::DirectorySeparatorChar
    return @(
        Get-ChildItem -LiteralPath $Root -Recurse -File |
            Where-Object {
                $_.Extension -ne ".pyc" -and
                $_.FullName -notmatch "[\\/]__pycache__[\\/]"
            } |
            ForEach-Object { $_.FullName.Substring($rootWithSeparator.Length) }
    )
}

function Sync-SpireWorld {
    Assert-PathExists $SourceWorldPath "Source APWorld directory" "Container"
    Assert-PathExists (Join-Path $ArchipelagoPath "worlds") "Archipelago worlds directory" "Container"

    if (-not (Test-Path -LiteralPath $DestinationWorldPath -PathType Container)) {
        New-Item -ItemType Directory -Path $DestinationWorldPath | Out-Null
    }

    $sourceFiles = @(Get-RelativeFileSet $SourceWorldPath)
    $destinationFiles = @(Get-RelativeFileSet $DestinationWorldPath)
    $staleFiles = @($destinationFiles | Where-Object { $_ -notin $sourceFiles })
    if ($staleFiles.Count -gt 0) {
        $formattedFiles = $staleFiles | ForEach-Object { "  $_" }
        throw "The sibling APWorld contains stale files that are not in the source checkout.`n$($formattedFiles -join [Environment]::NewLine)`nRemove or preserve them manually, then rerun."
    }

    Write-Host "`nSyncing world\spire2..." -ForegroundColor Cyan
    Copy-Item -Path (Join-Path $SourceWorldPath "*") -Destination $DestinationWorldPath -Recurse -Force

    $mismatches = [System.Collections.Generic.List[string]]::new()
    foreach ($relativePath in $sourceFiles) {
        $sourceFile = Join-Path $SourceWorldPath $relativePath
        $destinationFile = Join-Path $DestinationWorldPath $relativePath
        if (-not (Test-Path -LiteralPath $destinationFile -PathType Leaf)) {
            $mismatches.Add($relativePath)
            continue
        }
        if ((Get-FileHash -Algorithm SHA256 -LiteralPath $sourceFile).Hash -ne
            (Get-FileHash -Algorithm SHA256 -LiteralPath $destinationFile).Hash) {
            $mismatches.Add($relativePath)
        }
    }
    if ($mismatches.Count -gt 0) {
        throw "APWorld sync verification failed: $($mismatches -join ', ')"
    }

    Write-Host "  Synced and verified $($sourceFiles.Count) files." -ForegroundColor Green
}

function Test-ArchipelagoVersion {
    if ($SkipVersionCheck) {
        Write-Warning "Skipping Archipelago branch/ref validation."
        return
    }

    $actualBranch = (Invoke-CheckedGit -GitArguments @("branch", "--show-current")).Trim()
    if ($actualBranch -ne $ExpectedArchipelagoBranch) {
        throw "Expected Archipelago branch '$ExpectedArchipelagoBranch', but found '$actualBranch'."
    }

    $actualHead = (Invoke-CheckedGit -GitArguments @("rev-parse", "HEAD")).Trim()
    $expectedHead = (Invoke-CheckedGit -GitArguments @("rev-list", "-n", "1", $ExpectedArchipelagoRef)).Trim()
    if ($actualHead -ne $expectedHead) {
        throw "Archipelago HEAD $actualHead does not match '$ExpectedArchipelagoRef' ($expectedHead)."
    }

    Write-Host "Archipelago: $actualBranch at $ExpectedArchipelagoRef ($($actualHead.Substring(0, 8)))" -ForegroundColor Green
}

function Invoke-CoreTests {
    $testModules = @(
        "worlds.spire2.test.id_tests",
        "worlds.spire2.test.group_tests",
        "worlds.spire2.test.option_tests",
        "worlds.spire2.test.logic_tests",
        "test.general.test_groups",
        "test.general.test_options"
    )
    $logPath = Join-Path $ResultPath "core-tests.log"

    Write-Host "`nRunning core APWorld tests..." -ForegroundColor Cyan
    $previousPreference = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    try {
        & $PythonPath -m unittest @testModules 2>&1 | Tee-Object -FilePath $logPath
        $exitCode = $LASTEXITCODE
    } finally {
        $ErrorActionPreference = $previousPreference
    }

    $status = if ($exitCode -eq 0) { "Passed" } else { "Failed" }
    $results.Add([pscustomobject]@{
        Name = "Core"
        Total = "-"
        Success = "-"
        Ignored = "-"
        Failures = if ($exitCode -eq 0) { 0 } else { 1 }
        Timeouts = 0
        Status = $status
    })
    if ($exitCode -ne 0) {
        throw "Core tests failed with exit code $exitCode. See $logPath"
    }
}

function Assert-FuzzDependency {
    param([string]$RelativePath, [string]$Description)
    Assert-PathExists (Join-Path $ArchipelagoPath $RelativePath) $Description "Leaf"
}

function Invoke-FuzzJob {
    param(
        [string]$Name,
        [int]$RunCount,
        [string]$Hook = "",
        [int]$JobTimeout = $Timeout,
        [int]$JobCount = $Jobs
    )

    $arguments = @(
        "fuzz.py",
        "-g", "spire2",
        "-r", $RunCount,
        "-n", "1",
        "-t", $JobTimeout,
        "-j", $JobCount
    )
    if ($Hook) {
        $arguments += @("--hook", $Hook)
    }

    $safeName = $Name.ToLowerInvariant() -replace "[^a-z0-9-]", "-"
    $logPath = Join-Path $ResultPath "$safeName.log"

    Write-Host "`nRunning $Name ($RunCount runs, $JobCount workers, ${JobTimeout}s timeout)..." -ForegroundColor Cyan
    if (Test-Path -LiteralPath $FuzzReportPath -PathType Leaf) {
        Remove-Item -LiteralPath $FuzzReportPath -Force
    }
    $previousPreference = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    try {
        & $PythonPath @arguments 2>&1 | Tee-Object -FilePath $logPath
        $exitCode = $LASTEXITCODE
    } finally {
        $ErrorActionPreference = $previousPreference
    }

    Assert-PathExists $FuzzReportPath "$Name fuzz report" "Leaf"
    $report = Get-Content -LiteralPath $FuzzReportPath -Raw | ConvertFrom-Json
    Copy-Item -LiteralPath $FuzzReportPath -Destination (Join-Path $ResultPath "$safeName-report.json")

    $passed = $exitCode -eq 0 -and $report.stats.failure -eq 0 -and $report.stats.timeout -eq 0
    $results.Add([pscustomobject]@{
        Name = $Name
        Total = $report.stats.total
        Success = $report.stats.success
        Ignored = $report.stats.ignored
        Failures = $report.stats.failure
        Timeouts = $report.stats.timeout
        Status = if ($passed) { "Passed" } else { "Failed" }
    })

    if (-not $passed) {
        throw "$Name failed. Exit code: $exitCode. See $logPath"
    }
}

function Get-RunCount {
    param([int]$Default)
    if ($RunsWasSpecified) {
        return $Runs
    }
    return $Default
}

Assert-PathExists $ArchipelagoPath "Archipelago checkout" "Container"
Assert-PathExists (Join-Path $ArchipelagoPath ".git") "Archipelago Git directory" "Container"
Assert-PathExists $PythonPath "Fuzzer Python executable" "Leaf"

Test-ArchipelagoVersion
if (-not $SkipSync) {
    Sync-SpireWorld
} else {
    Write-Warning "Skipping APWorld sync. Tests will use the existing sibling copy."
}

New-Item -ItemType Directory -Path $ResultPath -Force | Out-Null
Write-Host "Results: $ResultPath" -ForegroundColor DarkGray

$previousTestWorlds = $env:AP_TEST_WORLDS
$previousSkipUpdate = $env:SKIP_REQUIREMENTS_UPDATE
$env:AP_TEST_WORLDS = "spire2"
$env:SKIP_REQUIREMENTS_UPDATE = "1"

Push-Location $ArchipelagoPath
try {
    if ($Suite -eq "Core") {
        Invoke-CoreTests
    } elseif ($Suite -eq "Smoke") {
        Assert-FuzzDependency "fuzz.py" "Fuzzer entry point"
        Invoke-FuzzJob "Smoke" (Get-RunCount 100)
    } elseif ($Suite -eq "UT") {
        Assert-FuzzDependency "fuzz.py" "Fuzzer entry point"
        Assert-FuzzDependency "worlds\tracker.apworld" "Universal Tracker APWorld"
        if (-not (Test-Path -LiteralPath (Join-Path $ArchipelagoPath "Players") -PathType Container)) {
            New-Item -ItemType Directory -Path (Join-Path $ArchipelagoPath "Players") | Out-Null
        }
        Invoke-FuzzJob "UT" (Get-RunCount 500) "worlds.tracker.fuzzer_hook:Hook"
    } else {
        Assert-FuzzDependency "fuzz.py" "Fuzzer entry point"
        Assert-FuzzDependency "worlds\tracker.apworld" "Universal Tracker APWorld"
        Assert-FuzzDependency "worlds\empty.apworld" "Empty APWorld"
        if (-not (Test-Path -LiteralPath (Join-Path $ArchipelagoPath "Players") -PathType Container)) {
            New-Item -ItemType Directory -Path (Join-Path $ArchipelagoPath "Players") | Out-Null
        }

        Invoke-CoreTests
        Invoke-FuzzJob "Baseline" (Get-RunCount 5000)
        Invoke-FuzzJob "No Restrictive Starts" (Get-RunCount 5000) "hooks.with_empty:Hook"
        Invoke-FuzzJob "UT" (Get-RunCount 500) "worlds.tracker.fuzzer_hook:Hook"
        Invoke-FuzzJob "Gerpocalypse" (Get-RunCount 500) "hooks.gerpocalypse:Hook"
        Invoke-FuzzJob "Item Location Count" (Get-RunCount 500) "hooks.item_location_count:Hook"
        Invoke-FuzzJob "Lambda Capture" (Get-RunCount 500) "hooks.detect_rule_variable_capture_issues:Hook"
        Invoke-FuzzJob "Placement References" (Get-RunCount 500) "hooks.check_placement_item_location_references:Hook"

        $determinismJobs = if ($JobsWasSpecified) { $Jobs } else { 4 }
        $determinismTimeout = if ($TimeoutWasSpecified) { $Timeout } else { 30 }
        Invoke-FuzzJob "Determinism" (Get-RunCount 500) "hooks.determinism:Hook" $determinismTimeout $determinismJobs

        Invoke-FuzzJob "Indirect Conditions" (Get-RunCount 500) "hooks.indirect_conditions:Hook"
        Invoke-FuzzJob "Collect Accessibility" (Get-RunCount 500) "hooks.collect_accessibility_test:Hook"
        Invoke-FuzzJob "Static Output Placement" (Get-RunCount 500) "hooks.detect_output_placement_changes:Hook"
    }
} finally {
    Pop-Location

    if ($null -eq $previousTestWorlds) {
        Remove-Item Env:AP_TEST_WORLDS -ErrorAction SilentlyContinue
    } else {
        $env:AP_TEST_WORLDS = $previousTestWorlds
    }
    if ($null -eq $previousSkipUpdate) {
        Remove-Item Env:SKIP_REQUIREMENTS_UPDATE -ErrorAction SilentlyContinue
    } else {
        $env:SKIP_REQUIREMENTS_UPDATE = $previousSkipUpdate
    }

    if ($results.Count -gt 0) {
        Write-Host "`nResults:" -ForegroundColor Cyan
        $results | Format-Table Name, Total, Success, Ignored, Failures, Timeouts, Status -AutoSize
    }
    Write-Host "Artifacts: $ResultPath" -ForegroundColor DarkGray
}

<#
.SYNOPSIS
  Windows convenience wrapper for the Python release workflow.

.EXAMPLE
  .\scripts\release.ps1 build
  .\scripts\release.ps1 publish --expected-mod-version 1.0.1
#>

$ErrorActionPreference = "Stop"
$releaseScript = Join-Path $PSScriptRoot "release.py"

if (-not (Get-Command py -ErrorAction SilentlyContinue)) {
    Write-Error "Python Launcher for Windows (py) is required. Install Python 3.13 and try again."
    exit 1
}

& py -3.13 $releaseScript @args
exit $LASTEXITCODE

<#
.SYNOPSIS
  Build the APWorld using the shared headless Python builder.
.DESCRIPTION
  Uses the repository virtual environment when present. The sibling Archipelago
  checkout must be 0.6.8+ with worlds/spire2 linked to this checkout. On Windows,
  creating that link may require Developer Mode or an elevated terminal.
#>
$ErrorActionPreference = "Stop"
$RepoRoot = Split-Path $PSScriptRoot -Parent
$Script = Join-Path $PSScriptRoot "build_apworld_local.py"
$WindowsPython = Join-Path $RepoRoot ".venv/Scripts/python.exe"
$UnixPython = Join-Path $RepoRoot ".venv/bin/python"
if (Test-Path $WindowsPython) {
    & $WindowsPython $Script @args
} elseif (Test-Path $UnixPython) {
    & $UnixPython $Script @args
} elseif (Get-Command py -ErrorAction SilentlyContinue) {
    & py -3.13 $Script @args
} else {
    & python3 $Script @args
}
exit $LASTEXITCODE

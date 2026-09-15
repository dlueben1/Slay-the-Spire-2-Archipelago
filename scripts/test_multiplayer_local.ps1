<#
.SYNOPSIS
  Launch two AP-compatible Slay the Spire II beta multiplayer processes locally.

.DESCRIPTION
  Starts one native fastmp host and one native fastmp client with distinct
  client IDs. AP slot names may be the same or different. Steam is disabled so
  each client ID receives an independent StS2 account root, including RitsuLib
  settings and AP gold state.
  Native lobby rows are labeled with the corresponding AP slot and launch role.
  Each process also writes a separate log under logs\multiplayer.

  By default, the game executable is discovered from client/StS2AP/local.props.
  If that file is unavailable, the standard Steam installation path is tried.

.PARAMETER ExePath
  Optional path to SlayTheSpire2.exe.

.PARAMETER ApServer
  Archipelago server for both processes. Defaults to localhost:38281.

.PARAMETER HostSlot
  AP slot used by the StS2 host process. Defaults to Alice.

.PARAMETER ClientSlot
  AP slot used by the StS2 client process. Defaults to Bob.

.PARAMETER SettingsOnly
  Opens both isolated account identities without starting multiplayer. Use this
  once to enable Experimental Multiplayer for both client IDs.

.EXAMPLE
  .\scripts\test_multiplayer_local.ps1 -SettingsOnly

.EXAMPLE
  .\scripts\test_multiplayer_local.ps1

.EXAMPLE
  .\scripts\test_multiplayer_local.ps1 -ApServer localhost:38281 -HostSlot Alice -ClientSlot Bob

.EXAMPLE
  .\scripts\test_multiplayer_local.ps1 -ApServer localhost:38281 -HostSlot Alice -ClientSlot Alice
#>

#Requires -Version 5.1

[CmdletBinding()]
param(
    [string]$ExePath,

    [ValidateNotNullOrEmpty()]
    [string]$ApServer = "localhost:38281",

    [ValidateNotNullOrEmpty()]
    [string]$HostSlot = "Alice",

    [ValidateNotNullOrEmpty()]
    [string]$ClientSlot = "Bob",

    [ValidateRange(1, 2147483647)]
    [int]$HostClientId = 1,

    [ValidateRange(1, 2147483647)]
    [int]$ClientClientId = 1000,

    [ValidateRange(0, 30)]
    [int]$LaunchDelaySeconds = 2,

    [switch]$SettingsOnly
)

$ErrorActionPreference = "Stop"
$RepoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))

function Resolve-Sts2Executable {
    if ($ExePath) {
        $explicitPath = [System.IO.Path]::GetFullPath($ExePath)
        if (-not (Test-Path -LiteralPath $explicitPath -PathType Leaf)) {
            throw "SlayTheSpire2.exe was not found at the supplied path: $explicitPath"
        }
        return $explicitPath
    }

    $localPropsPath = Join-Path $RepoRoot "client\StS2AP\local.props"
    if (Test-Path -LiteralPath $localPropsPath -PathType Leaf) {
        try {
            [xml]$localProps = Get-Content -LiteralPath $localPropsPath -Raw
            $gamePathNode = $localProps.SelectSingleNode("//STS2GamePath")
            $configuredGamePath = if ($null -ne $gamePathNode) {
                $gamePathNode.InnerText.Trim()
            }
            else {
                $null
            }
            if ($configuredGamePath) {
                $configuredExe = Join-Path $configuredGamePath "SlayTheSpire2.exe"
                if (Test-Path -LiteralPath $configuredExe -PathType Leaf) {
                    return [System.IO.Path]::GetFullPath($configuredExe)
                }
            }
        }
        catch {
            Write-Warning "Could not read STS2GamePath from ${localPropsPath}: $($_.Exception.Message)"
        }
    }

    $standardCandidates = @()
    if (${env:ProgramFiles(x86)}) {
        $standardCandidates += Join-Path ${env:ProgramFiles(x86)} `
            "Steam\steamapps\common\Slay the Spire 2\SlayTheSpire2.exe"
    }
    if ($env:ProgramFiles) {
        $standardCandidates += Join-Path $env:ProgramFiles `
            "Steam\steamapps\common\Slay the Spire 2\SlayTheSpire2.exe"
    }

    foreach ($candidate in $standardCandidates) {
        if (Test-Path -LiteralPath $candidate -PathType Leaf) {
            return [System.IO.Path]::GetFullPath($candidate)
        }
    }

    throw "Could not find SlayTheSpire2.exe. Configure client\StS2AP\local.props or pass -ExePath."
}

function ConvertTo-NativeArgumentLine {
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$Arguments
    )

    $quotedArguments = foreach ($argument in $Arguments) {
        if ($argument.Contains('"')) {
            throw "Command-line values containing a double quote are not supported: $argument"
        }

        if ($argument -match "\s") {
            '"' + $argument + '"'
        }
        else {
            $argument
        }
    }
    return $quotedArguments -join " "
}

function Start-Sts2Process {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Label,

        [Parameter(Mandatory = $true)]
        [int]$ClientId,

        [ValidateSet("host_standard", "join")]
        [string]$Role,

        [string]$Slot
    )

    $arguments = @(
        "--log-file", (Join-Path $script:LogDirectory "$Role-$ClientId.log"),
        "-force-steam", "off",
        "-clientId", $ClientId.ToString()
    )

    if (-not $SettingsOnly) {
        $arguments += @(
            "-fastmp",
            "-apFastmp", $Role,
            "-apServer", $ApServer,
            "-apSlot", $Slot,
            "-apHostSlot", $HostSlot,
            "-apClientSlot", $ClientSlot,
            "-apHostClientId", $HostClientId.ToString(),
            "-apClientClientId", $ClientClientId.ToString()
        )
    }

    $argumentLine = ConvertTo-NativeArgumentLine -Arguments $arguments
    $slotDetail = if ($Slot) { ", AP slot=$Slot" } else { "" }
    Write-Host "Starting $Label (clientId=$ClientId$slotDetail)..." -ForegroundColor Cyan
    Write-Host "  Log: $(Join-Path $script:LogDirectory "$Role-$ClientId.log")"
    $process = Start-Process `
        -FilePath $script:ResolvedExePath `
        -WorkingDirectory (Split-Path $script:ResolvedExePath -Parent) `
        -ArgumentList $argumentLine `
        -PassThru
    return $process
}

if ($HostClientId -eq $ClientClientId) {
    throw "HostClientId and ClientClientId must be different."
}
$ResolvedExePath = Resolve-Sts2Executable
$LogDirectory = Join-Path $RepoRoot "logs\multiplayer"
New-Item -ItemType Directory -Path $LogDirectory -Force | Out-Null
$steamAppIdPath = Join-Path (Split-Path $ResolvedExePath -Parent) "steam_appid.txt"
$expectedSteamAppId = "2868840"
$currentSteamAppId = if (Test-Path -LiteralPath $steamAppIdPath -PathType Leaf) {
    (Get-Content -LiteralPath $steamAppIdPath -Raw).Trim()
}
else {
    ""
}
if ($currentSteamAppId -ne $expectedSteamAppId) {
    Set-Content `
        -LiteralPath $steamAppIdPath `
        -Value $expectedSteamAppId `
        -NoNewline `
        -Encoding ASCII
    Write-Host "Created $steamAppIdPath" -ForegroundColor Green
}

Write-Host "Using game executable: $ResolvedExePath" -ForegroundColor Green
$hostSlotArgument = if ($SettingsOnly) { $null } else { $HostSlot }
$hostProcess = Start-Sts2Process `
    -Label "AP multiplayer host" `
    -ClientId $HostClientId `
    -Role "host_standard" `
    -Slot $hostSlotArgument

if ($LaunchDelaySeconds -gt 0) {
    Start-Sleep -Seconds $LaunchDelaySeconds
}

$clientSlotArgument = if ($SettingsOnly) { $null } else { $ClientSlot }
$clientProcess = Start-Sts2Process `
    -Label "AP multiplayer client" `
    -ClientId $ClientClientId `
    -Role "join" `
    -Slot $clientSlotArgument

Write-Host "Started host PID $($hostProcess.Id) and client PID $($clientProcess.Id)." `
    -ForegroundColor Green
if ($SettingsOnly) {
    Write-Host `
        "Enable Experimental Multiplayer in Archipelago Settings in both windows, close them, then rerun without -SettingsOnly." `
        -ForegroundColor Yellow
}
else {
    Write-Host "Connect $HostSlot first. After its native lobby opens, connect $ClientSlot." `
        -ForegroundColor Yellow
}

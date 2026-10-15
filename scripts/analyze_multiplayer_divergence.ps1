<#
.SYNOPSIS
  Summarize Slay the Spire II multiplayer state-divergence dumps.

.DESCRIPTION
  Finds detailed "LOCAL STATE DUMP" / "REMOTE STATE DUMP" pairs in one or
  more game logs and reports only the fields that differ. The parser groups
  state by player and understands relics, RNG counters, and relic grab bags,
  making the first useful mismatch much easier to spot than in the raw dump.

  See scripts/README.md for the complete debugging workflow and interpretation
  guide.

  If neither -Path nor -Text is supplied, the script reads text from the
  clipboard. This is convenient when the divergence was copied from the
  in-game log viewer.

.PARAMETER Path
  One or more log/text paths. Wildcards are supported.

.PARAMETER Text
  Raw divergence text to analyze instead of reading files or the clipboard.

.PARAMETER MaxDifferences
  Maximum number of differing fields to print for each dump. Defaults to 50.

.EXAMPLE
  .\scripts\analyze_multiplayer_divergence.ps1 logs\multiplayer\host_standard-1.log

.EXAMPLE
  .\scripts\analyze_multiplayer_divergence.ps1 logs\multiplayer\*.log

.EXAMPLE
  Get-Clipboard -Raw | .\scripts\analyze_multiplayer_divergence.ps1

.EXAMPLE
  .\scripts\analyze_multiplayer_divergence.ps1
#>

#Requires -Version 5.1

[CmdletBinding(DefaultParameterSetName = "Path")]
param(
    [Parameter(Position = 0, ParameterSetName = "Path")]
    [string[]]$Path,

    [Parameter(Mandatory = $true, ValueFromPipeline = $true, ParameterSetName = "Text")]
    [AllowEmptyString()]
    [string]$Text,

    [ValidateRange(1, 1000)]
    [int]$MaxDifferences = 50
)

begin {
    $ErrorActionPreference = "Stop"
    $PipedText = [System.Text.StringBuilder]::new()
}

process {
    if ($PSCmdlet.ParameterSetName -eq "Text") {
        if ($PipedText.Length -gt 0) {
            [void]$PipedText.AppendLine()
        }
        [void]$PipedText.Append($Text)
    }
}

end {
    function Add-StateEntry {
        param(
            [Parameter(Mandatory = $true)]
            [System.Collections.IDictionary]$Entries,

            [Parameter(Mandatory = $true)]
            [string]$Key,

            [AllowEmptyString()]
            [string]$Value
        )

        $uniqueKey = $Key
        $occurrence = 2
        while ($Entries.Contains($uniqueKey)) {
            $uniqueKey = "$Key #$occurrence"
            $occurrence++
        }
        $Entries[$uniqueKey] = $Value.Trim()
    }

    function Add-SummaryFields {
        param(
            [Parameter(Mandatory = $true)]
            [System.Collections.IDictionary]$Entries,

            [Parameter(Mandatory = $true)]
            [string]$Scope,

            [Parameter(Mandatory = $true)]
            [string]$Line
        )

        $fieldPattern = [regex]'(?<name>[A-Za-z][A-Za-z ]*?):\s*(?<value>.*?)(?=\s+[A-Za-z][A-Za-z ]*?:\s|$)'
        foreach ($field in $fieldPattern.Matches($Line)) {
            Add-StateEntry `
                -Entries $Entries `
                -Key "$Scope/$($field.Groups['name'].Value.Trim())" `
                -Value $field.Groups['value'].Value
        }
    }

    function ConvertTo-StateEntries {
        param(
            [AllowEmptyString()]
            [string]$Dump
        )

        $entries = [ordered]@{}
        $scope = "Run"

        foreach ($rawLine in ($Dump -split "`r?`n")) {
            $line = $rawLine.Trim()
            if (-not $line) {
                continue
            }

            if ($line -match '^Player with ID:\s*(?<id>\d+)\s+Character:\s*(?<character>.+)$') {
                $scope = "Player $($Matches['id'])"
                Add-StateEntry -Entries $entries -Key "$scope/Character" -Value $Matches['character']
                continue
            }

            if ($line -match '^RNG global seed:\s*(?<seed>.*)$') {
                $scope = "Global"
                Add-StateEntry -Entries $entries -Key "$scope/RNG seed" -Value $Matches['seed']
                continue
            }

            if ($line -match '^Relic\s+(?<id>\S+)\s+Props:\s*(?<props>.*?)\s+Floor added:\s*(?<floor>.*)$') {
                $value = "Props=$($Matches['props'].Trim()); Floor added=$($Matches['floor'].Trim())"
                Add-StateEntry -Entries $entries -Key "$scope/Relic/$($Matches['id'])" -Value $value
                continue
            }

            if ($line -match '^RNG counter\s+(?<name>[^:]+):\s*(?<value>.*)$') {
                Add-StateEntry `
                    -Entries $entries `
                    -Key "$scope/RNG/$($Matches['name'].Trim())" `
                    -Value $Matches['value']
                continue
            }

            if ($line -match '^Player RNG seed:\s*(?<seed>.*)$') {
                Add-StateEntry -Entries $entries -Key "$scope/RNG seed" -Value $Matches['seed']
                continue
            }

            if ($line -match '^Rarity\s+(?<rarity>[^:]+):\s*(?<relics>.*)$') {
                Add-StateEntry `
                    -Entries $entries `
                    -Key "$scope/Relic grab bag/$($Matches['rarity'].Trim())" `
                    -Value $Matches['relics']
                continue
            }

            if ($line -eq 'Player relic grab bag:') {
                continue
            }

            if ($line -match '^(Turn|Energy|Pile Count):') {
                Add-SummaryFields -Entries $entries -Scope $scope -Line $line
                continue
            }

            if ($line -match '^(?<name>[^:]+):\s*(?<value>.*)$') {
                Add-StateEntry `
                    -Entries $entries `
                    -Key "$scope/$($Matches['name'].Trim())" `
                    -Value $Matches['value']
                continue
            }

            Add-StateEntry -Entries $entries -Key "$scope/Unclassified" -Value $line
        }

        return $entries
    }

    function Get-CollectionDetail {
        param(
            [AllowEmptyString()]
            [string]$LocalValue,

            [AllowEmptyString()]
            [string]$RemoteValue
        )

        $localItems = @($LocalValue -split ',' | ForEach-Object { $_.Trim() } | Where-Object { $_ })
        $remoteItems = @($RemoteValue -split ',' | ForEach-Object { $_.Trim() } | Where-Object { $_ })
        $localOnly = @(Compare-Object $localItems $remoteItems -PassThru | Where-Object SideIndicator -eq '<=')
        $remoteOnly = @(Compare-Object $localItems $remoteItems -PassThru | Where-Object SideIndicator -eq '=>')

        if ($localOnly.Count -eq 0 -and $remoteOnly.Count -eq 0) {
            $limit = [Math]::Min($localItems.Count, $remoteItems.Count)
            for ($index = 0; $index -lt $limit; $index++) {
                if ($localItems[$index] -cne $remoteItems[$index]) {
                    return "Same entries in a different order; first mismatch at index $index " +
                        "('$($localItems[$index])' vs '$($remoteItems[$index])')."
                }
            }
            return "Same entries in a different order."
        }

        $parts = @()
        if ($localOnly.Count -gt 0) {
            $parts += "Local only: $($localOnly -join ', ')"
        }
        if ($remoteOnly.Count -gt 0) {
            $parts += "Remote only: $($remoteOnly -join ', ')"
        }
        return $parts -join '; '
    }

    function Get-FocusHint {
        param(
            [Parameter(Mandatory = $true)]
            [string[]]$DifferenceKeys
        )

        if ($DifferenceKeys -match '/Choice IDs($| #)') {
            return "Choice sequencing differs. Inspect option construction, removal, or selection before this checkpoint."
        }
        if ($DifferenceKeys -match '/Reward IDs($| #)') {
            return "Reward sequencing differs. Inspect reward insertion, claim, and removal order."
        }
        if ($DifferenceKeys -match '/RNG/') {
            return "An RNG stream differs. The named counter usually identifies which operation advanced on only one replica."
        }
        if ($DifferenceKeys -match '/Relic grab bag/') {
            return "A relic bag differs. Check relic materialization and bag removal on every replica."
        }
        if ($DifferenceKeys -match '/Relic/') {
            return "Player relic state differs. Check reward ownership, grant count, and relic property changes."
        }
        return $null
    }

    function Write-DivergenceReport {
        param(
            [Parameter(Mandatory = $true)]
            [System.Text.RegularExpressions.Match]$Match,

            [Parameter(Mandatory = $true)]
            [string]$Source,

            [Parameter(Mandatory = $true)]
            [int]$Index
        )

        $header = $Match.Groups['header'].Value
        $contextMatch = [regex]::Match(
            $header,
            'Context:\s*(?<context>.*?)\s+Local:\s*(?<local>\d+)\.\s+Remote:\s*(?<remote>\d+)\.',
            [System.Text.RegularExpressions.RegexOptions]::Singleline
        )
        $context = if ($contextMatch.Success) {
            ($contextMatch.Groups['context'].Value -replace '\s+', ' ').Trim()
        }
        else {
            "<not found>"
        }
        $localChecksum = if ($contextMatch.Success) { $contextMatch.Groups['local'].Value } else { "?" }
        $remoteChecksum = if ($contextMatch.Success) { $contextMatch.Groups['remote'].Value } else { "?" }

        $localEntries = ConvertTo-StateEntries -Dump $Match.Groups['localDump'].Value
        $remoteEntries = ConvertTo-StateEntries -Dump $Match.Groups['remoteDump'].Value
        $allKeys = @($localEntries.Keys + $remoteEntries.Keys | Sort-Object -Unique)
        $differences = @()
        $matchingCount = 0

        foreach ($key in $allKeys) {
            $hasLocal = $localEntries.Contains($key)
            $hasRemote = $remoteEntries.Contains($key)
            $localValue = if ($hasLocal) { [string]$localEntries[$key] } else { "<missing>" }
            $remoteValue = if ($hasRemote) { [string]$remoteEntries[$key] } else { "<missing>" }

            if ($hasLocal -and $hasRemote -and $localValue -ceq $remoteValue) {
                $matchingCount++
                continue
            }

            $detail = $null
            if ($hasLocal -and $hasRemote -and $key -match '/Relic grab bag/') {
                $detail = Get-CollectionDetail -LocalValue $localValue -RemoteValue $remoteValue
            }
            $differences += [pscustomobject]@{
                Key = $key
                Local = $localValue
                Remote = $remoteValue
                Detail = $detail
            }
        }

        Write-Output ""
        Write-Output "=== $Source :: divergence $Index ==="
        Write-Output "Checksum ID: $($Match.Groups['id'].Value) | Reported client: $($Match.Groups['player'].Value)"
        Write-Output "Context: $context"
        Write-Output "Checksums: local=$localChecksum remote=$remoteChecksum"
        Write-Output "Differences: $($differences.Count) | Matching parsed fields: $matchingCount"

        if ($differences.Count -eq 0) {
            Write-Output "No differing parsed fields were found. Inspect untracked game state or the raw dump."
            return
        }

        foreach ($difference in @($differences | Select-Object -First $MaxDifferences)) {
            Write-Output "- $($difference.Key)"
            Write-Output "  LOCAL : $($difference.Local)"
            Write-Output "  REMOTE: $($difference.Remote)"
            if ($difference.Detail) {
                Write-Output "  DETAIL: $($difference.Detail)"
            }
        }

        if ($differences.Count -gt $MaxDifferences) {
            Write-Output "... $($differences.Count - $MaxDifferences) more difference(s) omitted; increase -MaxDifferences to show them."
        }

        $hint = Get-FocusHint -DifferenceKeys @($differences.Key)
        if ($hint) {
            Write-Output "Focus: $hint"
        }
    }

    $sources = @()
    if ($PSCmdlet.ParameterSetName -eq "Text") {
        $sources += [pscustomobject]@{
            Name = "provided text"
            Content = $PipedText.ToString()
        }
    }
    elseif ($Path -and $Path.Count -gt 0) {
        foreach ($requestedPath in $Path) {
            foreach ($resolvedPath in @(Resolve-Path -Path $requestedPath)) {
                if (-not (Test-Path -LiteralPath $resolvedPath.Path -PathType Leaf)) {
                    continue
                }
                $sources += [pscustomobject]@{
                    Name = $resolvedPath.Path
                    Content = Get-Content -LiteralPath $resolvedPath.Path -Raw
                }
            }
        }
    }
    else {
        try {
            $clipboardText = Get-Clipboard -Raw
        }
        catch {
            throw "No -Path or -Text was supplied, and the clipboard could not be read: $($_.Exception.Message)"
        }
        $sources += [pscustomobject]@{
            Name = "clipboard"
            Content = [string]$clipboardText
        }
    }

    if ($sources.Count -eq 0) {
        throw "No readable input files were found."
    }

    $dumpPattern = [regex]::new(
        '(?ms)^\s*State divergence message received for player\s+(?<player>\d+)\s+checksum ID\s+(?<id>\d+)!' +
            '(?<header>.*?)^\s*LOCAL STATE DUMP\s*\r?\n' +
            '(?<localDump>.*?)^\s*REMOTE STATE DUMP\s*\r?\n' +
            '(?<remoteDump>.*?)(?=^\s+at MegaCrit\.Sts2\.Core\.Multiplayer\.Game\.ChecksumTracker\.LogStateDivergence|^\s*State divergence message received for player|\z)',
        [System.Text.RegularExpressions.RegexOptions]::CultureInvariant
    )

    $totalDivergences = 0
    foreach ($source in $sources) {
        $matches = $dumpPattern.Matches([string]$source.Content)
        if ($matches.Count -eq 0) {
            Write-Warning "No detailed state-divergence dump was found in $($source.Name)."
            continue
        }

        for ($index = 0; $index -lt $matches.Count; $index++) {
            Write-DivergenceReport `
                -Match $matches[$index] `
                -Source $source.Name `
                -Index ($index + 1)
            $totalDivergences++
        }
    }

    if ($totalDivergences -eq 0) {
        throw "No detailed state-divergence dumps were found. Include the LOCAL STATE DUMP and REMOTE STATE DUMP sections."
    }
}

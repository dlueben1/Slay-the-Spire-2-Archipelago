<#
.SYNOPSIS
  Download the latest Slay the Spire II relic data and images.

.DESCRIPTION
  Fetches relic metadata from Spire Codex, removes fields that are not needed
  by the web app, writes the result to web/src/generated/relics.json, and
  downloads each relic's primary image into the matching web/public path.

.EXAMPLE
  .\scripts\download_latest_relics.ps1
#>

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$RepoRoot = Resolve-Path (Join-Path $PSScriptRoot "..") | Select-Object -ExpandProperty Path
$ApiUrl = "https://spire-codex.com/api/relics?lang=eng"
$ImageOrigin = "https://spire-codex.com"
$RelicsPath = Join-Path $RepoRoot "web\src\generated\relics.json"
$RelicsDirectory = Split-Path -Parent $RelicsPath
$WebPublicRoot = Join-Path $RepoRoot "web\public"

$ExcludedProperties = @(
    "merchant_price",
    "compendium_order",
    "name_variants",
    "image_variants",
    "description_raw",
    "flavor",
    "notes",
    "rarity"
)

function Get-ImageDownloadDetails {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ImageUrl,

        [Parameter(Mandatory = $true)]
        [string]$PublicRoot,

        [Parameter(Mandatory = $true)]
        [string]$Origin
    )

    try {
        $parsedUrl = [Uri]::new($ImageUrl, [UriKind]::RelativeOrAbsolute)
    } catch {
        throw "Relic image URL '$ImageUrl' is not a valid URI."
    }

    if ($parsedUrl.IsAbsoluteUri) {
        if ($parsedUrl.Scheme -ne "https" -or $parsedUrl.Host -ne "spire-codex.com") {
            throw "Relic image URL '$ImageUrl' must use HTTPS and the spire-codex.com host."
        }

        $remoteUrl = $parsedUrl
        $relativeImagePath = $parsedUrl.AbsolutePath.TrimStart("/")
    } else {
        if (-not $ImageUrl.StartsWith("/", [StringComparison]::Ordinal)) {
            throw "Relic image URL '$ImageUrl' must be root-relative or an HTTPS Spire Codex URL."
        }

        $remoteUrl = [Uri]::new([Uri]$Origin, $ImageUrl)
        $relativeImagePath = $parsedUrl.OriginalString.Split("?", 2)[0].TrimStart("/")
    }

    $relativeImagePath = [Uri]::UnescapeDataString($relativeImagePath)
    if ([string]::IsNullOrWhiteSpace($relativeImagePath)) {
        throw "Relic image URL '$ImageUrl' does not contain a file path."
    }

    $localRelativePath = $relativeImagePath -replace "/", [IO.Path]::DirectorySeparatorChar
    if ([IO.Path]::IsPathRooted($localRelativePath)) {
        throw "Relic image URL '$ImageUrl' resolves to an absolute local path."
    }

    try {
        $publicRootPath = [IO.Path]::GetFullPath($PublicRoot).TrimEnd([IO.Path]::DirectorySeparatorChar)
        $localPath = [IO.Path]::GetFullPath((Join-Path $publicRootPath $localRelativePath))
    } catch {
        throw "Relic image URL '$ImageUrl' could not be mapped to a local path."
    }

    $publicRootPrefix = $publicRootPath + [IO.Path]::DirectorySeparatorChar
    if (-not $localPath.StartsWith($publicRootPrefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Relic image URL '$ImageUrl' resolves outside web/public."
    }

    [pscustomobject]@{
        RemoteUrl = $remoteUrl.AbsoluteUri
        LocalPath = $localPath
    }
}

if (-not (Test-Path $RelicsDirectory -PathType Container)) {
    New-Item -ItemType Directory -Path $RelicsDirectory -Force | Out-Null
}

if (-not (Test-Path $WebPublicRoot -PathType Container)) {
    New-Item -ItemType Directory -Path $WebPublicRoot -Force | Out-Null
}

Write-Host "`nDownloading relic metadata..." -ForegroundColor Cyan
try {
    $apiResponse = Invoke-RestMethod -Method Get -Uri $ApiUrl
} catch {
    throw "Failed to download relic metadata from $ApiUrl`: $($_.Exception.Message)"
}

if ($null -eq $apiResponse -or $apiResponse -isnot [Array]) {
    throw "The relic API response was not an array."
}

$apiRelics = @($apiResponse)
if ($apiRelics.Count -eq 0) {
    throw "The relic API returned no relics."
}

$transformedRelics = foreach ($relicIndex in 0..($apiRelics.Count - 1)) {
    $relic = $apiRelics[$relicIndex]
    if ($null -eq $relic) {
        throw "The relic API returned a null relic at index $relicIndex."
    }

    $imageUrlProperty = $relic.PSObject.Properties["image_url"]
    if ($null -eq $imageUrlProperty -or [string]::IsNullOrWhiteSpace([string]$imageUrlProperty.Value)) {
        throw "Relic at index $relicIndex does not contain a non-empty image_url."
    }

    $transformedRelic = [ordered]@{}
    foreach ($property in $relic.PSObject.Properties) {
        if ($ExcludedProperties -notcontains $property.Name) {
            $transformedRelic[$property.Name] = $property.Value
        }
    }

    [pscustomobject]$transformedRelic
}

$temporaryRelicsPath = "$RelicsPath.tmp"
try {
    $relicJson = ConvertTo-Json -InputObject $transformedRelics -Depth 20
    [IO.File]::WriteAllText(
        $temporaryRelicsPath,
        $relicJson + [Environment]::NewLine,
        [Text.UTF8Encoding]::new($false)
    )
    Move-Item -LiteralPath $temporaryRelicsPath -Destination $RelicsPath -Force
} finally {
    if (Test-Path $temporaryRelicsPath -PathType Leaf) {
        Remove-Item -LiteralPath $temporaryRelicsPath -Force
    }
}

$writtenRelics = ConvertFrom-Json -InputObject (Get-Content -LiteralPath $RelicsPath -Raw)
if ($writtenRelics -isnot [Array]) {
    $writtenRelics = @($writtenRelics)
}
Write-Host "  Wrote $($writtenRelics.Count) relics to $RelicsPath" -ForegroundColor Green

Write-Host "`nDownloading relic images..." -ForegroundColor Cyan
for ($relicIndex = 0; $relicIndex -lt $writtenRelics.Count; $relicIndex++) {
    $relic = $writtenRelics[$relicIndex]
    $imageDetails = Get-ImageDownloadDetails -ImageUrl ([string]$relic.image_url) -PublicRoot $WebPublicRoot -Origin $ImageOrigin
    $imageDirectory = Split-Path -Parent $imageDetails.LocalPath

    if (-not (Test-Path $imageDirectory -PathType Container)) {
        New-Item -ItemType Directory -Path $imageDirectory -Force | Out-Null
    }

    try {
        Invoke-WebRequest -Method Get -Uri $imageDetails.RemoteUrl -OutFile $imageDetails.LocalPath -UseBasicParsing
    } catch {
        throw "Failed to download image for relic '$($relic.id)' from $($imageDetails.RemoteUrl): $($_.Exception.Message)"
    }

    Write-Progress -Activity "Downloading relic images" -Status "$($relic.name)" -PercentComplete ((($relicIndex + 1) / $writtenRelics.Count) * 100)
}
Write-Progress -Activity "Downloading relic images" -Completed

Write-Host "  Downloaded $($writtenRelics.Count) relic images to $WebPublicRoot" -ForegroundColor Green
Write-Host "`nDone! Relic metadata and images are up to date." -ForegroundColor Green
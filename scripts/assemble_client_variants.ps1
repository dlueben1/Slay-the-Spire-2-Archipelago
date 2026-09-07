param(
    [Parameter(Mandatory = $true)]
    [string]$Configuration,
    [Parameter(Mandatory = $true)]
    [string]$ClientProject,
    [Parameter(Mandatory = $true)]
    [string]$LoaderProject,
    [Parameter(Mandatory = $true)]
    [string]$PublicReferenceDir,
    [Parameter(Mandatory = $true)]
    [string]$BetaReferenceDir,
    [Parameter(Mandatory = $true)]
    [string]$ModsOutputDir
)

$ErrorActionPreference = "Stop"

function Get-Sha256Hex([string]$Path) {
    $stream = [IO.File]::OpenRead($Path)
    try {
        $sha256 = [Security.Cryptography.SHA256]::Create()
        try {
            $digest = $sha256.ComputeHash($stream)
        }
        finally {
            $sha256.Dispose()
        }
    }
    finally {
        $stream.Dispose()
    }

    return [BitConverter]::ToString($digest).Replace("-", "").ToLowerInvariant()
}

$clientProjectPath = [IO.Path]::GetFullPath($ClientProject)
$loaderProjectPath = [IO.Path]::GetFullPath($LoaderProject)
$clientDirectory = Split-Path -Parent $clientProjectPath
$modsOutputPath = [IO.Path]::GetFullPath($ModsOutputDir)
$expectedModsParent = [IO.Path]::GetFullPath((Join-Path (Split-Path -Parent (Split-Path -Parent $modsOutputPath)) "mods"))

if ((Split-Path -Leaf $modsOutputPath) -ne "Archipelago" -or
    [IO.Path]::GetFullPath((Split-Path -Parent $modsOutputPath)) -ne $expectedModsParent) {
    throw "Refusing to replace unexpected mod output path: $modsOutputPath"
}

$targets = @(
    @{ Version = "0.107.1"; References = [IO.Path]::GetFullPath($PublicReferenceDir) },
    @{ Version = "0.111.0"; References = [IO.Path]::GetFullPath($BetaReferenceDir) }
)

foreach ($target in $targets) {
    foreach ($assembly in @("sts2.dll", "0Harmony.dll", "GodotSharp.dll")) {
        $reference = Join-Path $target.References $assembly
        if (-not (Test-Path -LiteralPath $reference -PathType Leaf)) {
            throw "Missing $($target.Version) reference: $reference"
        }
    }

    Write-Host "Building Archipelago compatibility variant $($target.Version)..."
    & dotnet build $clientProjectPath -c $Configuration --no-restore --nologo --disable-build-servers `
        "-clp:ErrorsOnly;Summary" `
        "-p:Sts2ApiCompat=$($target.Version)" `
        "-p:STS2ReferenceDataDir=$($target.References)" `
        "-p:DllOnlyBuild=true"
    if ($LASTEXITCODE -ne 0) {
        throw "Archipelago $($target.Version) variant build failed with exit code $LASTEXITCODE."
    }
}

Write-Host "Building Archipelago compatibility loader..."
& dotnet build $loaderProjectPath -c $Configuration --nologo --disable-build-servers `
    "-clp:ErrorsOnly;Summary" `
    "-p:STS2ReferenceDataDir=$([IO.Path]::GetFullPath($PublicReferenceDir))"
if ($LASTEXITCODE -ne 0) {
    throw "Archipelago loader build failed with exit code $LASTEXITCODE."
}

$stageRoot = Join-Path ([IO.Path]::GetTempPath()) ("archipelago-variants-" + [Guid]::NewGuid().ToString("N"))
$null = New-Item -ItemType Directory -Path $stageRoot

try {
    Write-Host "Assembling compatibility bundle for $modsOutputPath..."
    $loaderDll = Join-Path (Split-Path -Parent $loaderProjectPath) "bin\$Configuration\net9.0\Archipelago.Loader.dll"
    Copy-Item -LiteralPath $loaderDll -Destination (Join-Path $stageRoot "Archipelago.dll")
    Copy-Item -LiteralPath (Join-Path $clientDirectory "Archipelago.json") -Destination $stageRoot

    foreach ($name in @("Archipelago.pck", "spire2.apworld")) {
        $source = Join-Path $modsOutputPath $name
        if (Test-Path -LiteralPath $source -PathType Leaf) {
            Copy-Item -LiteralPath $source -Destination $stageRoot
        }
    }

    $betaOutput = Join-Path $clientDirectory "bin\0.111.0\$Configuration\net9.0"
    Get-ChildItem -LiteralPath $betaOutput -Filter "*.dll" -File | Where-Object {
        $_.Name -notin @("Archipelago.dll", "0Harmony.dll", "GodotSharp.dll", "sts2.dll") -and
        -not $_.Name.StartsWith("STS2.RitsuLib", [StringComparison]::OrdinalIgnoreCase) -and
        -not $_.Name.StartsWith("STS2-RitsuLib", [StringComparison]::OrdinalIgnoreCase)
    } | Copy-Item -Destination $stageRoot

    $manifest = [ordered]@{
        schema = 1
        modVersion = (Get-Content -LiteralPath (Join-Path $clientDirectory "Archipelago.json") -Raw | ConvertFrom-Json).version
        variants = [ordered]@{}
    }

    foreach ($target in $targets) {
        $version = $target.Version
        $variantDirectory = Join-Path $stageRoot "lib\$version"
        $null = New-Item -ItemType Directory -Path $variantDirectory
        $variantDll = Join-Path $clientDirectory "bin\$version\$Configuration\net9.0\Archipelago.dll"
        $deployedDll = Join-Path $variantDirectory "Archipelago.dll"
        Copy-Item -LiteralPath $variantDll -Destination $deployedDll
        Set-Content -LiteralPath (Join-Path $variantDirectory "compat-target.txt") -Value $version -Encoding utf8
        $manifest.variants[$version] = [ordered]@{
            assembly = "lib/$version/Archipelago.dll"
            sha256 = Get-Sha256Hex $deployedDll
        }
    }

    $manifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $stageRoot "archipelago-variants.json") -Encoding utf8

    if (Test-Path -LiteralPath $modsOutputPath) {
        Remove-Item -LiteralPath $modsOutputPath -Recurse -Force
    }
    $null = New-Item -ItemType Directory -Path (Split-Path -Parent $modsOutputPath) -Force
    Move-Item -LiteralPath $stageRoot -Destination $modsOutputPath
    $stageRoot = $null
}
finally {
    if ($null -ne $stageRoot -and (Test-Path -LiteralPath $stageRoot)) {
        Remove-Item -LiteralPath $stageRoot -Recurse -Force
    }
}

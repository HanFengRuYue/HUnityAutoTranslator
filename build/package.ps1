param(
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"

$root = Resolve-Path -LiteralPath (Join-Path $PSScriptRoot "..")
$project = Join-Path $root "src\HUnityAutoTranslator.Plugin\HUnityAutoTranslator.Plugin.csproj"
$artifactsRoot = Join-Path $root "artifacts"
$packageRoot = Join-Path $artifactsRoot "HUnityAutoTranslator"
$pluginRoot = Join-Path $packageRoot "BepInEx\plugins\HUnityAutoTranslator"
$buildOutput = Join-Path $root "src\HUnityAutoTranslator.Plugin\bin\$Configuration\netstandard2.1"
$zipPath = Join-Path $artifactsRoot "HUnityAutoTranslator-0.1.0.zip"

dotnet build $project -c $Configuration

New-Item -ItemType Directory -Force -Path $artifactsRoot | Out-Null
if (Test-Path -LiteralPath $packageRoot) {
    $resolvedArtifacts = (Resolve-Path -LiteralPath $artifactsRoot).Path
    $resolvedPackage = (Resolve-Path -LiteralPath $packageRoot).Path
    if (-not $resolvedPackage.StartsWith($resolvedArtifacts, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to remove a path outside artifacts: $resolvedPackage"
    }

    Remove-Item -LiteralPath $packageRoot -Recurse -Force
}

New-Item -ItemType Directory -Force -Path $pluginRoot | Out-Null

$files = @(
    "HUnityAutoTranslator.Plugin.dll",
    "HUnityAutoTranslator.Core.dll",
    "Newtonsoft.Json.dll"
)

foreach ($file in $files) {
    $source = Join-Path $buildOutput $file
    if (Test-Path -LiteralPath $source) {
        Copy-Item -LiteralPath $source -Destination $pluginRoot -Force
    }
}

if (Test-Path -LiteralPath $zipPath) {
    Remove-Item -LiteralPath $zipPath -Force
}

Compress-Archive -Path (Join-Path $packageRoot "*") -DestinationPath $zipPath -Force

Write-Host "Package directory: $packageRoot"
Write-Host "Package zip: $zipPath"

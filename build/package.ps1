param(
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"

$root = Resolve-Path -LiteralPath (Join-Path $PSScriptRoot "..")
$project = Join-Path $root "src\HUnityAutoTranslator.Plugin\HUnityAutoTranslator.Plugin.csproj"
$outputRoot = Resolve-Path -LiteralPath $PSScriptRoot
$packageRoot = Join-Path $outputRoot "HUnityAutoTranslator"
$pluginRoot = Join-Path $packageRoot "BepInEx\plugins\HUnityAutoTranslator"
$buildOutput = Join-Path $root "src\HUnityAutoTranslator.Plugin\bin\$Configuration\netstandard2.1"
$zipPath = Join-Path $outputRoot "HUnityAutoTranslator-0.1.0.zip"

dotnet build $project -c $Configuration

New-Item -ItemType Directory -Force -Path $outputRoot | Out-Null
if (Test-Path -LiteralPath $packageRoot) {
    $resolvedOutputRoot = (Resolve-Path -LiteralPath $outputRoot).Path.TrimEnd([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar)
    $resolvedPackage = (Resolve-Path -LiteralPath $packageRoot).Path
    if (-not $resolvedPackage.StartsWith($resolvedOutputRoot + [System.IO.Path]::DirectorySeparatorChar, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to remove a path outside build output: $resolvedPackage"
    }

    Remove-Item -LiteralPath $packageRoot -Recurse -Force
}

New-Item -ItemType Directory -Force -Path $pluginRoot | Out-Null

Get-ChildItem -LiteralPath $buildOutput -Filter "*.dll" |
    Where-Object { $_.Name -notlike "BepInEx.*" -and $_.Name -notlike "UnityEngine.*" -and $_.Name -ne "0Harmony.dll" } |
    Copy-Item -Destination $pluginRoot -Force

$nativeSqlite = Join-Path $buildOutput "runtimes\win-x64\native\e_sqlite3.dll"
if (Test-Path -LiteralPath $nativeSqlite) {
    Copy-Item -LiteralPath $nativeSqlite -Destination $pluginRoot -Force
}
else {
    $nugetPackagesRoot = if ($env:NUGET_PACKAGES) {
        $env:NUGET_PACKAGES
    }
    else {
        Join-Path $env:USERPROFILE ".nuget\packages"
    }

    $nativeSqlite = Join-Path $nugetPackagesRoot "sqlitepclraw.lib.e_sqlite3\2.1.11\runtimes\win-x64\native\e_sqlite3.dll"
    if (Test-Path -LiteralPath $nativeSqlite) {
        Copy-Item -LiteralPath $nativeSqlite -Destination $pluginRoot -Force
    }
}

if (Test-Path -LiteralPath $zipPath) {
    Remove-Item -LiteralPath $zipPath -Force
}

Compress-Archive -Path (Join-Path $packageRoot "*") -DestinationPath $zipPath -Force

Write-Host "Package directory: $packageRoot"
Write-Host "Package zip: $zipPath"

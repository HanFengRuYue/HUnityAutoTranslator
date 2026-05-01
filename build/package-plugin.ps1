param(
    [string]$Configuration = "Release",
    [ValidateSet("BepInEx5", "Mono", "IL2CPP", "All")]
    [string]$Runtime = "All",
    [switch]$SkipNpmInstall
)

$ErrorActionPreference = "Stop"

$packageScript = Join-Path $PSScriptRoot "package.ps1"
$parameters = @{
    Configuration = $Configuration
    Runtime = $Runtime
    LlamaCppVariant = "None"
}

if ($SkipNpmInstall) {
    $parameters.SkipNpmInstall = $true
}

& $packageScript @parameters

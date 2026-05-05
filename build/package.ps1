param(
    [string]$Configuration = "Release",
    [string]$PackageVersion = "0.1.1",
    [ValidateSet("BepInEx5", "Mono", "IL2CPP", "All")]
    [string]$Runtime = "All",
    [ValidateSet("None", "Cuda13", "Vulkan", "All")]
    [string]$LlamaCppVariant = "All",
    [switch]$GeneratePanelOnly,
    [switch]$SkipNpmInstall
)

$ErrorActionPreference = "Stop"

$root = Resolve-Path -LiteralPath (Join-Path $PSScriptRoot "..")
$project = Join-Path $root "src\HUnityAutoTranslator.Plugin\HUnityAutoTranslator.Plugin.csproj"
$bepInEx5Project = Join-Path $root "src\HUnityAutoTranslator.Plugin.BepInEx5\HUnityAutoTranslator.Plugin.BepInEx5.csproj"
$outputRoot = Resolve-Path -LiteralPath $PSScriptRoot
$bepInEx5PackageRoot = Join-Path $outputRoot "HUnityAutoTranslator-bepinex5"
$bepInEx5PluginRoot = Join-Path $bepInEx5PackageRoot "BepInEx\plugins\HUnityAutoTranslator"
$packageRoot = Join-Path $outputRoot "HUnityAutoTranslator"
$pluginRoot = Join-Path $packageRoot "BepInEx\plugins\HUnityAutoTranslator"
$il2CppPackageRoot = Join-Path $outputRoot "HUnityAutoTranslator-il2cpp"
$il2CppPluginRoot = Join-Path $il2CppPackageRoot "BepInEx\plugins\HUnityAutoTranslator"
$buildOutput = Join-Path $root "src\HUnityAutoTranslator.Plugin\bin\$Configuration\netstandard2.1"
$bepInEx5ZipPath = Join-Path $outputRoot "HUnityAutoTranslator-$PackageVersion-bepinex5.zip"
$zipPath = Join-Path $outputRoot "HUnityAutoTranslator-$PackageVersion.zip"
$il2CppZipPath = Join-Path $outputRoot "HUnityAutoTranslator-$PackageVersion-il2cpp.zip"
$controlPanelRoot = Join-Path $root "src\HUnityAutoTranslator.ControlPanel"
$controlPanelBuildRoot = Join-Path $outputRoot ".control-panel-build"
$LlamaCppReleaseTag = "b8943"

function Invoke-CheckedNative([string]$Command, [string[]]$Arguments) {
    & $Command @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Command failed with exit code ${LASTEXITCODE}: $Command $($Arguments -join ' ')"
    }
}

function Normalize-Newlines([string]$Value) {
    return ($Value -replace "`r`n", "`n") -replace "`r", "`n"
}

function Resolve-ControlPanelDistAssetPath([string]$DistRoot, [string]$AssetReference) {
    $assetPath = $AssetReference.TrimStart(".", "/", "\").Replace("/", [System.IO.Path]::DirectorySeparatorChar)
    $fullPath = Join-Path $DistRoot $assetPath
    if (-not (Test-Path -LiteralPath $fullPath)) {
        throw "Referenced control panel asset not found: $AssetReference"
    }

    return $fullPath
}

function Read-ControlPanelDistAsset([string]$DistRoot, [string]$AssetReference) {
    $fullPath = Resolve-ControlPanelDistAssetPath $DistRoot $AssetReference
    $assetContent = Get-Content -LiteralPath $fullPath -Raw -Encoding UTF8
    return (Normalize-Newlines $assetContent)
}

function Read-ControlPanelBinaryAssetDataUri([string]$DistRoot, [string]$AssetReference, [string]$MimeType) {
    $fullPath = Resolve-ControlPanelDistAssetPath $DistRoot $AssetReference
    $iconDataUriPrefix = "data:image/x-icon;base64,"
    $bytes = [System.IO.File]::ReadAllBytes($fullPath)
    if ($MimeType -eq "image/x-icon") {
        return $iconDataUriPrefix + [Convert]::ToBase64String($bytes)
    }

    return "data:$MimeType;base64,$([Convert]::ToBase64String($bytes))"
}

function Convert-LocalIconLinksToDataUris([string]$Html, [string]$DistRoot) {
    return [regex]::Replace(
        $Html,
        '<link([^>]*\brel="(?:icon|shortcut icon)"[^>]*)\bhref="([^"]+)"([^>]*)>',
        {
            param($match)
            $href = $match.Groups[2].Value
            if ($href -match '^(?i:data:|https?://)') {
                return $match.Value
            }

            $dataUri = Read-ControlPanelBinaryAssetDataUri $DistRoot $href "image/x-icon"
            "<link$($match.Groups[1].Value)href=`"$dataUri`"$($match.Groups[3].Value)>"
        },
        [System.Text.RegularExpressions.RegexOptions]::IgnoreCase)
}

function Write-ControlPanelHtml([string]$InputHtml, [string]$OutputFile) {
    if (-not (Test-Path -LiteralPath $InputHtml)) {
        throw "Built control panel HTML not found: $InputHtml"
    }

    $inputPath = Resolve-Path -LiteralPath $InputHtml
    $distRoot = Split-Path -Parent $inputPath
    $html = Get-Content -LiteralPath $inputPath -Raw -Encoding UTF8
    $html = Normalize-Newlines $html

    if ($html -match '(?i)<(?:script|link|img|source|iframe|audio|video)[^>]+(?:src|href)="https?://') {
        throw "Generated control panel must not reference remote assets."
    }

    $html = Convert-LocalIconLinksToDataUris -Html $html -DistRoot $distRoot

    $html = [regex]::Replace(
        $html,
        '<link[^>]+rel="modulepreload"[^>]*>',
        '',
        [System.Text.RegularExpressions.RegexOptions]::IgnoreCase)

    $html = [regex]::Replace(
        $html,
        '<link[^>]+rel="stylesheet"[^>]+href="([^"]+)"[^>]*>',
        {
            param($match)
            $css = Read-ControlPanelDistAsset $distRoot $match.Groups[1].Value
            "<style>`n$css`n</style>"
        },
        [System.Text.RegularExpressions.RegexOptions]::IgnoreCase)

    $html = [regex]::Replace(
        $html,
        '<script([^>]*)src="([^"]+)"([^>]*)></script>',
        {
            param($match)
            $before = $match.Groups[1].Value -replace '\s*crossorigin', ''
            $after = $match.Groups[3].Value -replace '\s*crossorigin', ''
            $js = Read-ControlPanelDistAsset $distRoot $match.Groups[2].Value
            "<script$before$after>`n$js`n</script>"
        },
        [System.Text.RegularExpressions.RegexOptions]::IgnoreCase)

    if ($html -match '(?i)(?:src|href)="https?://') {
        throw "Generated control panel must not reference remote assets."
    }

    $delimiter = '""""""""'
    $content = @(
        'namespace HUnityAutoTranslator.Plugin;'
        ''
        'internal static class ControlPanelHtml'
        '{'
        '    // <auto-generated by build/package.ps1 -GeneratePanelOnly>'
        "    public const string Document = $delimiter"
        $html
        "$delimiter;"
        '}'
    ) -join "`n"

    $utf8NoBom = New-Object System.Text.UTF8Encoding($false)
    [System.IO.File]::WriteAllText($OutputFile, $content, $utf8NoBom)
    Write-Host "Generated control panel: $OutputFile"
}

function Copy-ControlPanelSource([string]$SourceRoot, [string]$TargetRoot, [bool]$PreserveDependencies) {
    if (-not $PreserveDependencies) {
        Remove-BuildSubdirectory -Path $TargetRoot
    }

    New-Item -ItemType Directory -Force -Path $TargetRoot | Out-Null
    $distPath = Join-Path $TargetRoot "dist"
    Remove-BuildSubdirectory -Path $distPath

    Get-ChildItem -LiteralPath $SourceRoot -Force |
        Where-Object { $_.Name -notin @("node_modules", "dist") } |
        ForEach-Object {
            Copy-Item -LiteralPath $_.FullName -Destination $TargetRoot -Recurse -Force
        }
}

function Build-ControlPanel([string]$ControlPanelRoot) {
    if (-not (Test-Path -LiteralPath (Join-Path $ControlPanelRoot "package.json"))) {
        return
    }

    Copy-ControlPanelSource -SourceRoot $ControlPanelRoot -TargetRoot $controlPanelBuildRoot -PreserveDependencies $SkipNpmInstall

    Push-Location $controlPanelBuildRoot
    try {
        if (-not $SkipNpmInstall) {
            if (Test-Path -LiteralPath "package-lock.json") {
                Invoke-CheckedNative "npm" @("ci")
            }
            else {
                Invoke-CheckedNative "npm" @("install")
            }
        }

        Invoke-CheckedNative "npm" @("run", "build")
    }
    finally {
        Pop-Location
    }

    $inputHtml = Join-Path $controlPanelBuildRoot "dist\index.html"
    $outputFile = Join-Path $root "src\HUnityAutoTranslator.Plugin\Web\ControlPanelHtml.cs"
    Write-ControlPanelHtml -InputHtml $inputHtml -OutputFile $outputFile
}

function Get-CheckedAsset([string]$AssetName, [string]$Sha256) {
    $cacheRoot = Join-Path $outputRoot ".cache\llama.cpp\$LlamaCppReleaseTag"
    New-Item -ItemType Directory -Force -Path $cacheRoot | Out-Null

    $assetPath = Join-Path $cacheRoot $AssetName
    $expectedHash = $Sha256.ToLowerInvariant()
    if (Test-Path -LiteralPath $assetPath) {
        $existingHash = (Get-FileHash -LiteralPath $assetPath -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($existingHash -eq $expectedHash) {
            return $assetPath
        }

        Remove-Item -LiteralPath $assetPath -Force
    }

    $assetUrl = "https://github.com/ggml-org/llama.cpp/releases/download/$LlamaCppReleaseTag/$AssetName"
    Write-Host "Downloading llama.cpp asset: $AssetName"
    Invoke-WebRequest -Uri $assetUrl -OutFile $assetPath

    $actualHash = (Get-FileHash -LiteralPath $assetPath -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actualHash -ne $expectedHash) {
        Remove-Item -LiteralPath $assetPath -Force
        throw "SHA256 mismatch for $AssetName. Expected $expectedHash but got $actualHash."
    }

    return $assetPath
}

function Expand-LlamaCppAsset([string]$AssetPath, [string]$TargetDirectory) {
    Expand-Archive -LiteralPath $AssetPath -DestinationPath $TargetDirectory -Force
}

function Add-LlamaCppBackend([string]$Variant, [string]$TargetRoot) {
    if ($Variant -eq "None") {
        return
    }

    $backend = ""
    $assets = @()
    if ($Variant -eq "Cuda13") {
        $backend = "CUDA 13.1"
        $assets = @(
            @{ Name = "llama-b8943-bin-win-cuda-13.1-x64.zip"; Sha256 = "b4a53f4fe822320357bc45b14d46bde1beadf6cc912a148d33b09b78482f20d7" },
            @{ Name = "cudart-llama-bin-win-cuda-13.1-x64.zip"; Sha256 = "f96935e7e385e3b2d0189239077c10fe8fd7e95690fea4afec455b1b6c7e3f18" }
        )
    }
    elseif ($Variant -eq "Vulkan") {
        $backend = "Vulkan"
        $assets = @(
            @{ Name = "llama-b8943-bin-win-vulkan-x64.zip"; Sha256 = "cb7bf6f828afd15885f5a0d9e279f6d6a988662e6ca2296308b31818a91d1534" }
        )
    }
    else {
        throw "Unknown llama.cpp package variant: $Variant"
    }

    $llamaRoot = Join-Path $TargetRoot "llama.cpp"
    New-Item -ItemType Directory -Force -Path $llamaRoot | Out-Null
    foreach ($asset in $assets) {
        $assetPath = Get-CheckedAsset -AssetName $asset.Name -Sha256 $asset.Sha256
        Expand-LlamaCppAsset -AssetPath $assetPath -TargetDirectory $llamaRoot
    }

    $serverPath = Join-Path $llamaRoot "llama-server.exe"
    if (-not (Test-Path -LiteralPath $serverPath)) {
        throw "llama.cpp package did not contain llama-server.exe for variant $Variant."
    }

    $manifest = @{
        Backend = $backend
        ServerPath = "llama-server.exe"
        Release = $LlamaCppReleaseTag
        Variant = $Variant
        Assets = @($assets | ForEach-Object { $_.Name })
    }
    $manifestPath = Join-Path $llamaRoot "backend.json"
    $manifest | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $manifestPath -Encoding UTF8
}

function Get-LlamaCppPackageVariants([string]$Variant) {
    if ($Variant -eq "All") {
        return @("Cuda13", "Vulkan")
    }

    if ($Variant -eq "None") {
        return @()
    }

    return @($Variant)
}

function Remove-BuildSubdirectory([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path)) {
        return
    }

    $resolvedOutputRoot = (Resolve-Path -LiteralPath $outputRoot).Path.TrimEnd([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar)
    $resolvedPath = (Resolve-Path -LiteralPath $Path).Path
    if (-not $resolvedPath.StartsWith($resolvedOutputRoot + [System.IO.Path]::DirectorySeparatorChar, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to remove a path outside build output: $resolvedPath"
    }

    Remove-Item -LiteralPath $Path -Recurse -Force
}

function Get-LlamaCppPackageZipName([string]$Variant) {
    if ($Variant -eq "Cuda13") {
        return "HUnityAutoTranslator-$PackageVersion-llamacpp-cuda13.zip"
    }

    if ($Variant -eq "Vulkan") {
        return "HUnityAutoTranslator-$PackageVersion-llamacpp-vulkan.zip"
    }

    throw "Unknown llama.cpp package variant: $Variant"
}

function Build-LlamaCppPackage([string]$Variant) {
    $suffix = $Variant.ToLowerInvariant()
    $llamaPackageRoot = Join-Path $outputRoot "HUnityAutoTranslator-llamacpp-$suffix"
    $llamaTargetRoot = Join-Path $llamaPackageRoot "BepInEx\plugins\HUnityAutoTranslator"
    $llamaZipPath = Join-Path $outputRoot (Get-LlamaCppPackageZipName -Variant $Variant)

    Remove-BuildSubdirectory -Path $llamaPackageRoot
    New-Item -ItemType Directory -Force -Path $llamaTargetRoot | Out-Null
    Add-LlamaCppBackend -Variant $Variant -TargetRoot $llamaTargetRoot

    if (Test-Path -LiteralPath $llamaZipPath) {
        Remove-Item -LiteralPath $llamaZipPath -Force
    }

    Compress-Archive -Path (Join-Path $llamaPackageRoot "*") -DestinationPath $llamaZipPath -Force
    Write-Host "llama.cpp package directory: $llamaPackageRoot"
    Write-Host "llama.cpp package zip: $llamaZipPath"
}

function Get-PluginRuntimeBuilds([string]$Runtime) {
    $builds = @()
    if ($Runtime -eq "BepInEx5" -or $Runtime -eq "All") {
        $builds += @{
            Runtime = "BepInEx5"
            Project = $bepInEx5Project
            TargetFramework = "netstandard2.1"
            PackageRoot = $bepInEx5PackageRoot
            PluginRoot = $bepInEx5PluginRoot
            ZipPath = $bepInEx5ZipPath
            AssemblyName = "HUnityAutoTranslator.Plugin.BepInEx5.dll"
        }
    }

    if ($Runtime -eq "Mono" -or $Runtime -eq "All") {
        $builds += @{
            Runtime = "Mono"
            TargetFramework = "netstandard2.1"
            PackageRoot = $packageRoot
            PluginRoot = $pluginRoot
            ZipPath = $zipPath
            AssemblyName = "HUnityAutoTranslator.Plugin.dll"
        }
    }

    if ($Runtime -eq "IL2CPP" -or $Runtime -eq "All") {
        $builds += @{
            Runtime = "IL2CPP"
            TargetFramework = "net6.0"
            PackageRoot = $il2CppPackageRoot
            PluginRoot = $il2CppPluginRoot
            ZipPath = $il2CppZipPath
            AssemblyName = "HUnityAutoTranslator.Plugin.IL2CPP.dll"
        }
    }

    return $builds
}

function Copy-NativeSqlite([string]$BuildOutput, [string]$TargetRoot) {
    $nativeSqlite = Join-Path $BuildOutput "runtimes\win-x64\native\e_sqlite3.dll"
    if (Test-Path -LiteralPath $nativeSqlite) {
        Copy-Item -LiteralPath $nativeSqlite -Destination $TargetRoot -Force
        return
    }

    $nugetPackagesRoot = if ($env:NUGET_PACKAGES) {
        $env:NUGET_PACKAGES
    }
    else {
        Join-Path $env:USERPROFILE ".nuget\packages"
    }

    $nativeSqlite = Join-Path $nugetPackagesRoot "sqlitepclraw.lib.e_sqlite3\2.1.11\runtimes\win-x64\native\e_sqlite3.dll"
    if (Test-Path -LiteralPath $nativeSqlite) {
        Copy-Item -LiteralPath $nativeSqlite -Destination $TargetRoot -Force
    }
}

function Build-PluginPackage([hashtable]$Build) {
    $runtimeProject = $Build.Project
    if (-not $runtimeProject) {
        $runtimeProject = $project
    }

    Invoke-CheckedNative "dotnet" @(
        "build",
        $runtimeProject,
        "-c",
        $Configuration,
        "-f",
        $Build.TargetFramework,
        "-p:Version=$PackageVersion",
        "-p:PackageVersion=$PackageVersion",
        "-p:BepInExPluginVersion=$PackageVersion",
        "-p:FileVersion=$PackageVersion",
        "-p:InformationalVersion=$PackageVersion"
    )

    $runtimePackageRoot = $Build.PackageRoot
    $runtimePluginRoot = $Build.PluginRoot
    $runtimeZipPath = $Build.ZipPath
    $runtimeProjectDirectory = Split-Path -Parent $runtimeProject
    $runtimeBuildOutput = Join-Path $runtimeProjectDirectory "bin\$Configuration\$($Build.TargetFramework)"

    Remove-BuildSubdirectory -Path $runtimePackageRoot
    New-Item -ItemType Directory -Force -Path $runtimePluginRoot | Out-Null

    Get-ChildItem -LiteralPath $runtimeBuildOutput -Filter "*.dll" |
        Where-Object { $_.Name -ne "BepInEx.dll" -and $_.Name -notlike "BepInEx.*" -and $_.Name -notlike "UnityEngine.*" -and $_.Name -ne "0Harmony.dll" } |
        Copy-Item -Destination $runtimePluginRoot -Force

    $expectedPlugin = Join-Path $runtimePluginRoot $Build.AssemblyName
    if (-not (Test-Path -LiteralPath $expectedPlugin)) {
        throw "$($Build.Runtime) package is missing plugin assembly: $($Build.AssemblyName)"
    }

    Copy-NativeSqlite -BuildOutput $runtimeBuildOutput -TargetRoot $runtimePluginRoot

    if (Test-Path -LiteralPath $runtimeZipPath) {
        Remove-Item -LiteralPath $runtimeZipPath -Force
    }

    Compress-Archive -Path (Join-Path $runtimePackageRoot "*") -DestinationPath $runtimeZipPath -Force

    Write-Host "$($Build.Runtime) package directory: $runtimePackageRoot"
    Write-Host "$($Build.Runtime) package zip: $runtimeZipPath"
}

if (Test-Path -LiteralPath (Join-Path $controlPanelRoot "package.json")) {
    Build-ControlPanel -ControlPanelRoot $controlPanelRoot
}

if ($GeneratePanelOnly) {
    return
}

New-Item -ItemType Directory -Force -Path $outputRoot | Out-Null
foreach ($pluginBuild in Get-PluginRuntimeBuilds -Runtime $Runtime) {
    Build-PluginPackage -Build $pluginBuild
}

foreach ($variant in Get-LlamaCppPackageVariants -Variant $LlamaCppVariant) {
    Build-LlamaCppPackage -Variant $variant
}

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

. (Join-Path $PSScriptRoot "lib\AssetCache.ps1")

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
$LlamaCppReleaseTag = "b9139"

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
    $assetUrl = "https://github.com/ggml-org/llama.cpp/releases/download/$LlamaCppReleaseTag/$AssetName"
    return Get-CheckedCachedAsset -AssetName $AssetName -Sha256 $Sha256 -DownloadUri $assetUrl -CacheDirectory $cacheRoot
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
            @{ Name = "llama-b9139-bin-win-cuda-13.1-x64.zip"; Sha256 = "7b0a01faa98b4384d34555983c9c6f9ea0bdc05c1b83882e9057c819c86ba2cc" },
            @{ Name = "cudart-llama-bin-win-cuda-13.1-x64.zip"; Sha256 = "f96935e7e385e3b2d0189239077c10fe8fd7e95690fea4afec455b1b6c7e3f18" }
        )
    }
    elseif ($Variant -eq "Vulkan") {
        $backend = "Vulkan"
        $assets = @(
            @{ Name = "llama-b9139-bin-win-vulkan-x64.zip"; Sha256 = "36100aa2a925b3f9452096f4b820aa43d57a35b37713fc300e15d96e589f3970" }
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
            # Plugin.BepInEx5 必须是 net462：Unity 2019.4 LTS 自带的 Mono 没有完整的 netstandard.dll 转发，
            # 直接打 .NET Framework 4.6.2 才能让 BepInEx 5 在老 Unity 上把 MonoBehaviour 实例化出来。
            TargetFramework = "net462"
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

function Test-UnityEngineModulesCached {
    # 插件 csproj 的 RestoreAdditionalProjectSources 里只挂了 nuget.samboy.dev，而它只为下载
    # UnityEngine.Modules 这一个固定版本包。包进了 NuGet 全局缓存后该源就不再需要——调用方据此
    # 把它从还原源里摘掉，免得它临时 502 时连累浮动版本（BepInEx.Analyzers 1.* 等）的还原（NU1301）。
    $projectXml = Get-Content -LiteralPath $project -Raw
    $match = [regex]::Match($projectXml, 'UnityEngine\.Modules"\s+Version="([^"]+)"')
    if (-not $match.Success) {
        return $false
    }

    $nugetPackagesRoot = if ($env:NUGET_PACKAGES) {
        $env:NUGET_PACKAGES
    }
    else {
        Join-Path $env:USERPROFILE ".nuget\packages"
    }

    # NuGet 解包完成后才会写入 .nupkg.metadata，用它判断是“完整缓存”而非半截解包。
    $packageMarker = Join-Path $nugetPackagesRoot "unityengine.modules\$($match.Groups[1].Value)\.nupkg.metadata"
    return (Test-Path -LiteralPath $packageMarker)
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
        # 见下方 $unityEngineModulesCached：缓存命中时清空 RestoreAdditionalProjectSources 摘掉 samboy.dev。
        if ($unityEngineModulesCached) { "-p:RestoreAdditionalProjectSources=" }
    )

    $runtimePackageRoot = $Build.PackageRoot
    $runtimePluginRoot = $Build.PluginRoot
    $runtimeZipPath = $Build.ZipPath
    $runtimeProjectDirectory = Split-Path -Parent $runtimeProject
    $runtimeBuildOutput = Join-Path $runtimeProjectDirectory "bin\$Configuration\$($Build.TargetFramework)"

    Remove-BuildSubdirectory -Path $runtimePackageRoot
    New-Item -ItemType Directory -Force -Path $runtimePluginRoot | Out-Null

    Get-ChildItem -LiteralPath $runtimeBuildOutput -Filter "*.dll" |
        Where-Object {
            $_.Name -ne "BepInEx.dll" -and
            $_.Name -notlike "BepInEx.*" -and
            $_.Name -notlike "UnityEngine.*" -and
            $_.Name -ne "0Harmony.dll" -and
            # 排除 SQLitePCLRaw 的 batteries_v2 + dynamic_cdecl provider：
            # Microsoft.Data.Sqlite 的 SqliteConnection 静态构造会反射调用 SQLitePCL.Batteries_V2.Init，
            # 而 net462 解析出来的 batteries_v2 实现走 MakeDynamic → RuntimeInformation.IsOSPlatform，
            # 部分 Unity Mono（Unity 6 / Unity 2019.4）不带这个程序集会 FileNotFoundException 让插件起不来。
            # SqliteTranslationCache.EnsureSqliteInitialized 在调任何 SqliteConnection 之前手工挂
            # SQLite3Provider_e_sqlite3，Type.GetType 找不到 Batteries_V2 时 Microsoft.Data.Sqlite 会跳过 Init。
            $_.Name -ne "SQLitePCLRaw.provider.dynamic_cdecl.dll" -and
            $_.Name -ne "SQLitePCLRaw.batteries_v2.dll"
        } |
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

# nuget.samboy.dev 只用来下载 UnityEngine.Modules（固定版本、编译期引用）。该包已缓存时就把这个
# 第三方源从插件项目的还原里摘掉，避免它临时不可用（如 502 Bad Gateway）时连累整个打包流程；
# 没缓存时保留它，让 NuGet 照常去拉包，拉不到也会给出准确的源不可达报错。
$unityEngineModulesCached = Test-UnityEngineModulesCached
if (-not $unityEngineModulesCached) {
    Write-Warning "UnityEngine.Modules is not in the NuGet cache; this build still needs nuget.samboy.dev. If that feed is unavailable, run 'dotnet restore' once while it is reachable to populate the cache."
}

foreach ($pluginBuild in Get-PluginRuntimeBuilds -Runtime $Runtime) {
    Build-PluginPackage -Build $pluginBuild
}

foreach ($variant in Get-LlamaCppPackageVariants -Variant $LlamaCppVariant) {
    Build-LlamaCppPackage -Variant $variant
}

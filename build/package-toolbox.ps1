param(
    [string]$Configuration = "Release",
    [string]$PackageVersion = "0.1.1",
    [string]$RuntimeIdentifier = "win-x64",
    [switch]$GenerateHtmlOnly,
    [switch]$SkipNpmInstall,
    [switch]$SkipBundleAssets
)

$ErrorActionPreference = "Stop"

. (Join-Path $PSScriptRoot "lib\AssetCache.ps1")

$root = Resolve-Path -LiteralPath (Join-Path $PSScriptRoot "..")
$toolboxProject = Join-Path $root "src\HUnityAutoTranslator.Toolbox\HUnityAutoTranslator.Toolbox.csproj"
$toolboxUiRoot = Join-Path $root "src\HUnityAutoTranslator.Toolbox.Ui"
$outputRoot = Resolve-Path -LiteralPath $PSScriptRoot
$publishRoot = Join-Path $outputRoot "HUnityAutoTranslator.Toolbox"
$singleExePath = Join-Path $outputRoot "HUnityAutoTranslator.Toolbox.exe"
$singleExeCachePath = "$singleExePath.WebView2"
$toolboxHtmlOutput = Join-Path $root "src\HUnityAutoTranslator.Toolbox\Web\ToolboxHtml.cs"
$embeddedAssetsStageRoot = Join-Path $root "src\HUnityAutoTranslator.Toolbox\EmbeddedAssets"
$embeddedManifestOutput = Join-Path $root "src\HUnityAutoTranslator.Toolbox.Core\Installation\EmbeddedAssetManifest.cs"
$bepInExCacheRoot = Join-Path $outputRoot ".cache\bepinex"

function Invoke-CheckedNative([string]$Command, [string[]]$Arguments) {
    & $Command @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Command failed with exit code ${LASTEXITCODE}: $Command $($Arguments -join ' ')"
    }
}

function Invoke-ToolboxNpmInstall {
    if (-not (Test-Path -LiteralPath "package-lock.json")) {
        Invoke-CheckedNative "npm" @("install")
        return
    }

    $previousErrorActionPreference = $ErrorActionPreference
    try {
        $ErrorActionPreference = "Continue"
        $ciOutput = & npm @("ci") 2>&1
        $ciExitCode = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $previousErrorActionPreference
    }

    if ($ciExitCode -eq 0) {
        $ciOutput | ForEach-Object { Write-Host $_ }
        return
    }

    if ($ciExitCode -eq -4048 -and (Test-Path -LiteralPath "node_modules")) {
        Write-Warning "npm ci could not remove a locked native dependency. Retrying with npm install without cleaning node_modules."
        Invoke-CheckedNative "npm" @("install", "--no-audit", "--no-fund")
        return
    }

    $ciOutput | ForEach-Object { Write-Host $_ }
    throw "Command failed with exit code ${ciExitCode}: npm ci"
}

function Normalize-Newlines([string]$Value) {
    return ($Value -replace "`r`n", "`n") -replace "`r", "`n"
}

function Remove-TrailingWhitespace([string]$Value) {
    return [regex]::Replace($Value, '[ \t]+(?=\n|$)', '')
}

function Get-AssetMimeType([string]$AssetPath) {
    switch ([System.IO.Path]::GetExtension($AssetPath).ToLowerInvariant()) {
        ".ico" { return "image/x-icon" }
        ".png" { return "image/png" }
        ".jpg" { return "image/jpeg" }
        ".jpeg" { return "image/jpeg" }
        ".svg" { return "image/svg+xml" }
        ".webp" { return "image/webp" }
        ".css" { return "text/css" }
        ".js" { return "text/javascript" }
        default { return "application/octet-stream" }
    }
}

function Resolve-ToolboxDistAssetPath([string]$DistRoot, [string]$AssetReference) {
    if ($AssetReference -match '^(?i:data:|https?://)') {
        throw "Remote or data asset references should not be resolved as files: $AssetReference"
    }

    $normalizedReference = $AssetReference -replace '/', [System.IO.Path]::DirectorySeparatorChar
    $distCandidate = if ([System.IO.Path]::IsPathRooted($normalizedReference)) {
        Join-Path $DistRoot $normalizedReference.TrimStart("\", "/")
    }
    else {
        Join-Path $DistRoot $normalizedReference
    }

    $distCandidate = [System.IO.Path]::GetFullPath($distCandidate)
    if (Test-Path -LiteralPath $distCandidate) {
        return $distCandidate
    }

    if ($AssetReference -match '^\.\./HUnityAutoTranslator\.ControlPanel[\\/]') {
        $relative = $AssetReference -replace '^\.\./HUnityAutoTranslator\.ControlPanel[\\/]', ''
        $controlPanelCandidate = Join-Path (Join-Path $root "src\HUnityAutoTranslator.ControlPanel") ($relative -replace '/', [System.IO.Path]::DirectorySeparatorChar)
        if (Test-Path -LiteralPath $controlPanelCandidate) {
            return (Resolve-Path -LiteralPath $controlPanelCandidate).Path
        }
    }

    throw "Referenced toolbox asset not found: $AssetReference"
}

function Read-ToolboxDistAsset([string]$DistRoot, [string]$AssetReference) {
    $fullPath = Resolve-ToolboxDistAssetPath $DistRoot $AssetReference
    $assetContent = Get-Content -LiteralPath $fullPath -Raw -Encoding UTF8
    return (Normalize-Newlines $assetContent)
}

function Read-ToolboxBinaryAssetDataUri([string]$DistRoot, [string]$AssetReference) {
    $fullPath = Resolve-ToolboxDistAssetPath $DistRoot $AssetReference
    $mimeType = Get-AssetMimeType $fullPath
    $bytes = [System.IO.File]::ReadAllBytes($fullPath)
    return "data:$mimeType;base64,$([Convert]::ToBase64String($bytes))"
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

            $dataUri = Read-ToolboxBinaryAssetDataUri $DistRoot $href
            "<link$($match.Groups[1].Value)href=`"$dataUri`"$($match.Groups[3].Value)>"
        },
        [System.Text.RegularExpressions.RegexOptions]::IgnoreCase)
}

function Write-ToolboxHtml([string]$InputHtml, [string]$OutputFile) {
    if (-not (Test-Path -LiteralPath $InputHtml)) {
        throw "Built toolbox HTML not found: $InputHtml"
    }

    $inputPath = Resolve-Path -LiteralPath $InputHtml
    $distRoot = Split-Path -Parent $inputPath
    $html = Get-Content -LiteralPath $inputPath -Raw -Encoding UTF8
    $html = Normalize-Newlines $html

    if ($html -match '(?i)<(?:script|link|img|source|iframe|audio|video)[^>]+(?:src|href)="https?://') {
        throw "Generated toolbox must not reference remote assets."
    }

    $html = Convert-LocalIconLinksToDataUris -Html $html -DistRoot $distRoot

    $scriptBlocks = [System.Collections.Generic.List[string]]::new()

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
            $css = Read-ToolboxDistAsset $distRoot $match.Groups[1].Value
            "<style>`n$css`n</style>"
        },
        [System.Text.RegularExpressions.RegexOptions]::IgnoreCase)

    $html = [regex]::Replace(
        $html,
        '<script([^>]*)src="([^"]+)"([^>]*)></script>',
        {
            param($match)
            $js = Read-ToolboxDistAsset $distRoot $match.Groups[2].Value
            $scriptBlocks.Add($js) | Out-Null
            ''
        },
        [System.Text.RegularExpressions.RegexOptions]::IgnoreCase)

    if ($scriptBlocks.Count -gt 0) {
        $inlineScripts = ($scriptBlocks | ForEach-Object {
            $script = $_
            "<script>`n$script`n</script>"
        }) -join "`n"

        $bodyCloseRegex = [regex]::new('</body>', [System.Text.RegularExpressions.RegexOptions]::IgnoreCase)
        if (-not $bodyCloseRegex.IsMatch($html)) {
            throw "Built toolbox HTML is missing a closing body tag."
        }

        $html = $bodyCloseRegex.Replace(
            $html,
            {
                param($match)
                "$inlineScripts`n$($match.Value)"
            },
            1)
    }

    if ($html -match '(?i)(?:src|href)="https?://') {
        throw "Generated toolbox must not reference remote assets."
    }

    $html = Remove-TrailingWhitespace $html

    $delimiter = '""""""""'
    $content = @(
        'namespace HUnityAutoTranslator.Toolbox;'
        ''
        'internal static class ToolboxHtml'
        '{'
        '    // <auto-generated by build/package-toolbox.ps1>'
        "    public const string Document = $delimiter"
        $html
        "$delimiter;"
        '}'
    ) -join "`n"

    $utf8NoBom = New-Object System.Text.UTF8Encoding($false)
    [System.IO.File]::WriteAllText($OutputFile, $content, $utf8NoBom)
    Write-Host "Generated toolbox HTML: $OutputFile"
}

function Build-ToolboxUi {
    if (-not (Test-Path -LiteralPath (Join-Path $toolboxUiRoot "package.json"))) {
        throw "Toolbox UI package.json not found: $toolboxUiRoot"
    }

    Push-Location $toolboxUiRoot
    try {
        if (-not $SkipNpmInstall) {
            Invoke-ToolboxNpmInstall
        }

        Invoke-CheckedNative "npm" @("run", "build")
    }
    finally {
        Pop-Location
    }

    $inputHtml = Join-Path $toolboxUiRoot "dist\index.html"
    Write-ToolboxHtml -InputHtml $inputHtml -OutputFile $toolboxHtmlOutput
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

function Remove-StageSubdirectory([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path)) {
        return
    }

    $expectedSuffix = [System.IO.Path]::Combine("src", "HUnityAutoTranslator.Toolbox", "EmbeddedAssets")
    $resolvedPath = (Resolve-Path -LiteralPath $Path).Path
    if (-not $resolvedPath.EndsWith($expectedSuffix, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to remove a path that is not the EmbeddedAssets stage: $resolvedPath"
    }

    Remove-Item -LiteralPath $Path -Recurse -Force
}

function Ensure-PluginAndLlamaCppZips {
    $required = @(
        "HUnityAutoTranslator-$PackageVersion-bepinex5.zip",
        "HUnityAutoTranslator-$PackageVersion.zip",
        "HUnityAutoTranslator-$PackageVersion-il2cpp.zip",
        "HUnityAutoTranslator-$PackageVersion-llamacpp-cuda13.zip",
        "HUnityAutoTranslator-$PackageVersion-llamacpp-vulkan.zip"
    )
    $missing = @()
    foreach ($name in $required) {
        $path = Join-Path $outputRoot $name
        if (-not (Test-Path -LiteralPath $path)) {
            $missing += $name
        }
    }

    if ($missing.Count -eq 0) {
        Write-Host "All plugin / llama.cpp zips already present in build/."
        return
    }

    Write-Host ("Missing plugin / llama.cpp zips: {0}. Building via package.ps1..." -f ($missing -join ", "))
    $packageScript = Join-Path $PSScriptRoot "package.ps1"
    $parameters = @{
        Configuration = $Configuration
        PackageVersion = $PackageVersion
        Runtime = "All"
        LlamaCppVariant = "All"
    }
    if ($SkipNpmInstall) {
        $parameters.SkipNpmInstall = $true
    }
    & $packageScript @parameters
}

function Format-CsharpStringLiteral([string]$Value) {
    if ($null -eq $Value) {
        return "null"
    }
    return '"' + ($Value -replace '\\', '\\' -replace '"', '\"') + '"'
}

function Write-EmbeddedAssetManifestCs([object[]]$Entries, [string]$OutputFile) {
    $lines = New-Object System.Collections.Generic.List[string]
    $lines.Add('// <auto-generated by build/package-toolbox.ps1>') | Out-Null
    $lines.Add('// This file is regenerated by the toolbox packaging script. Do not hand-edit.') | Out-Null
    $lines.Add('namespace HUnityAutoTranslator.Toolbox.Core.Installation;') | Out-Null
    $lines.Add('') | Out-Null
    $lines.Add('internal static class EmbeddedAssetManifest') | Out-Null
    $lines.Add('{') | Out-Null
    $lines.Add('    public static readonly IReadOnlyList<EmbeddedAsset> Entries = new EmbeddedAsset[]') | Out-Null
    $lines.Add('    {') | Out-Null
    for ($i = 0; $i -lt $Entries.Count; $i++) {
        $entry = $Entries[$i]
        $separator = if ($i -eq $Entries.Count - 1) { '' } else { ',' }
        $lines.Add('        new EmbeddedAsset(') | Out-Null
        $lines.Add('            Key: ' + (Format-CsharpStringLiteral $entry.Key) + ',') | Out-Null
        $lines.Add('            Kind: EmbeddedAssetKind.' + $entry.Kind + ',') | Out-Null
        $lines.Add('            Runtime: ToolboxRuntimeKind.' + $entry.Runtime + ',') | Out-Null
        $lines.Add('            Backend: LlamaCppBackendKind.' + $entry.Backend + ',') | Out-Null
        $lines.Add('            Version: ' + (Format-CsharpStringLiteral $entry.Version) + ',') | Out-Null
        $lines.Add('            Sha256: ' + (Format-CsharpStringLiteral $entry.Sha256) + ',') | Out-Null
        $lines.Add('            SizeBytes: ' + $entry.SizeBytes + 'L,') | Out-Null
        $lines.Add('            ResourceName: ' + (Format-CsharpStringLiteral $entry.ResourceName) + ')' + $separator) | Out-Null
    }
    $lines.Add('    };') | Out-Null
    $lines.Add('}') | Out-Null

    $utf8NoBom = New-Object System.Text.UTF8Encoding($false)
    [System.IO.File]::WriteAllText($OutputFile, (($lines.ToArray()) -join "`n"), $utf8NoBom)
    Write-Host "Generated embedded asset manifest: $OutputFile"
}

function Stage-EmbeddedAssetFile([string]$SourcePath, [string]$DestinationFileName) {
    $destination = Join-Path $embeddedAssetsStageRoot $DestinationFileName
    Copy-Item -LiteralPath $SourcePath -Destination $destination -Force
    $hash = (Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash.ToLowerInvariant()
    $size = (Get-Item -LiteralPath $destination).Length
    return @{ Path = $destination; Sha256 = $hash; SizeBytes = $size; FileName = $DestinationFileName }
}

function Bundle-EmbeddedAssets {
    Ensure-PluginAndLlamaCppZips

    Remove-StageSubdirectory -Path $embeddedAssetsStageRoot
    New-Item -ItemType Directory -Force -Path $embeddedAssetsStageRoot | Out-Null

    $entries = New-Object System.Collections.Generic.List[object]

    # 1. Plugin packages from build/
    $pluginPackages = @(
        @{ ZipName = "HUnityAutoTranslator-$PackageVersion-bepinex5.zip"; Key = "plugin-bepinex5"; Runtime = "BepInEx5Mono" },
        @{ ZipName = "HUnityAutoTranslator-$PackageVersion.zip";          Key = "plugin-mono";     Runtime = "Mono" },
        @{ ZipName = "HUnityAutoTranslator-$PackageVersion-il2cpp.zip";   Key = "plugin-il2cpp";   Runtime = "IL2CPP" }
    )
    foreach ($pkg in $pluginPackages) {
        $source = Join-Path $outputRoot $pkg.ZipName
        if (-not (Test-Path -LiteralPath $source)) {
            throw "Plugin package zip missing: $source"
        }
        $staged = Stage-EmbeddedAssetFile -SourcePath $source -DestinationFileName $pkg.ZipName
        $entries.Add([pscustomobject]@{
            Key = $pkg.Key
            Kind = "PluginPackage"
            Runtime = $pkg.Runtime
            Backend = "None"
            Version = $PackageVersion
            Sha256 = $staged.Sha256
            SizeBytes = $staged.SizeBytes
            ResourceName = "HUnityAutoTranslator.Toolbox.EmbeddedAssets.$($staged.FileName)"
        })
    }

    # 2. BepInEx framework zips (cache + verify + stage)
    $bepInExPins = Get-BepInExPins
    $bepInExMap = @(
        @{ Variant = "BepInEx5Mono";   Key = "bepinex5-framework";        Runtime = "BepInEx5Mono" },
        @{ Variant = "BepInEx6Mono";   Key = "bepinex6mono-framework";    Runtime = "Mono" },
        @{ Variant = "BepInEx6IL2CPP"; Key = "bepinex6il2cpp-framework";  Runtime = "IL2CPP" }
    )
    foreach ($item in $bepInExMap) {
        $pin = $bepInExPins[$item.Variant]
        $cached = Get-BepInExAsset -CacheRoot $bepInExCacheRoot -Variant $item.Variant
        $staged = Stage-EmbeddedAssetFile -SourcePath $cached -DestinationFileName $pin.Asset
        $entries.Add([pscustomobject]@{
            Key = $item.Key
            Kind = "BepInExFramework"
            Runtime = $item.Runtime
            Backend = "None"
            Version = $pin.Version
            Sha256 = $staged.Sha256
            SizeBytes = $staged.SizeBytes
            ResourceName = "HUnityAutoTranslator.Toolbox.EmbeddedAssets.$($staged.FileName)"
        })
    }

    # 3. llama.cpp backend packages from build/
    $llamaPackages = @(
        @{ ZipName = "HUnityAutoTranslator-$PackageVersion-llamacpp-cuda13.zip"; Key = "llamacpp-cuda13"; Backend = "Cuda13" },
        @{ ZipName = "HUnityAutoTranslator-$PackageVersion-llamacpp-vulkan.zip"; Key = "llamacpp-vulkan"; Backend = "Vulkan" }
    )
    foreach ($pkg in $llamaPackages) {
        $source = Join-Path $outputRoot $pkg.ZipName
        if (-not (Test-Path -LiteralPath $source)) {
            throw "llama.cpp package zip missing: $source"
        }
        $staged = Stage-EmbeddedAssetFile -SourcePath $source -DestinationFileName $pkg.ZipName
        $entries.Add([pscustomobject]@{
            Key = $pkg.Key
            Kind = "LlamaCppBackend"
            Runtime = "Unknown"
            Backend = $pkg.Backend
            Version = $PackageVersion
            Sha256 = $staged.Sha256
            SizeBytes = $staged.SizeBytes
            ResourceName = "HUnityAutoTranslator.Toolbox.EmbeddedAssets.$($staged.FileName)"
        })
    }

    Write-EmbeddedAssetManifestCs -Entries $entries.ToArray() -OutputFile $embeddedManifestOutput

    Write-Host ("Embedded asset bundle staged: {0} files at {1}" -f $entries.Count, $embeddedAssetsStageRoot)
}

Build-ToolboxUi

if ($GenerateHtmlOnly) {
    return
}

if (-not $SkipBundleAssets) {
    Bundle-EmbeddedAssets
}
else {
    Write-Host "-SkipBundleAssets specified; reusing staged EmbeddedAssets and manifest as-is."
}

Remove-BuildSubdirectory -Path $publishRoot
Remove-BuildSubdirectory -Path $singleExeCachePath
New-Item -ItemType Directory -Force -Path $publishRoot | Out-Null

Invoke-CheckedNative "dotnet" @(
    "publish",
    $toolboxProject,
    "-c",
    $Configuration,
    "-r",
    $RuntimeIdentifier,
    "--self-contained",
    "true",
    "-p:PublishSingleFile=true",
    "-p:IncludeNativeLibrariesForSelfExtract=true",
    "-p:EnableCompressionInSingleFile=true",
    "-p:Version=$PackageVersion",
    "-p:PackageVersion=$PackageVersion",
    "-p:FileVersion=$PackageVersion",
    "-p:InformationalVersion=$PackageVersion",
    "-o",
    $publishRoot
)

$publishedExe = Join-Path $publishRoot "HUnityAutoTranslator.Toolbox.exe"
if (-not (Test-Path -LiteralPath $publishedExe)) {
    throw "Published toolbox exe not found: $publishedExe"
}

Copy-Item -LiteralPath $publishedExe -Destination $singleExePath -Force
Remove-BuildSubdirectory -Path $publishRoot
Write-Host "Toolbox exe: $singleExePath"

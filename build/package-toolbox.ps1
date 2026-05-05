param(
    [string]$Configuration = "Release",
    [string]$PackageVersion = "0.1.1",
    [string]$RuntimeIdentifier = "win-x64",
    [switch]$GenerateHtmlOnly,
    [switch]$SkipNpmInstall
)

$ErrorActionPreference = "Stop"

$root = Resolve-Path -LiteralPath (Join-Path $PSScriptRoot "..")
$toolboxProject = Join-Path $root "src\HUnityAutoTranslator.Toolbox\HUnityAutoTranslator.Toolbox.csproj"
$toolboxUiRoot = Join-Path $root "src\HUnityAutoTranslator.Toolbox.Ui"
$outputRoot = Resolve-Path -LiteralPath $PSScriptRoot
$publishRoot = Join-Path $outputRoot "HUnityAutoTranslator.Toolbox"
$singleExePath = Join-Path $outputRoot "HUnityAutoTranslator.Toolbox.exe"
$singleExeCachePath = "$singleExePath.WebView2"
$toolboxHtmlOutput = Join-Path $root "src\HUnityAutoTranslator.Toolbox\Web\ToolboxHtml.cs"

function Invoke-CheckedNative([string]$Command, [string[]]$Arguments) {
    & $Command @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Command failed with exit code ${LASTEXITCODE}: $Command $($Arguments -join ' ')"
    }
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

Build-ToolboxUi

if ($GenerateHtmlOnly) {
    return
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

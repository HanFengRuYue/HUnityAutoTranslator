#
# Shared download / cache helpers used by build/package.ps1.
# Dot-source this file from a build script: `. (Join-Path $PSScriptRoot 'lib\AssetCache.ps1')`.
#
# Public functions:
#   Invoke-StreamingDownload         Stream a remote file to disk with retry.
#   Get-CheckedCachedAsset           Download (or reuse cached) asset, verify SHA256, return path.
#   Get-BepInExAsset                 Fetch BepInEx release asset by tag + filename, into bepinex cache.
#   Get-BepInExPins                  Return the pinned (Tag, Asset, Sha256) table for the three BepInEx variants.
#

[Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
Add-Type -AssemblyName System.Net.Http -ErrorAction SilentlyContinue

function Invoke-StreamingDownload([string]$Uri, [string]$OutFile) {
    $maxAttempts = 3
    $lastError = $null
    $partialPath = "$OutFile.partial"

    for ($attempt = 1; $attempt -le $maxAttempts; $attempt++) {
        if (Test-Path -LiteralPath $partialPath) {
            Remove-Item -LiteralPath $partialPath -Force
        }

        $handler = $null
        $client = $null
        $response = $null
        $contentStream = $null
        $fileStream = $null
        try {
            $handler = New-Object System.Net.Http.HttpClientHandler
            $handler.AutomaticDecompression = [System.Net.DecompressionMethods]::GZip -bor [System.Net.DecompressionMethods]::Deflate
            $client = New-Object System.Net.Http.HttpClient $handler
            $client.Timeout = [System.TimeSpan]::FromMinutes(30)
            $client.DefaultRequestHeaders.UserAgent.ParseAdd("HUnityAutoTranslator-Build/1.0") | Out-Null

            $response = $client.GetAsync($Uri, [System.Net.Http.HttpCompletionOption]::ResponseHeadersRead).GetAwaiter().GetResult()
            $response.EnsureSuccessStatusCode() | Out-Null

            $contentStream = $response.Content.ReadAsStreamAsync().GetAwaiter().GetResult()
            $fileStream = [System.IO.File]::Create($partialPath)
            $contentStream.CopyTo($fileStream, 81920)
            $fileStream.Flush()
            $fileStream.Dispose()
            $fileStream = $null

            if (Test-Path -LiteralPath $OutFile) {
                Remove-Item -LiteralPath $OutFile -Force
            }
            Move-Item -LiteralPath $partialPath -Destination $OutFile
            return
        }
        catch {
            $lastError = $_
            if ($null -ne $fileStream) {
                try { $fileStream.Dispose() } catch { }
            }
            if (Test-Path -LiteralPath $partialPath) {
                try { Remove-Item -LiteralPath $partialPath -Force } catch { }
            }
            if ($attempt -lt $maxAttempts) {
                $waitSeconds = [int][Math]::Min(30, [Math]::Pow(2, $attempt) * 2)
                Write-Warning ("Download attempt {0}/{1} failed: {2}. Retrying in {3}s..." -f $attempt, $maxAttempts, $_.Exception.Message, $waitSeconds)
                Start-Sleep -Seconds $waitSeconds
            }
        }
        finally {
            if ($null -ne $contentStream) { try { $contentStream.Dispose() } catch { } }
            if ($null -ne $response) { try { $response.Dispose() } catch { } }
            if ($null -ne $client) { try { $client.Dispose() } catch { } }
            if ($null -ne $handler) { try { $handler.Dispose() } catch { } }
        }
    }

    $errorMessage = if ($null -ne $lastError) { $lastError.Exception.Message } else { "unknown error" }
    throw ("Failed to download {0} after {1} attempts. Last error: {2}" -f $Uri, $maxAttempts, $errorMessage)
}

function Get-CheckedCachedAsset([string]$AssetName, [string]$Sha256, [string]$DownloadUri, [string]$CacheDirectory) {
    New-Item -ItemType Directory -Force -Path $CacheDirectory | Out-Null

    $assetPath = Join-Path $CacheDirectory $AssetName
    $expectedHash = if ([string]::IsNullOrWhiteSpace($Sha256)) { $null } else { $Sha256.ToLowerInvariant() }
    if (Test-Path -LiteralPath $assetPath) {
        if ($null -eq $expectedHash) {
            return $assetPath
        }
        $existingHash = (Get-FileHash -LiteralPath $assetPath -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($existingHash -eq $expectedHash) {
            return $assetPath
        }

        Remove-Item -LiteralPath $assetPath -Force
    }

    Write-Host "Downloading asset: $AssetName"
    Invoke-StreamingDownload -Uri $DownloadUri -OutFile $assetPath

    $actualHash = (Get-FileHash -LiteralPath $assetPath -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($null -ne $expectedHash -and $actualHash -ne $expectedHash) {
        Remove-Item -LiteralPath $assetPath -Force
        throw "SHA256 mismatch for $AssetName. Expected $expectedHash but got $actualHash."
    }
    if ($null -eq $expectedHash) {
        Write-Warning ("No SHA256 pin for {0}; observed hash: {1}" -f $AssetName, $actualHash)
    }

    return $assetPath
}

#
# BepInEx pin table. Pin both tag and SHA256 of the BepInEx asset.
# BepInEx 5 pins point at https://github.com/BepInEx/BepInEx/releases (stable releases).
# BepInEx 6 pins point at https://builds.bepinex.dev/projects/bepinex_be (Bleeding Edge);
#   BepInEx 6 has no stable release yet, and only BE builds ship a Cpp2IL/LibCpp2IL new
#   enough to load Unity 2022.2+ IL2CPP games (metadata version 31+). Each pin sets
#   `DownloadUri` directly so we don't accidentally fall back to GitHub.
# To rotate to a newer build:
#   1. Browse https://builds.bepinex.dev/projects/bepinex_be for the latest #<build>.
#   2. Download both win-x64 Mono and IL2CPP zips, compute SHA256:
#        (Get-FileHash <zip> -Algorithm SHA256).Hash.ToLowerInvariant()
#   3. Update Tag/Asset/Sha256/Version/DownloadUri below.
#
function Get-BepInExPins {
    return @{
        BepInEx5Mono = @{
            Tag = "v5.4.23.5"
            Asset = "BepInEx_win_x64_5.4.23.5.zip"
            Sha256 = "82f9878551030f54657792c0740d9d51a09500eeae1fba21106b0c441e6732c4"
            Version = "5.4.23.5"
        }
        BepInEx6Mono = @{
            Tag = "be.755"
            Asset = "BepInEx-Unity.Mono-win-x64-6.0.0-be.755.zip"
            Sha256 = "113f4f44d2d83480c0c3b7a6662340861e79cc56dd7c13c9d72d715a55f2ebde"
            Version = "6.0.0-be.755"
            DownloadUri = "https://builds.bepinex.dev/projects/bepinex_be/755/BepInEx-Unity.Mono-win-x64-6.0.0-be.755%2B3fab71a.zip"
        }
        BepInEx6IL2CPP = @{
            Tag = "be.755"
            Asset = "BepInEx-Unity.IL2CPP-win-x64-6.0.0-be.755.zip"
            Sha256 = "3616d6a67f5f595973ec4aa7bd7edaf7f799d5bb9926f7146a6dcc7b4abf478f"
            Version = "6.0.0-be.755"
            DownloadUri = "https://builds.bepinex.dev/projects/bepinex_be/755/BepInEx-Unity.IL2CPP-win-x64-6.0.0-be.755%2B3fab71a.zip"
        }
    }
}

function Get-BepInExAsset([string]$CacheRoot, [string]$Variant) {
    $pins = Get-BepInExPins
    if (-not $pins.ContainsKey($Variant)) {
        throw "Unknown BepInEx variant: $Variant. Expected one of: $($pins.Keys -join ', ')"
    }

    $pin = $pins[$Variant]
    $cacheDirectory = Join-Path $CacheRoot $pin.Tag
    if ($pin.ContainsKey('DownloadUri') -and -not [string]::IsNullOrWhiteSpace($pin.DownloadUri)) {
        $downloadUri = $pin.DownloadUri
    }
    else {
        $downloadUri = "https://github.com/BepInEx/BepInEx/releases/download/$($pin.Tag)/$($pin.Asset)"
    }
    return Get-CheckedCachedAsset -AssetName $pin.Asset -Sha256 $pin.Sha256 -DownloadUri $downloadUri -CacheDirectory $cacheDirectory
}

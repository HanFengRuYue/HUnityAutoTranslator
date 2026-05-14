#
# Shared download / cache helpers used by build/package.ps1 and build/package-toolbox.ps1.
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
# BepInEx pin table. Pin both tag and SHA256 of the GitHub release asset.
# To rotate to a newer release:
#   1. List candidates: `gh release list -R BepInEx/BepInEx --limit 8`
#   2. Download manually: `gh release download <tag> -R BepInEx/BepInEx --pattern '<asset>'`
#   3. Compute SHA: `(Get-FileHash <asset> -Algorithm SHA256).Hash.ToLowerInvariant()`
#   4. Update entries below.
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
            Tag = "v6.0.0-pre.2"
            Asset = "BepInEx-Unity.Mono-win-x64-6.0.0-pre.2.zip"
            Sha256 = "4699fadeeae31366a647026d8d992185a16070d2953636cd085ceadf75d3b26e"
            Version = "6.0.0-pre.2"
        }
        BepInEx6IL2CPP = @{
            Tag = "v6.0.0-pre.2"
            Asset = "BepInEx-Unity.IL2CPP-win-x64-6.0.0-pre.2.zip"
            Sha256 = "616ec7eb06cf11b2a0000e8fcef04d1b12bb58e84a2e0bdac9523234fc193ceb"
            Version = "6.0.0-pre.2"
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
    $downloadUri = "https://github.com/BepInEx/BepInEx/releases/download/$($pin.Tag)/$($pin.Asset)"
    return Get-CheckedCachedAsset -AssetName $pin.Asset -Sha256 $pin.Sha256 -DownloadUri $downloadUri -CacheDirectory $cacheDirectory
}

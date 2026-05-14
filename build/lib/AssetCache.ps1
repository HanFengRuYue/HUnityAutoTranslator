#
# Shared download / cache helpers used by build/package.ps1.
# Dot-source this file from a build script: `. (Join-Path $PSScriptRoot 'lib\AssetCache.ps1')`.
#
# Public functions:
#   Invoke-StreamingDownload         Stream a remote file to disk with retry.
#   Get-CheckedCachedAsset           Download (or reuse cached) asset, verify SHA256, return path.
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

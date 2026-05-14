using System.Net.Http;

namespace HUnityAutoTranslator.Toolbox.Core.Installation;

/// <summary>
/// Resolves a local copy of the Unity base library zip that BepInEx 6 IL2CPP would otherwise
/// fetch on first run. Resolution order: an explicit custom path (offline override) → the global
/// Toolbox cache under LocalAppData → a network download from <see cref="DefaultSourceTemplate"/>.
///
/// The resulting zip is placed at &lt;gameRoot&gt;/BepInEx/unity-libs/&lt;version&gt;.zip — BepInEx
/// itself checks for that file's existence and skips its own download when it's present.
/// </summary>
public sealed class UnityBaseLibraryProvider
{
    public const string DefaultSourceTemplate = "https://unity.bepinex.dev/libraries/{VERSION}.zip";

    private readonly string _cacheDirectory;
    private readonly string _sourceTemplate;
    private readonly Func<string, CancellationToken, Task<Stream>>? _httpDownloaderOverride;

    public UnityBaseLibraryProvider(
        string? cacheDirectory = null,
        string? sourceTemplate = null,
        Func<string, CancellationToken, Task<Stream>>? httpDownloaderOverride = null)
    {
        _cacheDirectory = cacheDirectory ?? GetDefaultCacheDirectory();
        _sourceTemplate = string.IsNullOrWhiteSpace(sourceTemplate) ? DefaultSourceTemplate : sourceTemplate;
        _httpDownloaderOverride = httpDownloaderOverride;
    }

    public static string GetDefaultCacheDirectory()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localAppData))
        {
            localAppData = Path.GetTempPath();
        }
        return Path.Combine(localAppData, "HUnityAutoTranslator", "Toolbox", "unity-libs");
    }

    public string GetTargetZipPath(string gameRoot, string unityVersion)
    {
        return Path.Combine(gameRoot, "BepInEx", "unity-libs", unityVersion + ".zip");
    }

    public sealed record StageResult(string SourceKind, string SourcePath, string DestinationPath, long SizeBytes);

    /// <summary>
    /// Ensure the Unity base library zip exists at the expected location inside the game folder.
    /// Reports byte progress through <paramref name="progress"/> when a download is needed.
    /// </summary>
    public async Task<StageResult> EnsureAsync(
        string gameRoot,
        string unityVersion,
        string? customZipPath,
        IProgress<UnityBaseLibraryProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(gameRoot))
            throw new ArgumentException("游戏目录为空。", nameof(gameRoot));
        if (string.IsNullOrWhiteSpace(unityVersion))
            throw new ArgumentException("Unity 版本为空。", nameof(unityVersion));

        var destination = GetTargetZipPath(gameRoot, unityVersion);
        var destinationDir = Path.GetDirectoryName(destination);
        if (!string.IsNullOrEmpty(destinationDir))
        {
            Directory.CreateDirectory(destinationDir);
        }

        // Path 1: user-provided local zip (works completely offline).
        if (!string.IsNullOrWhiteSpace(customZipPath))
        {
            var fullCustom = Path.GetFullPath(customZipPath);
            if (!File.Exists(fullCustom))
            {
                throw new FileNotFoundException($"自定义 Unity 库 zip 不存在：{fullCustom}", fullCustom);
            }
            progress?.Report(new UnityBaseLibraryProgress("UseCustom", $"使用自定义 Unity 库：{Path.GetFileName(fullCustom)}", 0, 0));
            CopyAtomic(fullCustom, destination);
            return new StageResult("LocalFile", fullCustom, destination, new FileInfo(destination).Length);
        }

        // Path 2: global Toolbox cache. Avoids re-downloading when multiple games share a Unity version.
        Directory.CreateDirectory(_cacheDirectory);
        var cachedPath = Path.Combine(_cacheDirectory, unityVersion + ".zip");
        if (File.Exists(cachedPath))
        {
            progress?.Report(new UnityBaseLibraryProgress("UseCache", $"复用缓存的 Unity {unityVersion} 库", 0, 0));
            CopyAtomic(cachedPath, destination);
            return new StageResult("Cache", cachedPath, destination, new FileInfo(destination).Length);
        }

        // Path 3: network download from unity.bepinex.dev → cache → game.
        var uri = _sourceTemplate.Replace("{VERSION}", unityVersion);
        progress?.Report(new UnityBaseLibraryProgress("Download", $"下载 Unity {unityVersion} 库", 0, 0));

        await DownloadToFileAsync(uri, cachedPath, progress, cancellationToken).ConfigureAwait(false);
        CopyAtomic(cachedPath, destination);
        return new StageResult("Download", uri, destination, new FileInfo(destination).Length);
    }

    private static void CopyAtomic(string source, string destination)
    {
        var temp = destination + ".tmp";
        if (File.Exists(temp)) File.Delete(temp);
        File.Copy(source, temp, overwrite: true);
        if (File.Exists(destination)) File.Delete(destination);
        File.Move(temp, destination);
    }

    private async Task DownloadToFileAsync(
        string uri,
        string destinationPath,
        IProgress<UnityBaseLibraryProgress>? progress,
        CancellationToken cancellationToken)
    {
        var temp = destinationPath + ".partial";
        if (File.Exists(temp)) File.Delete(temp);
        try
        {
            await using var source = _httpDownloaderOverride is not null
                ? await _httpDownloaderOverride(uri, cancellationToken).ConfigureAwait(false)
                : await OpenHttpStreamAsync(uri, cancellationToken).ConfigureAwait(false);

            await using (var sink = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                var buffer = new byte[81920];
                long bytesDownloaded = 0;
                long? totalBytes = source.CanSeek ? source.Length : null;
                int read;
                while ((read = await source.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false)) > 0)
                {
                    await sink.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                    bytesDownloaded += read;
                    progress?.Report(new UnityBaseLibraryProgress("Download",
                        $"下载 Unity 库 {bytesDownloaded / 1024} KB" + (totalBytes is long total ? $" / {total / 1024} KB" : string.Empty),
                        bytesDownloaded, totalBytes));
                }
            }

            if (File.Exists(destinationPath)) File.Delete(destinationPath);
            File.Move(temp, destinationPath);
        }
        catch
        {
            if (File.Exists(temp))
            {
                try { File.Delete(temp); } catch { /* swallow */ }
            }
            throw;
        }
    }

    private static async Task<Stream> OpenHttpStreamAsync(string uri, CancellationToken cancellationToken)
    {
        var handler = new HttpClientHandler
        {
            AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate
        };
        var client = new HttpClient(handler, disposeHandler: true) { Timeout = TimeSpan.FromMinutes(10) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("HUnityAutoTranslator-Toolbox/1.0");

        var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            client.Dispose();
            throw new InvalidOperationException(
                $"下载 Unity 基础库失败（HTTP {(int)response.StatusCode}）。"
                + $"\n请检查网络,或在「自定义安装 → 开发者」里指定本地 Unity 库 zip 路径。\nURL: {uri}");
        }
        return new HttpResponseStreamWrapper(client, response);
    }

    /// <summary>
    /// Wraps the response so disposing the file stream also disposes the underlying HttpClient.
    /// </summary>
    private sealed class HttpResponseStreamWrapper : Stream
    {
        private readonly HttpClient _client;
        private readonly HttpResponseMessage _response;
        private readonly Stream _inner;

        public HttpResponseStreamWrapper(HttpClient client, HttpResponseMessage response)
        {
            _client = client;
            _response = response;
            _inner = response.Content.ReadAsStream();
        }

        public override bool CanRead => _inner.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => _response.Content.Headers.ContentLength ?? _inner.Length;
        public override long Position { get => _inner.Position; set => throw new NotSupportedException(); }
        public override void Flush() => _inner.Flush();
        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            => _inner.ReadAsync(buffer, offset, count, cancellationToken);
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            => _inner.ReadAsync(buffer, cancellationToken);
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _inner.Dispose();
                _response.Dispose();
                _client.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}

public sealed record UnityBaseLibraryProgress(
    string Stage,
    string Message,
    long BytesTransferred,
    long? TotalBytes);

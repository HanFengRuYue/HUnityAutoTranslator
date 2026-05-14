using System.Security.Cryptography;
using HUnityAutoTranslator.Core.Http;

namespace HUnityAutoTranslator.Core.Control;

public sealed class LlamaCppModelDownloadManager : IDisposable
{
    private const int BufferSize = 128 * 1024;

    private readonly object _gate = new();
    private readonly IHttpTransport _transport;
    private readonly bool _ownsTransport;
    private readonly string _modelRoot;
    private readonly IReadOnlyList<LlamaCppModelDownloadPreset> _presets;
    private CancellationTokenSource? _activeCts;
    private Task? _activeTask;
    private LlamaCppModelDownloadStatus _status = LlamaCppModelDownloadStatus.Idle();

    public LlamaCppModelDownloadManager(string modelRoot)
        : this(new WebRequestHttpTransport(), modelRoot, LlamaCppModelDownloadPresets.All, ownsTransport: true)
    {
    }

    public LlamaCppModelDownloadManager(
        IHttpTransport transport,
        string modelRoot,
        IReadOnlyList<LlamaCppModelDownloadPreset>? presets = null)
        : this(transport, modelRoot, presets ?? LlamaCppModelDownloadPresets.All, ownsTransport: false)
    {
    }

    private LlamaCppModelDownloadManager(
        IHttpTransport transport,
        string modelRoot,
        IReadOnlyList<LlamaCppModelDownloadPreset> presets,
        bool ownsTransport)
    {
        _transport = transport;
        _modelRoot = modelRoot;
        _presets = presets;
        _ownsTransport = ownsTransport;
    }

    public IReadOnlyList<LlamaCppModelDownloadPreset> GetPresets()
    {
        return _presets;
    }

    public LlamaCppModelDownloadStatus GetStatus()
    {
        lock (_gate)
        {
            return _status;
        }
    }

    public LlamaCppModelDownloadStatus StartDownload(string? presetId)
    {
        var preset = _presets.FirstOrDefault(item => string.Equals(item.Id, presetId, StringComparison.OrdinalIgnoreCase));
        if (preset == null)
        {
            return SetStatus(new LlamaCppModelDownloadStatus(
                "error",
                presetId,
                null,
                null,
                null,
                0,
                0,
                0,
                "未知模型预设。",
                "unknown-preset",
                null,
                DateTimeOffset.UtcNow));
        }

        var targetDirectory = Path.Combine(_modelRoot, preset.Id);
        var targetPath = Path.Combine(targetDirectory, preset.FileName);
        if (File.Exists(targetPath))
        {
            return CompleteExistingFileIfValid(preset, targetPath);
        }

        lock (_gate)
        {
            if (_activeTask != null && !_activeTask.IsCompleted && _status.State == "downloading")
            {
                return _status with { Message = "已有模型下载任务正在运行，请等待完成或先取消。" };
            }

            Directory.CreateDirectory(targetDirectory);
            _activeCts?.Dispose();
            _activeCts = new CancellationTokenSource();
            _status = CreateStatus(
                "downloading",
                preset,
                targetPath,
                downloadedBytes: 0,
                totalBytes: preset.FileSizeBytes,
                message: "正在下载模型。",
                error: null,
                startedUtc: DateTimeOffset.UtcNow,
                completedUtc: null);
            _activeTask = Task.Run(() => DownloadAsync(preset, targetPath, _activeCts.Token));
            return _status;
        }
    }

    public LlamaCppModelDownloadStatus CancelDownload()
    {
        lock (_gate)
        {
            if (_status.State != "downloading" || _activeTask == null || _activeTask.IsCompleted)
            {
                _status = _status with { Message = "当前没有正在下载的模型。" };
                return _status;
            }

            _activeCts?.Cancel();
            return _status with
            {
                State = "cancelled",
                Message = "已请求取消模型下载。",
                CompletedUtc = DateTimeOffset.UtcNow
            };
        }
    }

    private LlamaCppModelDownloadStatus CompleteExistingFileIfValid(LlamaCppModelDownloadPreset preset, string targetPath)
    {
        if (!VerifyFile(targetPath, preset, out var error))
        {
            return SetStatus(CreateStatus(
                "error",
                preset,
                targetPath,
                downloadedBytes: new FileInfo(targetPath).Length,
                totalBytes: preset.FileSizeBytes,
                message: $"目标模型文件已存在但校验失败：{error}。请手动移走该文件后重试。",
                error: "existing-file-mismatch",
                startedUtc: null,
                completedUtc: DateTimeOffset.UtcNow));
        }

        return SetStatus(CreateStatus(
            "completed",
            preset,
            targetPath,
            downloadedBytes: preset.FileSizeBytes,
            totalBytes: preset.FileSizeBytes,
            message: "模型文件已存在，已直接使用。",
            error: null,
            startedUtc: null,
            completedUtc: DateTimeOffset.UtcNow));
    }

    private async Task DownloadAsync(LlamaCppModelDownloadPreset preset, string targetPath, CancellationToken cancellationToken)
    {
        var partPath = targetPath + ".part";
        try
        {
            if (File.Exists(partPath))
            {
                File.Delete(partPath);
            }

            var request = new HttpTransportRequest
            {
                Method = HttpTransportMethod.Get,
                Uri = new Uri(preset.DownloadUrl),
                Headers = new[] { new HttpHeaderEntry("User-Agent", "HUnityAutoTranslator/0.1") },
                ResponseHeadersOnly = true,
                // 多 GB 模型下载不能套默认 100s 超时，靠调用方的 cancellationToken 控制。
                Timeout = System.Threading.Timeout.InfiniteTimeSpan,
            };
            using var response = await _transport
                .SendStreamingAsync(request, cancellationToken)
                .ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException($"模型下载失败（HTTP {response.StatusCode}）。");
            }

            var totalBytes = response.ContentLength ?? preset.FileSizeBytes;
            var input = response.Body;
            using var output = new FileStream(partPath, FileMode.Create, FileAccess.Write, FileShare.None, BufferSize, useAsync: true);
            var buffer = new byte[BufferSize];
            long downloaded = 0;
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var read = await input.ReadAsync(buffer, 0, buffer.Length, cancellationToken).ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }

                cancellationToken.ThrowIfCancellationRequested();
                await output.WriteAsync(buffer, 0, read, cancellationToken).ConfigureAwait(false);
                downloaded += read;
                SetStatus(CreateStatus(
                    "downloading",
                    preset,
                    targetPath,
                    downloaded,
                    totalBytes,
                    "正在下载模型。",
                    null,
                    GetStatus().StartedUtc,
                    null));
            }
        }
        catch (OperationCanceledException)
        {
            TryDelete(partPath);
            SetStatus(CreateStatus(
                "cancelled",
                preset,
                targetPath,
                GetStatus().DownloadedBytes,
                preset.FileSizeBytes,
                "已取消模型下载。",
                null,
                GetStatus().StartedUtc,
                DateTimeOffset.UtcNow));
            return;
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException)
        {
            TryDelete(partPath);
            SetStatus(CreateStatus(
                "error",
                preset,
                targetPath,
                GetStatus().DownloadedBytes,
                preset.FileSizeBytes,
                $"模型下载失败：{ex.Message}",
                ex.GetType().Name,
                GetStatus().StartedUtc,
                DateTimeOffset.UtcNow));
            return;
        }

        try
        {
            if (!VerifyFile(partPath, preset, out var error))
            {
                throw new InvalidDataException(error);
            }

            if (File.Exists(targetPath))
            {
                if (VerifyFile(targetPath, preset, out _))
                {
                    TryDelete(partPath);
                    SetStatus(CreateStatus(
                        "completed",
                        preset,
                        targetPath,
                        preset.FileSizeBytes,
                        preset.FileSizeBytes,
                        "模型文件已存在，已直接使用。",
                        null,
                        GetStatus().StartedUtc,
                        DateTimeOffset.UtcNow));
                    return;
                }

                throw new IOException("目标模型文件已存在但校验不匹配，请手动移走该文件后重试。");
            }

            File.Move(partPath, targetPath);
            SetStatus(CreateStatus(
                "completed",
                preset,
                targetPath,
                preset.FileSizeBytes,
                preset.FileSizeBytes,
                "模型下载完成，已校验 SHA256。",
                null,
                GetStatus().StartedUtc,
                DateTimeOffset.UtcNow));
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException)
        {
            TryDelete(partPath);
            SetStatus(CreateStatus(
                "error",
                preset,
                targetPath,
                GetStatus().DownloadedBytes,
                preset.FileSizeBytes,
                $"模型下载失败：{ex.Message}",
                ex.GetType().Name,
                GetStatus().StartedUtc,
                DateTimeOffset.UtcNow));
        }
    }

    private LlamaCppModelDownloadStatus SetStatus(LlamaCppModelDownloadStatus status)
    {
        lock (_gate)
        {
            _status = status;
            return _status;
        }
    }

    private static LlamaCppModelDownloadStatus CreateStatus(
        string state,
        LlamaCppModelDownloadPreset preset,
        string? localPath,
        long downloadedBytes,
        long totalBytes,
        string message,
        string? error,
        DateTimeOffset? startedUtc,
        DateTimeOffset? completedUtc)
    {
        var total = totalBytes > 0 ? totalBytes : preset.FileSizeBytes;
        var progress = total > 0
            ? Math.Round(Math.Min(100, Math.Max(0, downloadedBytes * 100d / total)), 2)
            : 0;
        return new LlamaCppModelDownloadStatus(
            state,
            preset.Id,
            preset.Label,
            preset.FileName,
            localPath,
            downloadedBytes,
            total,
            progress,
            message,
            error,
            startedUtc,
            completedUtc);
    }

    private static bool VerifyFile(string path, LlamaCppModelDownloadPreset preset, out string error)
    {
        var file = new FileInfo(path);
        if (!file.Exists)
        {
            error = "文件不存在";
            return false;
        }

        if (file.Length != preset.FileSizeBytes)
        {
            error = $"文件大小不匹配，期望 {preset.FileSizeBytes}，实际 {file.Length}";
            return false;
        }

        var hash = ComputeSha256(path);
        if (!string.Equals(hash, preset.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            error = $"SHA256 不匹配，期望 {preset.Sha256}，实际 {hash}";
            return false;
        }

        error = string.Empty;
        return true;
    }

    private static string ComputeSha256(string path)
    {
        using var sha = SHA256.Create();
        using var stream = File.OpenRead(path);
        var hash = sha.ComputeHash(stream);
        return string.Concat(hash.Select(value => value.ToString("x2")));
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    public void Dispose()
    {
        _activeCts?.Cancel();
        _activeCts?.Dispose();
        if (_ownsTransport)
        {
            _transport.Dispose();
        }
    }
}

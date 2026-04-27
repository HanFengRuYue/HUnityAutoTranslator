using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using BepInEx.Logging;
using HUnityAutoTranslator.Core.Configuration;
using HUnityAutoTranslator.Core.Control;
using HUnityAutoTranslator.Core.Providers;
using Newtonsoft.Json;

namespace HUnityAutoTranslator.Plugin;

internal sealed class LlamaCppServerManager : IDisposable
{
    private static readonly TimeSpan HealthTimeout = TimeSpan.FromSeconds(2);

    private readonly object _gate = new();
    private readonly string _llamaDirectory;
    private readonly ManualLogSource _logger;
    private readonly HttpClient _httpClient = new();
    private Process? _process;
    private string _backend = string.Empty;
    private string? _lastOutput;
    private LlamaCppServerStatus? _status;

    public LlamaCppServerManager(string pluginDirectory, ManualLogSource logger)
    {
        _llamaDirectory = Path.Combine(pluginDirectory, "llama.cpp");
        _logger = logger;
    }

    public LlamaCppServerStatus GetStatus(RuntimeConfig config)
    {
        lock (_gate)
        {
            if (_process is { HasExited: true })
            {
                var exitCode = _process.ExitCode;
                var previous = _status;
                _process.Dispose();
                _process = null;
                _status = exitCode == 0
                    ? LlamaCppServerStatus.Stopped(
                        config.LlamaCpp,
                        previous?.Backend ?? _backend,
                        previous?.Installed ?? false,
                        previous?.Release,
                        previous?.Variant,
                        previous?.ServerPath)
                    : LlamaCppServerStatus.Error(
                        config.LlamaCpp,
                        previous?.Backend ?? _backend,
                        $"llama.cpp 已退出，退出码 {exitCode}。",
                        _lastOutput,
                        previous?.Port ?? 0,
                        previous?.Installed ?? false,
                        previous?.Release,
                        previous?.Variant,
                        previous?.ServerPath);
            }

            if (_process is { HasExited: false } && _status != null)
            {
                return _status with { ModelPath = config.LlamaCpp.ModelPath };
            }

            var manifest = LoadManifest(config.LlamaCpp);
            if (!manifest.Succeeded)
            {
                _status = manifest.Status;
                return manifest.Status with { ModelPath = config.LlamaCpp.ModelPath };
            }

            _backend = manifest.Backend;
            _status = LlamaCppServerStatus.Stopped(
                config.LlamaCpp,
                manifest.Backend,
                installed: true,
                release: manifest.Release,
                variant: manifest.Variant,
                serverPath: manifest.ServerPath);
            return _status;
        }
    }

    public async Task<LlamaCppServerStatus> StartAsync(RuntimeConfig config, CancellationToken cancellationToken)
    {
        var manifest = LoadManifest(config.LlamaCpp);
        if (!manifest.Succeeded)
        {
            return UpdateStatus(manifest.Status);
        }

        if (string.IsNullOrWhiteSpace(config.LlamaCpp.ModelPath) || !File.Exists(config.LlamaCpp.ModelPath))
        {
            return UpdateStatus(LlamaCppServerStatus.Error(
                config.LlamaCpp,
                manifest.Backend,
                "请先选择存在的 GGUF 模型文件。",
                installed: true,
                release: manifest.Release,
                variant: manifest.Variant,
                serverPath: manifest.ServerPath));
        }

        lock (_gate)
        {
            if (_process is { HasExited: false })
            {
                return _status ?? LlamaCppServerStatus.Starting(
                    config.LlamaCpp,
                    manifest.Backend,
                    0,
                    manifest.Release,
                    manifest.Variant,
                    manifest.ServerPath);
            }

            var port = GetFreeLoopbackPort();
            _backend = manifest.Backend;
            _lastOutput = null;
            var startInfo = new ProcessStartInfo
            {
                FileName = manifest.ServerPath,
                Arguments = LlamaCppServerCommandBuilder.BuildArguments(config.LlamaCpp, config.Provider, port),
                WorkingDirectory = _llamaDirectory,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            _process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
            _process.OutputDataReceived += (_, args) => AppendOutput(args.Data);
            _process.ErrorDataReceived += (_, args) => AppendOutput(args.Data);
            _process.Exited += (_, _) => _logger.LogInfo("llama.cpp server process exited.");
            if (!_process.Start())
            {
                _process.Dispose();
                _process = null;
                return UpdateStatus(LlamaCppServerStatus.Error(
                    config.LlamaCpp,
                    manifest.Backend,
                    "启动 llama.cpp 进程失败。",
                    installed: true,
                    release: manifest.Release,
                    variant: manifest.Variant,
                    serverPath: manifest.ServerPath));
            }

            _process.BeginOutputReadLine();
            _process.BeginErrorReadLine();
            _status = LlamaCppServerStatus.Starting(
                config.LlamaCpp,
                manifest.Backend,
                port,
                manifest.Release,
                manifest.Variant,
                manifest.ServerPath);
            _logger.LogInfo($"llama.cpp server started on random loopback port {port}.");
        }

        if (await IsReadyAsync(config, cancellationToken).ConfigureAwait(false))
        {
            return GetStatus(config);
        }

        var starting = GetStatus(config);
        return UpdateStatus(starting with { LastOutput = _lastOutput });
    }

    public LlamaCppServerStatus Stop(RuntimeConfig config)
    {
        StopOwnedProcess();
        var manifest = LoadManifest(config.LlamaCpp);
        return UpdateStatus(manifest.Succeeded
            ? LlamaCppServerStatus.Stopped(
                config.LlamaCpp,
                manifest.Backend,
                installed: true,
                release: manifest.Release,
                variant: manifest.Variant,
                serverPath: manifest.ServerPath)
            : manifest.Status);
    }

    public async Task<bool> IsReadyAsync(RuntimeConfig config, CancellationToken cancellationToken)
    {
        var status = GetStatus(config);
        if (status.State is "error" or "stopped" || status.Port <= 0)
        {
            return false;
        }

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(HealthTimeout);
            using var response = await _httpClient.GetAsync(
                $"http://127.0.0.1:{status.Port}/health",
                timeout.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return false;
            }

            UpdateStatus(LlamaCppServerStatus.Running(
                config.LlamaCpp,
                status.Backend,
                status.Port,
                status.Release,
                status.Variant,
                status.ServerPath,
                _lastOutput));
            return true;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            return false;
        }
    }

    private ManifestResult LoadManifest(LlamaCppConfig config)
    {
        var manifestPath = Path.Combine(_llamaDirectory, "backend.json");
        if (!File.Exists(manifestPath))
        {
            return ManifestResult.Failure(LlamaCppServerStatus.Error(
                config,
                string.Empty,
                "未检测到插件内 llama.cpp。请安装 CUDA 或 Vulkan 后端包。"));
        }

        try
        {
            var manifest = JsonConvert.DeserializeObject<BackendManifest>(File.ReadAllText(manifestPath)) ?? new BackendManifest();
            var backend = string.IsNullOrWhiteSpace(manifest.Backend) ? "unknown" : manifest.Backend.Trim();
            var variant = string.IsNullOrWhiteSpace(manifest.Variant) ? null : manifest.Variant.Trim();
            var release = string.IsNullOrWhiteSpace(manifest.Release) ? null : manifest.Release.Trim();
            var serverPath = string.IsNullOrWhiteSpace(manifest.ServerPath)
                ? Path.Combine(_llamaDirectory, "llama-server.exe")
                : Path.Combine(_llamaDirectory, manifest.ServerPath);
            if (!File.Exists(serverPath))
            {
                return ManifestResult.Failure(LlamaCppServerStatus.Error(
                    config,
                    backend,
                    "插件内 llama.cpp 不完整，未找到 llama-server.exe。",
                    installed: false,
                    release: release,
                    variant: variant,
                    serverPath: serverPath));
            }

            return ManifestResult.Success(backend, serverPath, release, variant);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return ManifestResult.Failure(LlamaCppServerStatus.Error(
                config,
                string.Empty,
                $"读取 llama.cpp 后端清单失败：{ex.Message}"));
        }
    }

    private LlamaCppServerStatus UpdateStatus(LlamaCppServerStatus status)
    {
        lock (_gate)
        {
            _status = status;
            return status;
        }
    }

    private void AppendOutput(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        lock (_gate)
        {
            var next = string.IsNullOrWhiteSpace(_lastOutput)
                ? value.Trim()
                : _lastOutput + "\n" + value.Trim();
            _lastOutput = next.Length <= 4000 ? next : next.Substring(next.Length - 4000);
        }
    }

    private void StopOwnedProcess()
    {
        Process? process;
        lock (_gate)
        {
            process = _process;
            _process = null;
        }

        if (process == null)
        {
            return;
        }

        try
        {
            if (!process.HasExited)
            {
                process.Kill();
                process.WaitForExit(3000);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            _logger.LogWarning($"Stopping llama.cpp server failed: {ex.Message}");
        }
        finally
        {
            process.Dispose();
        }
    }

    private static int GetFreeLoopbackPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }
        finally
        {
            listener.Stop();
        }
    }

    public void Dispose()
    {
        StopOwnedProcess();
        _httpClient.Dispose();
    }

    private sealed class BackendManifest
    {
        public string? Backend { get; set; }

        public string? ServerPath { get; set; }

        public string? Release { get; set; }

        public string? Variant { get; set; }
    }

    private sealed class ManifestResult
    {
        private ManifestResult(bool succeeded, string backend, string serverPath, string? release, string? variant, LlamaCppServerStatus status)
        {
            Succeeded = succeeded;
            Backend = backend;
            ServerPath = serverPath;
            Release = release;
            Variant = variant;
            Status = status;
        }

        public bool Succeeded { get; }

        public string Backend { get; }

        public string ServerPath { get; }

        public string? Release { get; }

        public string? Variant { get; }

        public LlamaCppServerStatus Status { get; }

        public static ManifestResult Success(string backend, string serverPath, string? release, string? variant)
        {
            return new ManifestResult(true, backend, serverPath, release, variant, LlamaCppServerStatus.Stopped(
                LlamaCppConfig.Default(),
                backend,
                installed: true,
                release: release,
                variant: variant,
                serverPath: serverPath));
        }

        public static ManifestResult Failure(LlamaCppServerStatus status)
        {
            return new ManifestResult(false, status.Backend, string.Empty, status.Release, status.Variant, status);
        }
    }
}

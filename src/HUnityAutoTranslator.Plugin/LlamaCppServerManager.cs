using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using BepInEx.Logging;
using HUnityAutoTranslator.Core.Configuration;
using HUnityAutoTranslator.Core.Control;
using HUnityAutoTranslator.Core.Providers;
using Newtonsoft.Json;

namespace HUnityAutoTranslator.Plugin;

internal sealed class LlamaCppServerManager : IDisposable
{
    private static readonly TimeSpan HealthTimeout = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan BenchmarkTimeout = TimeSpan.FromMinutes(5);
    private const string CudaLaunchQueuesVariable = "CUDA_SCALE_LAUNCH_QUEUES";
    private const string CudaLaunchQueuesValue = "4x";

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
            ApplyBackendEnvironment(startInfo, manifest.Backend);

            _process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
            _process.OutputDataReceived += (_, args) => AppendOutput(args.Data);
            _process.ErrorDataReceived += (_, args) => AppendOutput(args.Data);
            _process.Exited += (_, _) => _logger.LogInfo("llama.cpp 服务进程已退出。");
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
            _logger.LogInfo($"llama.cpp 服务已启动，本机端口：{port}。");
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

    public async Task<LlamaCppBenchmarkResult> BenchmarkAsync(RuntimeConfig config, CancellationToken cancellationToken)
    {
        var current = config.LlamaCpp;
        lock (_gate)
        {
            if (_process is { HasExited: false })
            {
                return LlamaCppBenchmarkResult.Failure(current, "请先停止本地模型后再运行性能基准。", _lastOutput);
            }
        }

        var manifest = LoadManifest(current);
        if (!manifest.Succeeded)
        {
            return LlamaCppBenchmarkResult.Failure(current, manifest.Status.Message, manifest.Status.LastOutput);
        }

        if (string.IsNullOrWhiteSpace(current.ModelPath) || !File.Exists(current.ModelPath))
        {
            return LlamaCppBenchmarkResult.Failure(current, "请先选择存在的 GGUF 模型文件。");
        }

        var benchPath = Path.Combine(_llamaDirectory, "llama-bench.exe");
        var batchedBenchPath = Path.Combine(_llamaDirectory, "llama-batched-bench.exe");
        if (!File.Exists(benchPath) || !File.Exists(batchedBenchPath))
        {
            return LlamaCppBenchmarkResult.Failure(current, "未找到 llama-bench.exe 或 llama-batched-bench.exe，请安装完整的 llama.cpp 后端包。");
        }

        var candidates = new List<LlamaCppBenchmarkCandidate>();
        var errors = new List<string>();
        var outputTail = new StringBuilder();

        var bench = await RunBenchmarkProcessAsync(
            benchPath,
            BuildLlamaBenchArguments(current),
            manifest.Backend,
            cancellationToken).ConfigureAwait(false);
        AppendTail(outputTail, bench.Output);
        if (!bench.Succeeded)
        {
            errors.Add(bench.Message);
            return LlamaCppBenchmarkResult.Failure(current, "llama-bench 运行失败，未保存配置。", Tail(outputTail.ToString()), errors);
        }

        var parsedBench = LlamaCppBenchmarkParser.ParseLlamaBenchJsonLines(bench.Output);
        candidates.AddRange(parsedBench.Candidates);
        errors.AddRange(parsedBench.Errors);
        if (parsedBench.Candidates.Count == 0)
        {
            return LlamaCppBenchmarkResult.Failure(current, "llama-bench 没有返回可用结果，未保存配置。", Tail(outputTail.ToString()), errors);
        }

        var kernelConfig = LlamaCppBenchmarkAdvisor.Recommend(current, parsedBench.Candidates);
        if (kernelConfig == null)
        {
            return LlamaCppBenchmarkResult.Failure(current, "未能从 llama-bench 结果生成推荐参数，未保存配置。", Tail(outputTail.ToString()), errors);
        }

        foreach (var slots in new[] { 1, 2, 4 })
        {
            var candidateConfig = kernelConfig with { ParallelSlots = slots };
            var parallel = await RunBenchmarkProcessAsync(
                batchedBenchPath,
                BuildLlamaBatchedBenchArguments(candidateConfig),
                manifest.Backend,
                cancellationToken).ConfigureAwait(false);
            AppendTail(outputTail, parallel.Output);
            if (!parallel.Succeeded)
            {
                errors.Add($"llama-batched-bench {slots} 槽位失败：{parallel.Message}");
                continue;
            }

            var parsedParallel = LlamaCppBenchmarkParser.ParseBatchedBenchJsonLines(parallel.Output);
            candidates.AddRange(parsedParallel.Candidates);
            errors.AddRange(parsedParallel.Errors);
        }

        if (!candidates.Any(candidate => string.Equals(candidate.Tool, "llama-batched-bench", StringComparison.Ordinal)))
        {
            return LlamaCppBenchmarkResult.Failure(current, "llama-batched-bench 没有成功结果，未保存配置。", Tail(outputTail.ToString()), errors);
        }

        var recommended = LlamaCppBenchmarkAdvisor.Recommend(current, candidates);
        if (recommended == null)
        {
            return LlamaCppBenchmarkResult.Failure(current, "未能生成可靠推荐配置，未保存配置。", Tail(outputTail.ToString()), errors);
        }

        return new LlamaCppBenchmarkResult(
            Succeeded: true,
            Saved: false,
            Message: "基准完成，已生成推荐参数。",
            CurrentConfig: current,
            RecommendedConfig: recommended,
            Candidates: candidates.ToArray(),
            Errors: errors.ToArray(),
            LastOutput: Tail(outputTail.ToString()));
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

    private static string BuildLlamaBenchArguments(LlamaCppConfig config)
    {
        return string.Join(" ", new[]
        {
            "-m",
            Quote(config.ModelPath!),
            "-ngl",
            config.GpuLayers.ToString(CultureInfo.InvariantCulture),
            "-p",
            "256",
            "-n",
            "64",
            "-r",
            "2",
            "-fa",
            "0,1",
            "-b",
            "512,2048",
            "-ub",
            "256,512",
            "-o",
            "jsonl"
        });
    }

    private static string BuildLlamaBatchedBenchArguments(LlamaCppConfig config)
    {
        var totalContext = Math.Max(1, config.ContextSize) * Math.Max(1, config.ParallelSlots);
        return string.Join(" ", new[]
        {
            "-m",
            Quote(config.ModelPath!),
            "-ngl",
            config.GpuLayers.ToString(CultureInfo.InvariantCulture),
            "-c",
            totalContext.ToString(CultureInfo.InvariantCulture),
            "-b",
            config.BatchSize.ToString(CultureInfo.InvariantCulture),
            "-ub",
            config.UBatchSize.ToString(CultureInfo.InvariantCulture),
            "-fa",
            ToFlashBenchArgument(config.FlashAttentionMode),
            "-npp",
            "256",
            "-ntg",
            "64",
            "-npl",
            config.ParallelSlots.ToString(CultureInfo.InvariantCulture),
            "--output-format",
            "jsonl"
        });
    }

    private async Task<BenchmarkProcessResult> RunBenchmarkProcessAsync(
        string fileName,
        string arguments,
        string backend,
        CancellationToken cancellationToken)
    {
        var output = new StringBuilder();
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            WorkingDirectory = _llamaDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        ApplyBackendEnvironment(startInfo, backend);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(BenchmarkTimeout);
        using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        var exited = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        process.OutputDataReceived += (_, args) => AppendProcessOutput(output, args.Data);
        process.ErrorDataReceived += (_, args) => AppendProcessOutput(output, args.Data);
        process.Exited += (_, _) =>
        {
            try
            {
                exited.TrySetResult(process.ExitCode);
            }
            catch (InvalidOperationException)
            {
                exited.TrySetResult(-1);
            }
        };

        try
        {
            if (!process.Start())
            {
                return new BenchmarkProcessResult(false, "启动 benchmark 进程失败。", output.ToString());
            }

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            using (timeout.Token.Register(() => exited.TrySetCanceled()))
            {
                int exitCode;
                try
                {
                    exitCode = await exited.Task.ConfigureAwait(false);
                }
                catch (TaskCanceledException)
                {
                    KillProcess(process);
                    return new BenchmarkProcessResult(false, "benchmark 超时。", output.ToString());
                }

                process.WaitForExit();
                var captured = output.ToString();
                return exitCode == 0
                    ? new BenchmarkProcessResult(true, "benchmark 完成。", captured)
                    : new BenchmarkProcessResult(false, $"benchmark 退出码 {exitCode}。", captured);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or IOException)
        {
            return new BenchmarkProcessResult(false, "benchmark 运行失败：" + ex.Message, output.ToString());
        }
    }

    private static void ApplyBackendEnvironment(ProcessStartInfo startInfo, string backend)
    {
        if (backend.IndexOf("CUDA", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            startInfo.EnvironmentVariables[CudaLaunchQueuesVariable] = CudaLaunchQueuesValue;
        }
    }

    private static void AppendProcessOutput(StringBuilder builder, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            lock (builder)
            {
                builder.AppendLine(value.Trim());
            }
        }
    }

    private static void AppendTail(StringBuilder builder, string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        builder.AppendLine(value.Trim());
        if (builder.Length > 8000)
        {
            var trimmed = Tail(builder.ToString(), 8000);
            builder.Clear();
            builder.Append(trimmed);
        }
    }

    private static string Tail(string value, int maxLength = 4000)
    {
        return value.Length <= maxLength ? value : value.Substring(value.Length - maxLength);
    }

    private static string ToFlashBenchArgument(string value)
    {
        return string.Equals(value, "on", StringComparison.OrdinalIgnoreCase) ? "1" : "0";
    }

    private static void KillProcess(Process process)
    {
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
        }
    }

    private static string Quote(string value)
    {
        return "\"" + value.Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
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
            _logger.LogWarning($"停止 llama.cpp 服务失败：{ex.Message}");
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

    private sealed class BenchmarkProcessResult
    {
        public BenchmarkProcessResult(bool succeeded, string message, string output)
        {
            Succeeded = succeeded;
            Message = message;
            Output = output;
        }

        public bool Succeeded { get; }

        public string Message { get; }

        public string Output { get; }
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

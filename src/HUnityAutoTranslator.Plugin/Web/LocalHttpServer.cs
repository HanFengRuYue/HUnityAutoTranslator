using System.Net;
using System.Net.Http;
using System.Text;
using BepInEx.Logging;
using HUnityAutoTranslator.Core.Caching;
using HUnityAutoTranslator.Core.Configuration;
using HUnityAutoTranslator.Core.Control;
using HUnityAutoTranslator.Core.Dispatching;
using HUnityAutoTranslator.Core.Glossary;
using HUnityAutoTranslator.Core.Pipeline;
using HUnityAutoTranslator.Core.Prompts;
using HUnityAutoTranslator.Core.Providers;
using HUnityAutoTranslator.Core.Queueing;
using HUnityAutoTranslator.Core.Text;
using HUnityAutoTranslator.Core.Textures;
using HUnityAutoTranslator.Plugin.Unity;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace HUnityAutoTranslator.Plugin;

internal sealed class LocalHttpServer : IDisposable
{
    private const int ManualWritebackPriority = (int)TranslationPriority.VisibleUi + 100;
    private const int MaxConcurrentHttpRequests = 4;
    private const long MaxJsonRequestBytes = 2L * 1024L * 1024L;
    private const long MaxTextureArchiveRequestBytes = 512L * 1024L * 1024L;
    private static readonly Encoding Utf8NoBom = new UTF8Encoding(false);

    private readonly ControlPanelService _controlPanel;
    private readonly ITranslationCache _cache;
    private readonly IGlossaryStore _glossary;
    private readonly TranslationJobQueue _queue;
    private readonly ResultDispatcher _dispatcher;
    private readonly UnityTextHighlighter? _highlighter;
    private readonly UnityTextureReplacementService _textureReplacement;
    private readonly LlamaCppServerManager? _llamaCppServer;
    private readonly LlamaCppModelDownloadManager _llamaCppModelDownloads;
    private readonly SelfCheckService _selfCheck;
    private readonly Func<MemoryDiagnosticsSnapshot> _memoryDiagnosticsProvider;
    private readonly HttpClient _httpClient = new();
    private readonly SemaphoreSlim _requestGate = new(MaxConcurrentHttpRequests, MaxConcurrentHttpRequests);
    private readonly ProviderUtilityClient _providerUtilityClient;
    private readonly ManualLogSource _logger;
    private readonly string _dataDirectory;
    private HttpListener? _listener;
    private CancellationTokenSource? _cts;

    public string Url { get; private set; } = string.Empty;

    public LocalHttpServer(
        ControlPanelService controlPanel,
        ITranslationCache cache,
        IGlossaryStore glossary,
        TranslationJobQueue queue,
        ResultDispatcher dispatcher,
        UnityTextHighlighter? highlighter,
        UnityTextureReplacementService textureReplacement,
        LlamaCppServerManager? llamaCppServer,
        LlamaCppModelDownloadManager llamaCppModelDownloads,
        SelfCheckService selfCheck,
        Func<MemoryDiagnosticsSnapshot> memoryDiagnosticsProvider,
        string dataDirectory,
        ManualLogSource logger)
    {
        _controlPanel = controlPanel;
        _cache = cache;
        _glossary = glossary;
        _queue = queue;
        _dispatcher = dispatcher;
        _highlighter = highlighter;
        _textureReplacement = textureReplacement;
        _llamaCppServer = llamaCppServer;
        _llamaCppModelDownloads = llamaCppModelDownloads;
        _selfCheck = selfCheck;
        _memoryDiagnosticsProvider = memoryDiagnosticsProvider;
        _providerUtilityClient = new ProviderUtilityClient(_httpClient, _controlPanel.GetApiKey);
        _logger = logger;
        _dataDirectory = dataDirectory;
    }

    public void Start(string host, int port)
    {
        var localHost = NormalizeLocalHost(host);
        var selectedPort = port <= 0 ? 48110 : port;
        Exception? lastError = null;

        for (var offset = 0; offset < 20; offset++)
        {
            var candidatePort = selectedPort + offset;
            var listener = new HttpListener();
            var url = $"http://{localHost}:{candidatePort}/";
            listener.Prefixes.Add(url);
            try
            {
                listener.Start();
                _listener = listener;
                Url = url;
                _cts = new CancellationTokenSource();
                _ = Task.Run(() => ServeAsync(_cts.Token));
                return;
            }
            catch (Exception ex)
            {
                listener.Close();
                lastError = ex;
            }
        }

        throw new InvalidOperationException("无法启动本机 HTTP 控制面板。", lastError);
    }

    private async Task ServeAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested && _listener?.IsListening == true)
        {
            try
            {
                var context = await _listener.GetContextAsync();
                _ = Task.Run(() => HandleWithConcurrencyLimitAsync(context, cancellationToken), cancellationToken);
            }
            catch (ObjectDisposedException)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"控制面板监听出错：{ex.Message}");
            }
        }
    }

    private async Task HandleWithConcurrencyLimitAsync(HttpListenerContext context, CancellationToken cancellationToken)
    {
        try
        {
            await _requestGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        try
        {
            await HandleAsync(context).ConfigureAwait(false);
        }
        finally
        {
            _requestGate.Release();
        }
    }

    private async Task HandleAsync(HttpListenerContext context)
    {
        try
        {
            context.Response.Headers["Cache-Control"] = "no-store";
            if (!IsLoopback(context))
            {
                context.Response.StatusCode = 403;
                await WriteTextAsync(context.Response, "仅允许本机访问。").ConfigureAwait(false);
                return;
            }

            var path = context.Request.Url?.AbsolutePath ?? "/";
            if (context.Request.HttpMethod == "GET" && path == "/api/state")
            {
                await WriteStateAsync(context.Response).ConfigureAwait(false);
            }
            else if (context.Request.HttpMethod == "POST" && path == "/api/config")
            {
                var request = await ReadJsonAsync<UpdateConfigRequest>(context.Request).ConfigureAwait(false);
                _controlPanel.UpdateConfig(request ?? new UpdateConfigRequest());
                _logger.LogInfo("控制面板设置已更新。");
                await WriteStateAsync(context.Response).ConfigureAwait(false);
            }
            else if (context.Request.HttpMethod == "POST" && path == "/api/key")
            {
                var body = await ReadBodyAsync(context.Request).ConfigureAwait(false);
                var apiKey = JObject.Parse(string.IsNullOrWhiteSpace(body) ? "{}" : body).Value<string>("ApiKey");
                _controlPanel.SetApiKey(apiKey ?? string.Empty);
                _logger.LogInfo(string.IsNullOrWhiteSpace(apiKey) ? "已清除 API Key。" : "已保存 API Key。");
                await WriteStateAsync(context.Response).ConfigureAwait(false);
            }
            else if (context.Request.HttpMethod == "POST" && path == "/api/texture-image/key")
            {
                var body = await ReadBodyAsync(context.Request).ConfigureAwait(false);
                var apiKey = JObject.Parse(string.IsNullOrWhiteSpace(body) ? "{}" : body).Value<string>("ApiKey");
                _controlPanel.SetTextureImageApiKey(apiKey ?? string.Empty);
                _logger.LogInfo(string.IsNullOrWhiteSpace(apiKey) ? "已清除贴图图片生成 API Key。" : "已保存贴图图片生成 API Key。");
                await WriteStateAsync(context.Response).ConfigureAwait(false);
            }
            else if (context.Request.HttpMethod == "POST" && path == "/api/texture-image/test")
            {
                await WriteJsonAsync(context.Response, await TestTextureImageConnectionAsync().ConfigureAwait(false)).ConfigureAwait(false);
            }
            else if (context.Request.HttpMethod == "GET" && path == "/api/self-check")
            {
                await WriteJsonAsync(context.Response, _selfCheck.GetLatestReport()).ConfigureAwait(false);
            }
            else if (context.Request.HttpMethod == "POST" && path == "/api/self-check/run")
            {
                await WriteJsonAsync(context.Response, _selfCheck.StartManualAsync()).ConfigureAwait(false);
            }
            else if (path.StartsWith("/api/texture-image-profiles", StringComparison.Ordinal))
            {
                await HandleTextureImageProviderProfilesAsync(context, path).ConfigureAwait(false);
            }
            else if (context.Request.HttpMethod == "POST" && path == "/api/fonts/pick")
            {
                await HandleFontPickAsync(context).ConfigureAwait(false);
            }
            else if (context.Request.HttpMethod == "POST" && path == "/api/llamacpp/model/pick")
            {
                await WriteJsonAsync(context.Response, WindowsLlamaCppModelFilePicker.PickModelFile()).ConfigureAwait(false);
            }
            else if (context.Request.HttpMethod == "GET" && path == "/api/llamacpp/model/presets")
            {
                await WriteJsonAsync(
                    context.Response,
                    _llamaCppModelDownloads.GetPresets()).ConfigureAwait(false);
            }
            else if (context.Request.HttpMethod == "POST" && path == "/api/llamacpp/model/download")
            {
                var request = await ReadJsonAsync<LlamaCppModelDownloadRequest>(context.Request).ConfigureAwait(false);
                var status = _llamaCppModelDownloads.StartDownload(request?.PresetId);
                await WriteJsonAsync(context.Response, status).ConfigureAwait(false);
            }
            else if (context.Request.HttpMethod == "GET" && path == "/api/llamacpp/model/download")
            {
                await WriteJsonAsync(
                    context.Response,
                    _llamaCppModelDownloads.GetStatus()).ConfigureAwait(false);
            }
            else if (context.Request.HttpMethod == "POST" && path == "/api/llamacpp/model/download/cancel")
            {
                var status = _llamaCppModelDownloads.CancelDownload();
                await WriteJsonAsync(context.Response, status).ConfigureAwait(false);
            }
            else if (context.Request.HttpMethod == "POST" && path == "/api/llamacpp/start")
            {
                var status = _llamaCppServer == null
                    ? LlamaCppServerStatus.Error(_controlPanel.GetConfig().LlamaCpp, string.Empty, "llama.cpp 本地模型管理器不可用。")
                    : await _llamaCppServer.StartAsync(_controlPanel.GetConfig(), CancellationToken.None).ConfigureAwait(false);
                _controlPanel.SetLlamaCppAutoStartOnStartup(status.State != "error");
                _controlPanel.SetLlamaCppStatus(status);
                await WriteJsonAsync(context.Response, status).ConfigureAwait(false);
            }
            else if (context.Request.HttpMethod == "POST" && path == "/api/llamacpp/stop")
            {
                var status = _llamaCppServer == null
                    ? LlamaCppServerStatus.Stopped(_controlPanel.GetConfig().LlamaCpp)
                    : _llamaCppServer.Stop(_controlPanel.GetConfig());
                _controlPanel.SetLlamaCppAutoStartOnStartup(false);
                _controlPanel.SetLlamaCppStatus(status);
                await WriteJsonAsync(context.Response, status).ConfigureAwait(false);
            }
            else if (context.Request.HttpMethod == "POST" && path == "/api/llamacpp/benchmark")
            {
                var result = _llamaCppServer == null
                    ? LlamaCppBenchmarkResult.Failure(_controlPanel.GetConfig().LlamaCpp, "llama.cpp 本地模型管理器不可用。")
                    : await _llamaCppServer.BenchmarkAsync(_controlPanel.GetConfig(), CancellationToken.None).ConfigureAwait(false);
                if (result.Succeeded && result.RecommendedConfig != null)
                {
                    var current = _controlPanel.GetConfig().LlamaCpp;
                    var savedConfig = current with
                    {
                        BatchSize = result.RecommendedConfig.BatchSize,
                        UBatchSize = result.RecommendedConfig.UBatchSize,
                        FlashAttentionMode = result.RecommendedConfig.FlashAttentionMode,
                        ParallelSlots = result.RecommendedConfig.ParallelSlots
                    };
                    _controlPanel.UpdateConfig(new UpdateConfigRequest(LlamaCpp: savedConfig));
                    result = result with
                    {
                        Saved = true,
                        Message = "基准完成，已自动保存推荐参数。",
                        RecommendedConfig = savedConfig
                    };
                }

                await WriteJsonAsync(context.Response, result).ConfigureAwait(false);
            }
            else if (context.Request.HttpMethod == "GET" && path == "/api/translations")
            {
                await WriteJsonAsync(context.Response, _cache.Query(ParseTranslationQuery(context.Request))).ConfigureAwait(false);
            }
            else if (context.Request.HttpMethod == "GET" && path == "/api/glossary")
            {
                await WriteJsonAsync(context.Response, _glossary.Query(ParseGlossaryQuery(context.Request))).ConfigureAwait(false);
            }
            else if (context.Request.HttpMethod == "GET" && path == "/api/glossary/filter-options")
            {
                await WriteJsonAsync(context.Response, _glossary.GetFilterOptions(ParseGlossaryFilterOptionsQuery(context.Request))).ConfigureAwait(false);
            }
            else if (context.Request.HttpMethod == "POST" && path == "/api/glossary")
            {
                var request = await ReadJsonAsync<GlossaryTermRequest>(context.Request).ConfigureAwait(false);
                if (request == null || string.IsNullOrWhiteSpace(request.SourceTerm) || string.IsNullOrWhiteSpace(request.TargetTerm))
                {
                    context.Response.StatusCode = 400;
                    await WriteTextAsync(context.Response, "Missing glossary term.").ConfigureAwait(false);
                    return;
                }

                var term = GlossaryTerm.CreateManual(
                    request.SourceTerm,
                    request.TargetTerm,
                    string.IsNullOrWhiteSpace(request.TargetLanguage) ? _controlPanel.GetConfig().TargetLanguage : request.TargetLanguage!,
                    request.Note) with
                {
                    Enabled = request.Enabled ?? true
                };
                _glossary.UpsertManual(term);
                await WriteJsonAsync(context.Response, _glossary.Query(new GlossaryQuery(null, "updated_utc", true, 0, 100))).ConfigureAwait(false);
            }
            else if (context.Request.HttpMethod == "PATCH" && path == "/api/glossary")
            {
                var request = await ReadJsonAsync<GlossaryTermRequest>(context.Request).ConfigureAwait(false);
                if (request == null || string.IsNullOrWhiteSpace(request.SourceTerm) || string.IsNullOrWhiteSpace(request.TargetTerm))
                {
                    context.Response.StatusCode = 400;
                    await WriteTextAsync(context.Response, "Missing glossary term.").ConfigureAwait(false);
                    return;
                }

                var term = GlossaryTerm.CreateManual(
                    request.SourceTerm,
                    request.TargetTerm,
                    string.IsNullOrWhiteSpace(request.TargetLanguage) ? _controlPanel.GetConfig().TargetLanguage : request.TargetLanguage!,
                    request.Note) with
                {
                    Enabled = request.Enabled ?? true,
                    UsageCount = request.UsageCount ?? 0
                };
                var savedTerm = _glossary.UpsertManual(term);
                DeleteRenamedGlossaryTermIfNeeded(request, savedTerm);
                await WriteJsonAsync(context.Response, _glossary.Query(new GlossaryQuery(null, "updated_utc", true, 0, 100))).ConfigureAwait(false);
            }
            else if (context.Request.HttpMethod == "DELETE" && path == "/api/glossary")
            {
                var entries = await ReadJsonAsync<List<GlossaryTermRequest>>(context.Request).ConfigureAwait(false);
                if (entries == null || entries.Count == 0)
                {
                    context.Response.StatusCode = 400;
                    await WriteTextAsync(context.Response, "Missing glossary rows.").ConfigureAwait(false);
                    return;
                }

                foreach (var entry in entries)
                {
                    if (!string.IsNullOrWhiteSpace(entry.SourceTerm))
                    {
                        _glossary.Delete(GlossaryTerm.CreateManual(
                            entry.SourceTerm,
                            string.IsNullOrWhiteSpace(entry.TargetTerm) ? entry.SourceTerm : entry.TargetTerm!,
                            string.IsNullOrWhiteSpace(entry.TargetLanguage) ? _controlPanel.GetConfig().TargetLanguage : entry.TargetLanguage!,
                            entry.Note));
                    }
                }

                await WriteJsonAsync(context.Response, new { DeletedCount = entries.Count }).ConfigureAwait(false);
            }
            else if (context.Request.HttpMethod == "GET" && path == "/api/translations/filter-options")
            {
                await WriteJsonAsync(context.Response, _cache.GetFilterOptions(ParseTranslationFilterOptionsQuery(context.Request))).ConfigureAwait(false);
            }
            else if (context.Request.HttpMethod == "POST" && path == "/api/translations/retranslate")
            {
                var entries = await ReadJsonAsync<List<TranslationCacheEntry>>(context.Request).ConfigureAwait(false);
                if (entries == null || entries.Count == 0)
                {
                    context.Response.StatusCode = 400;
                    await WriteTextAsync(context.Response, "Missing translation rows.").ConfigureAwait(false);
                    return;
                }

                var result = QueueRetranslations(entries);
                await WriteJsonAsync(context.Response, new
                {
                    RequestedCount = entries.Count,
                    result.QueuedCount,
                    result.PreservedCount
                }).ConfigureAwait(false);
            }
            else if (context.Request.HttpMethod == "POST" && path == "/api/translations/highlight")
            {
                var entry = await ReadJsonAsync<TranslationCacheEntry>(context.Request).ConfigureAwait(false);
                if (entry == null)
                {
                    context.Response.StatusCode = 400;
                    await WriteTextAsync(context.Response, "Missing translation row.").ConfigureAwait(false);
                    return;
                }

                var request = TranslationHighlightRequest.FromEntry(entry);
                var result = _highlighter == null
                    ? TranslationHighlightResult.UnsupportedTarget()
                    : _highlighter.RequestHighlight(request);
                await WriteJsonAsync(context.Response, result).ConfigureAwait(false);
            }
            else if (context.Request.HttpMethod == "PATCH" && path == "/api/translations")
            {
                var entry = await ReadJsonAsync<TranslationCacheEntry>(context.Request).ConfigureAwait(false);
                if (entry == null)
                {
                    context.Response.StatusCode = 400;
                    await WriteTextAsync(context.Response, "Missing translation row.").ConfigureAwait(false);
                    return;
                }

                entry = NormalizeManualEntry(entry) with { UpdatedUtc = DateTimeOffset.UtcNow };
                var previousTranslatedText = TryGetExistingTranslation(entry, out var existingTranslatedText)
                    ? existingTranslatedText
                    : null;
                _cache.Update(entry);
                if (entry.TranslatedText == null)
                {
                    PublishRestoreSourceWriteback(entry, previousTranslatedText);
                }
                else
                {
                    PublishManualWriteback(entry, previousTranslatedText);
                }
                await WriteJsonAsync(context.Response, _cache.Query(new TranslationCacheQuery(null, "updated_utc", true, 0, 100))).ConfigureAwait(false);
            }
            else if (context.Request.HttpMethod == "DELETE" && path == "/api/translations")
            {
                var entries = await ReadJsonAsync<List<TranslationCacheEntry>>(context.Request).ConfigureAwait(false);
                if (entries == null || entries.Count == 0)
                {
                    context.Response.StatusCode = 400;
                    await WriteTextAsync(context.Response, "Missing translation rows.").ConfigureAwait(false);
                    return;
                }

                foreach (var rawEntry in entries)
                {
                    var entry = NormalizeManualEntry(rawEntry) with { UpdatedUtc = DateTimeOffset.UtcNow };
                    var previousTranslatedText = TryGetExistingTranslation(entry, out var existingTranslatedText)
                        ? existingTranslatedText
                        : null;
                    _cache.Delete(entry);
                    PublishRestoreSourceWriteback(entry, previousTranslatedText);
                }

                await WriteJsonAsync(context.Response, new { DeletedCount = entries.Count }).ConfigureAwait(false);
            }
            else if (context.Request.HttpMethod == "GET" && path == "/api/translations/export")
            {
                var format = context.Request.QueryString["format"] ?? "json";
                context.Response.ContentType = string.Equals(format, "csv", StringComparison.OrdinalIgnoreCase)
                    ? "text/csv; charset=utf-8"
                    : "application/json; charset=utf-8";
                await WriteTextAsync(context.Response, _cache.Export(format)).ConfigureAwait(false);
            }
            else if (context.Request.HttpMethod == "POST" && path == "/api/translations/import")
            {
                var refreshSince = DateTimeOffset.UtcNow;
                var format = context.Request.QueryString["format"] ?? "json";
                var body = await ReadBodyAsync(context.Request).ConfigureAwait(false);
                var importResult = _cache.Import(body, format);
                var refreshQueued = PublishUpdatedWritebacks(refreshSince);
                await WriteJsonAsync(
                    context.Response,
                    new
                    {
                        importResult.ImportedCount,
                        importResult.Errors,
                        RefreshQueuedCount = refreshQueued
                    }).ConfigureAwait(false);
            }
            else if (context.Request.HttpMethod == "POST" && path == "/api/textures/scan")
            {
                var request = await ReadJsonAsync<TextureScanRequest>(context.Request).ConfigureAwait(false);
                await WriteJsonAsync(context.Response, await _textureReplacement.RequestScanAsync(request).ConfigureAwait(false)).ConfigureAwait(false);
            }
            else if (context.Request.HttpMethod == "POST" && path == "/api/textures/analyze-text")
            {
                var request = await ReadJsonAsync<TextureTextDetectionRequest>(context.Request).ConfigureAwait(false);
                await WriteJsonAsync(
                    context.Response,
                    await _textureReplacement.AnalyzeTextTexturesAsync(
                        request,
                        _controlPanel.GetReadyTextureImageProviderProfiles(),
                        _httpClient,
                        CancellationToken.None).ConfigureAwait(false)).ConfigureAwait(false);
            }
            else if (context.Request.HttpMethod == "POST" && path == "/api/textures/text-status")
            {
                var request = await ReadJsonAsync<TextureTextStatusUpdateRequest>(context.Request).ConfigureAwait(false);
                await WriteJsonAsync(context.Response, _textureReplacement.MarkTextStatus(request)).ConfigureAwait(false);
            }
            else if (context.Request.HttpMethod == "POST" && path == "/api/textures/translate-text")
            {
                var request = await ReadJsonAsync<TextureImageTranslateRequest>(context.Request).ConfigureAwait(false);
                await WriteJsonAsync(
                    context.Response,
                    await _textureReplacement.TranslateTextTexturesAsync(
                        request,
                        _controlPanel.GetConfig(),
                        _controlPanel.GetReadyTextureImageProviderProfiles(),
                        _httpClient,
                        CancellationToken.None).ConfigureAwait(false)).ConfigureAwait(false);
            }
            else if (context.Request.HttpMethod == "GET" && path == "/api/textures")
            {
                await WriteJsonAsync(context.Response, _textureReplacement.GetCatalog(ParseTextureCatalogQuery(context.Request))).ConfigureAwait(false);
            }
            else if (context.Request.HttpMethod == "GET" && path == "/api/textures/export")
            {
                context.Response.ContentType = "application/zip";
                context.Response.Headers["Content-Disposition"] = $"attachment; filename=\"{BuildTextureExportFileName()}\"";
                try
                {
                    await _textureReplacement.ExportArchiveAsync(
                        context.Response.OutputStream,
                        context.Request.QueryString["scene"]).ConfigureAwait(false);
                }
                finally
                {
                    context.Response.OutputStream.Close();
                }
            }
            else if (context.Request.HttpMethod == "GET" &&
                path.StartsWith("/api/textures/", StringComparison.Ordinal) &&
                path.EndsWith("/image", StringComparison.Ordinal))
            {
                var sourceHash = ExtractTextureImageHash(path);
                var variant = context.Request.QueryString["variant"] ?? "source";
                var imageBytes = Array.Empty<byte>();
                var hasImage = !string.IsNullOrWhiteSpace(sourceHash) &&
                    (string.Equals(variant, "source", StringComparison.OrdinalIgnoreCase)
                        ? _textureReplacement.TryGetSourceImage(sourceHash, out imageBytes)
                        : _textureReplacement.TryGetTextureImage(sourceHash, variant, out imageBytes));
                if (!hasImage)
                {
                    context.Response.StatusCode = 404;
                    await WriteTextAsync(context.Response, "贴图不存在。").ConfigureAwait(false);
                    return;
                }

                context.Response.ContentType = "image/png";
                context.Response.Headers["Cache-Control"] = "public, max-age=60";
                context.Response.Headers["X-Texture-Image-Variant"] = "variant=source|override";
                await WriteBytesAsync(context.Response, imageBytes).ConfigureAwait(false);
            }
            else if (context.Request.HttpMethod == "POST" && path == "/api/textures/import")
            {
                _logger.LogInfo("收到贴图包导入请求。");
                EnsureContentLengthWithinLimit(context.Request, MaxTextureArchiveRequestBytes);
                using var boundedArchive = new BoundedReadStream(context.Request.InputStream, MaxTextureArchiveRequestBytes);
                var result = await _textureReplacement.ImportOverridesAsync(boundedArchive).ConfigureAwait(false);
                _logger.LogInfo($"贴图包导入完成：导入 {result.ImportedCount} 张，应用 {result.AppliedCount} 个引用，错误 {result.Errors.Count} 条。");
                await WriteJsonAsync(context.Response, result).ConfigureAwait(false);
            }
            else if (context.Request.HttpMethod == "DELETE" && path == "/api/textures/overrides")
            {
                await WriteJsonAsync(context.Response, await _textureReplacement.ClearOverridesAsync().ConfigureAwait(false)).ConfigureAwait(false);
            }
            else if (path.StartsWith("/api/provider-profiles", StringComparison.Ordinal))
            {
                await HandleProviderProfilesAsync(context, path).ConfigureAwait(false);
            }
            else if (context.Request.HttpMethod == "GET" && path == "/api/provider/models")
            {
                var active = _controlPanel.GetReadyProviderRuntimeProfiles().FirstOrDefault();
                if (active == null)
                {
                    await WriteJsonAsync(context.Response, new ProviderModelsResult(false, "没有可用的在线服务商配置。", Array.Empty<ProviderModelInfo>())).ConfigureAwait(false);
                    return;
                }

                await WriteJsonAsync(
                    context.Response,
                    await CreateProviderUtilityClient(active).FetchModelsAsync(active.Profile, CancellationToken.None).ConfigureAwait(false)).ConfigureAwait(false);
            }
            else if (context.Request.HttpMethod == "GET" && path == "/api/provider/balance")
            {
                var active = _controlPanel.GetReadyProviderRuntimeProfiles().FirstOrDefault();
                if (active == null)
                {
                    await WriteJsonAsync(context.Response, new ProviderBalanceResult(false, "没有可用的在线服务商配置。", Array.Empty<ProviderBalanceInfo>())).ConfigureAwait(false);
                    return;
                }

                await WriteJsonAsync(
                    context.Response,
                    await CreateProviderUtilityClient(active).FetchBalanceAsync(active.Profile, CancellationToken.None).ConfigureAwait(false)).ConfigureAwait(false);
            }
            else if (context.Request.HttpMethod == "POST" && path == "/api/provider/test")
            {
                var config = _controlPanel.GetConfig();
                ProviderTestResult result;
                if (config.Provider.Kind == ProviderKind.LlamaCpp)
                {
                    var ready = _llamaCppServer != null && await _llamaCppServer.IsReadyAsync(config, CancellationToken.None).ConfigureAwait(false);
                    var status = _llamaCppServer?.GetStatus(config)
                        ?? LlamaCppServerStatus.Error(config.LlamaCpp, string.Empty, "llama.cpp 本地模型管理器不可用。");
                    _controlPanel.SetLlamaCppStatus(status);
                    result = new ProviderTestResult(
                        ready,
                        ready ? "llama.cpp 本地模型连接可用。" : status.Message);
                }
                else
                {
                    var active = _controlPanel.GetReadyProviderRuntimeProfiles().FirstOrDefault();
                    result = active == null
                        ? new ProviderTestResult(false, "没有可用的在线服务商配置。")
                        : await CreateProviderUtilityClient(active).TestConnectionAsync(active.Profile, CancellationToken.None).ConfigureAwait(false);
                }

                _controlPanel.SetProviderStatus(new ProviderStatus(result.Succeeded ? "ok" : "error", result.Message, DateTimeOffset.UtcNow));
                await WriteJsonAsync(context.Response, result).ConfigureAwait(false);
            }
            else if (context.Request.HttpMethod == "GET" && path == "/")
            {
                await WriteHtmlAsync(context.Response).ConfigureAwait(false);
            }
            else
            {
                context.Response.StatusCode = 404;
                await WriteTextAsync(context.Response, "未找到。").ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"控制面板请求处理失败：{ex.Message}");
            context.Response.StatusCode = 500;
            await WriteTextAsync(context.Response, "请求处理失败。").ConfigureAwait(false);
        }
    }

    private async Task WriteStateAsync(HttpListenerResponse response)
    {
        if (_llamaCppServer != null)
        {
            _controlPanel.SetLlamaCppStatus(_llamaCppServer.GetStatus(_controlPanel.GetConfig()));
        }

        await WriteJsonAsync(response, _controlPanel.GetState(
            _queue.PendingCount,
            _cache.Count,
            _dispatcher.PendingCount,
            _selfCheck.GetLatestReport(),
            _memoryDiagnosticsProvider())).ConfigureAwait(false);
    }

    private async Task HandleTextureImageProviderProfilesAsync(HttpListenerContext context, string path)
    {
        var request = context.Request;
        var response = context.Response;
        var segments = path.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
        if (request.HttpMethod == "GET" && path == "/api/texture-image-profiles")
        {
            await WriteJsonAsync(response, _controlPanel.GetState(_queue.PendingCount, _cache.Count, _dispatcher.PendingCount).TextureImageProviderProfiles ?? Array.Empty<TextureImageProviderProfileState>()).ConfigureAwait(false);
            return;
        }

        if (request.HttpMethod == "POST" && path == "/api/texture-image-profiles")
        {
            var createRequest = await ReadJsonAsync<TextureImageProviderProfileUpdateRequest>(request).ConfigureAwait(false);
            _controlPanel.CreateTextureImageProviderProfile(createRequest);
            await WriteStateAsync(response).ConfigureAwait(false);
            return;
        }

        if (request.HttpMethod == "POST" && path == "/api/texture-image-profiles/import")
        {
            var content = await ReadBodyAsync(request).ConfigureAwait(false);
            await WriteJsonAsync(response, _controlPanel.ImportTextureImageProviderProfile(content)).ConfigureAwait(false);
            return;
        }

        if (segments.Length < 3)
        {
            response.StatusCode = 404;
            await WriteTextAsync(response, "未找到贴图图片服务配置接口。").ConfigureAwait(false);
            return;
        }

        var id = Uri.UnescapeDataString(segments[2]);
        var action = segments.Length >= 4 ? segments[3] : string.Empty;
        if (request.HttpMethod == "PUT" && string.IsNullOrEmpty(action))
        {
            var updateRequest = await ReadJsonAsync<TextureImageProviderProfileUpdateRequest>(request).ConfigureAwait(false);
            _controlPanel.UpdateTextureImageProviderProfile(id, updateRequest ?? new TextureImageProviderProfileUpdateRequest());
            await WriteStateAsync(response).ConfigureAwait(false);
            return;
        }

        if (request.HttpMethod == "DELETE" && string.IsNullOrEmpty(action))
        {
            var deleted = _controlPanel.DeleteTextureImageProviderProfile(id);
            await WriteJsonAsync(response, new { DeletedCount = deleted ? 1 : 0 }).ConfigureAwait(false);
            return;
        }

        if (request.HttpMethod == "POST" && action == "move-up")
        {
            _controlPanel.MoveTextureImageProviderProfile(id, -1);
            await WriteStateAsync(response).ConfigureAwait(false);
            return;
        }

        if (request.HttpMethod == "POST" && action == "move-down")
        {
            _controlPanel.MoveTextureImageProviderProfile(id, 1);
            await WriteStateAsync(response).ConfigureAwait(false);
            return;
        }

        if (request.HttpMethod == "GET" && action == "export")
        {
            response.ContentType = "application/octet-stream; charset=utf-8";
            await WriteTextAsync(response, _controlPanel.ExportTextureImageProviderProfile(id)).ConfigureAwait(false);
            return;
        }

        if (!_controlPanel.TryGetTextureImageProviderProfile(id, out var profile))
        {
            response.StatusCode = 404;
            await WriteTextAsync(response, "贴图图片服务配置不存在。").ConfigureAwait(false);
            return;
        }

        if (request.HttpMethod == "POST" && action == "test")
        {
            if (string.IsNullOrWhiteSpace(profile.ApiKey))
            {
                await WriteJsonAsync(response, new ProviderTestResult(false, "请先保存贴图图片服务 API Key。")).ConfigureAwait(false);
                return;
            }

            var normalized = profile.Normalize();
            var client = new TextureImageEditClient(_httpClient, () => normalized.ApiKey);
            await WriteJsonAsync(response, await client.TestConnectionAsync(normalized.ToConfig(), CancellationToken.None).ConfigureAwait(false)).ConfigureAwait(false);
            return;
        }

        var utilityClient = CreateTextureImageProviderUtilityClient(profile);
        var utilityProfile = CreateTextureImageProviderProfile(profile);
        if (request.HttpMethod == "GET" && action == "models")
        {
            await WriteJsonAsync(response, await utilityClient.FetchModelsAsync(utilityProfile, CancellationToken.None).ConfigureAwait(false)).ConfigureAwait(false);
            return;
        }

        if (request.HttpMethod == "GET" && action == "balance")
        {
            await WriteJsonAsync(response, await utilityClient.FetchBalanceAsync(utilityProfile, CancellationToken.None).ConfigureAwait(false)).ConfigureAwait(false);
            return;
        }

        response.StatusCode = 404;
        await WriteTextAsync(response, "未找到贴图图片服务配置接口。").ConfigureAwait(false);
    }

    private async Task HandleProviderProfilesAsync(HttpListenerContext context, string path)
    {
        var request = context.Request;
        var response = context.Response;
        var segments = path.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
        if (request.HttpMethod == "GET" && path == "/api/provider-profiles")
        {
            await WriteJsonAsync(response, _controlPanel.GetState(_queue.PendingCount, _cache.Count, _dispatcher.PendingCount).ProviderProfiles ?? Array.Empty<ProviderProfileState>()).ConfigureAwait(false);
            return;
        }

        if (request.HttpMethod == "POST" && path == "/api/provider-profiles")
        {
            var createRequest = await ReadJsonAsync<ProviderProfileUpdateRequest>(request).ConfigureAwait(false);
            _controlPanel.CreateProviderProfile(createRequest);
            await WriteStateAsync(response).ConfigureAwait(false);
            return;
        }

        if (request.HttpMethod == "POST" && path == "/api/provider-profiles/import")
        {
            var content = await ReadBodyAsync(request).ConfigureAwait(false);
            await WriteJsonAsync(response, _controlPanel.ImportProviderProfile(content)).ConfigureAwait(false);
            return;
        }

        if (segments.Length == 4 &&
            string.Equals(segments[2], "draft", StringComparison.OrdinalIgnoreCase))
        {
            await HandleDraftProviderProfileUtilityAsync(context, segments[3]).ConfigureAwait(false);
            return;
        }

        if (segments.Length < 3)
        {
            response.StatusCode = 404;
            await WriteTextAsync(response, "未找到服务商配置接口。").ConfigureAwait(false);
            return;
        }

        var id = Uri.UnescapeDataString(segments[2]);
        var action = segments.Length >= 4 ? segments[3] : string.Empty;
        if (request.HttpMethod == "PUT" && string.IsNullOrEmpty(action))
        {
            var updateRequest = await ReadJsonAsync<ProviderProfileUpdateRequest>(request).ConfigureAwait(false);
            _controlPanel.UpdateProviderProfile(id, updateRequest ?? new ProviderProfileUpdateRequest());
            await WriteStateAsync(response).ConfigureAwait(false);
            return;
        }

        if (request.HttpMethod == "DELETE" && string.IsNullOrEmpty(action))
        {
            var deleted = _controlPanel.DeleteProviderProfile(id);
            await WriteJsonAsync(response, new { DeletedCount = deleted ? 1 : 0 }).ConfigureAwait(false);
            return;
        }

        if (request.HttpMethod == "POST" && action == "move-up")
        {
            _controlPanel.MoveProviderProfile(id, -1);
            await WriteStateAsync(response).ConfigureAwait(false);
            return;
        }

        if (request.HttpMethod == "POST" && action == "move-down")
        {
            _controlPanel.MoveProviderProfile(id, 1);
            await WriteStateAsync(response).ConfigureAwait(false);
            return;
        }

        if (request.HttpMethod == "GET" && action == "export")
        {
            response.ContentType = "application/octet-stream; charset=utf-8";
            await WriteTextAsync(response, _controlPanel.ExportProviderProfile(id)).ConfigureAwait(false);
            return;
        }

        if (!_controlPanel.TryGetProviderRuntimeProfile(id, out var profile))
        {
            response.StatusCode = 404;
            await WriteTextAsync(response, "服务商配置不存在。").ConfigureAwait(false);
            return;
        }

        if (profile.Profile.Kind == ProviderKind.LlamaCpp)
        {
            await HandleLlamaCppProviderProfileAsync(context, action, profile).ConfigureAwait(false);
            return;
        }

        var utilityClient = CreateProviderUtilityClient(profile);
        if (request.HttpMethod == "POST" && action == "test")
        {
            var result = await utilityClient.TestConnectionAsync(profile.Profile, CancellationToken.None).ConfigureAwait(false);
            _controlPanel.SetProviderStatus(new ProviderStatus(result.Succeeded ? "ok" : "error", result.Message, DateTimeOffset.UtcNow));
            await WriteJsonAsync(response, result).ConfigureAwait(false);
            return;
        }

        if (request.HttpMethod == "GET" && action == "models")
        {
            await WriteJsonAsync(response, await utilityClient.FetchModelsAsync(profile.Profile, CancellationToken.None).ConfigureAwait(false)).ConfigureAwait(false);
            return;
        }

        if (request.HttpMethod == "GET" && action == "balance")
        {
            await WriteJsonAsync(response, await utilityClient.FetchBalanceAsync(profile.Profile, CancellationToken.None).ConfigureAwait(false)).ConfigureAwait(false);
            return;
        }

        response.StatusCode = 404;
        await WriteTextAsync(response, "未找到服务商配置接口。").ConfigureAwait(false);
    }

    private async Task HandleDraftProviderProfileUtilityAsync(HttpListenerContext context, string action)
    {
        var request = context.Request;
        var response = context.Response;
        if (request.HttpMethod != "POST")
        {
            response.StatusCode = 404;
            await WriteTextAsync(response, "未找到服务商配置接口。").ConfigureAwait(false);
            return;
        }

        var updateRequest = await ReadJsonAsync<ProviderProfileUpdateRequest>(request).ConfigureAwait(false);
        var profile = _controlPanel.CreateDraftProviderRuntimeProfile(updateRequest);
        if (profile.Profile.Kind == ProviderKind.LlamaCpp)
        {
            await HandleDraftLlamaCppProviderProfileAsync(context, action, profile).ConfigureAwait(false);
            return;
        }

        var utilityClient = CreateProviderUtilityClient(profile);
        if (action == "test")
        {
            var result = await utilityClient.TestConnectionAsync(profile.Profile, CancellationToken.None).ConfigureAwait(false);
            _controlPanel.SetProviderStatus(new ProviderStatus(result.Succeeded ? "ok" : "error", result.Message, DateTimeOffset.UtcNow));
            await WriteJsonAsync(response, result).ConfigureAwait(false);
            return;
        }

        if (action == "models")
        {
            await WriteJsonAsync(response, await utilityClient.FetchModelsAsync(profile.Profile, CancellationToken.None).ConfigureAwait(false)).ConfigureAwait(false);
            return;
        }

        if (action == "balance")
        {
            await WriteJsonAsync(response, await utilityClient.FetchBalanceAsync(profile.Profile, CancellationToken.None).ConfigureAwait(false)).ConfigureAwait(false);
            return;
        }

        response.StatusCode = 404;
        await WriteTextAsync(response, "未找到服务商配置接口。").ConfigureAwait(false);
    }

    private async Task HandleDraftLlamaCppProviderProfileAsync(
        HttpListenerContext context,
        string action,
        ProviderRuntimeProfile profile)
    {
        var response = context.Response;
        var config = profile.ApplyTo(_controlPanel.GetConfig());
        if (action == "test")
        {
            if (_llamaCppServer == null)
            {
                await WriteJsonAsync(response, new ProviderTestResult(false, "llama.cpp 本地模型管理器不可用。")).ConfigureAwait(false);
                return;
            }

            var ready = await _llamaCppServer.IsReadyAsync(config, CancellationToken.None).ConfigureAwait(false);
            var status = _llamaCppServer.GetStatus(config);
            _controlPanel.SetLlamaCppStatus(status);
            var result = new ProviderTestResult(
                ready,
                ready ? "llama.cpp 本地模型连接可用。" : status.Message);
            _controlPanel.SetProviderStatus(new ProviderStatus(result.Succeeded ? "ok" : "error", result.Message, DateTimeOffset.UtcNow));
            await WriteJsonAsync(response, result).ConfigureAwait(false);
            return;
        }

        if (action == "models")
        {
            await WriteJsonAsync(
                response,
                new ProviderModelsResult(
                    true,
                    "本地模型使用当前 GGUF 文件。",
                    new[] { new ProviderModelInfo(profile.Profile.Model, "llama.cpp") })).ConfigureAwait(false);
            return;
        }

        if (action == "balance")
        {
            await WriteJsonAsync(
                response,
                new ProviderBalanceResult(true, "本地模型不适用账户余额查询。", Array.Empty<ProviderBalanceInfo>())).ConfigureAwait(false);
            return;
        }

        response.StatusCode = 404;
        await WriteTextAsync(response, "未找到本地模型配置接口。").ConfigureAwait(false);
    }

    private async Task HandleLlamaCppProviderProfileAsync(
        HttpListenerContext context,
        string action,
        ProviderRuntimeProfile profile)
    {
        var request = context.Request;
        var response = context.Response;
        var config = profile.ApplyTo(_controlPanel.GetConfig());
        if (request.HttpMethod == "POST" && action == "test")
        {
            if (_llamaCppServer == null)
            {
                await WriteJsonAsync(response, new ProviderTestResult(false, "llama.cpp 本地模型管理器不可用。")).ConfigureAwait(false);
                return;
            }

            var ready = await _llamaCppServer.IsReadyAsync(config, CancellationToken.None).ConfigureAwait(false);
            var status = _llamaCppServer.GetStatus(config);
            _controlPanel.SetLlamaCppStatus(status);
            var result = new ProviderTestResult(
                ready,
                ready ? "llama.cpp 本地模型连接可用。" : status.Message);
            _controlPanel.SetProviderStatus(new ProviderStatus(result.Succeeded ? "ok" : "error", result.Message, DateTimeOffset.UtcNow));
            await WriteJsonAsync(response, result).ConfigureAwait(false);
            return;
        }

        if (request.HttpMethod == "GET" && action == "models")
        {
            await WriteJsonAsync(
                response,
                new ProviderModelsResult(
                    true,
                    "本地模型使用当前 GGUF 文件。",
                    new[] { new ProviderModelInfo(profile.Profile.Model, "llama.cpp") })).ConfigureAwait(false);
            return;
        }

        if (request.HttpMethod == "GET" && action == "balance")
        {
            await WriteJsonAsync(
                response,
                new ProviderBalanceResult(true, "本地模型不适用账户余额查询。", Array.Empty<ProviderBalanceInfo>())).ConfigureAwait(false);
            return;
        }

        if (request.HttpMethod == "POST" && action == "start")
        {
            var status = _llamaCppServer == null
                ? LlamaCppServerStatus.Error(config.LlamaCpp, string.Empty, "llama.cpp 本地模型管理器不可用。")
                : await _llamaCppServer.StartAsync(config, CancellationToken.None).ConfigureAwait(false);
            _controlPanel.SetLlamaCppStatus(status);
            _controlPanel.SetProviderProfileLlamaCppAutoStartOnStartup(profile.Id, status.State != "error");
            await WriteJsonAsync(response, status).ConfigureAwait(false);
            return;
        }

        if (request.HttpMethod == "POST" && action == "stop")
        {
            var status = _llamaCppServer == null
                ? LlamaCppServerStatus.Stopped(config.LlamaCpp)
                : _llamaCppServer.Stop(config);
            _controlPanel.SetLlamaCppStatus(status);
            _controlPanel.SetProviderProfileLlamaCppAutoStartOnStartup(profile.Id, false);
            await WriteJsonAsync(response, status).ConfigureAwait(false);
            return;
        }

        if (request.HttpMethod == "POST" && action == "benchmark")
        {
            var result = _llamaCppServer == null
                ? LlamaCppBenchmarkResult.Failure(config.LlamaCpp, "llama.cpp 本地模型管理器不可用。")
                : await _llamaCppServer.BenchmarkAsync(config, CancellationToken.None).ConfigureAwait(false);
            if (result.Succeeded && result.RecommendedConfig != null)
            {
                var current = profile.LlamaCpp ?? config.LlamaCpp;
                var savedConfig = result.RecommendedConfig with
                {
                    ModelPath = current.ModelPath,
                    AutoStartOnStartup = current.AutoStartOnStartup
                };
                _controlPanel.UpdateProviderProfile(profile.Id, new ProviderProfileUpdateRequest(LlamaCpp: savedConfig));
                result = result with
                {
                    Saved = true,
                    Message = "基准完成，已自动保存推荐参数。",
                    RecommendedConfig = savedConfig
                };
            }

            await WriteJsonAsync(response, result).ConfigureAwait(false);
            return;
        }

        response.StatusCode = 404;
        await WriteTextAsync(response, "未找到本地模型配置接口。").ConfigureAwait(false);
    }

    private ProviderUtilityClient CreateProviderUtilityClient(ProviderRuntimeProfile profile)
    {
        return new ProviderUtilityClient(_httpClient, () => profile.ApiKey);
    }

    private async Task HandleFontPickAsync(HttpListenerContext context)
    {
        var fontPickRequest = await ReadJsonAsync<FontPickRequest>(context.Request).ConfigureAwait(false);
        var result = WindowsFontFilePicker.PickFontFile();
        if (fontPickRequest?.CopyToConfig == true)
        {
            result = CopyToFontConfigDirectory(result);
        }

        await WriteJsonAsync(context.Response, result).ConfigureAwait(false);
    }

    private FontPickResult CopyToFontConfigDirectory(FontPickResult result)
    {
        return result.CopyToDirectory(Path.Combine(_dataDirectory, "fonts"));
    }

    private ProviderUtilityClient CreateTextureImageProviderUtilityClient(TextureImageProviderProfileDefinition profile)
    {
        var normalized = profile.Normalize();
        return new ProviderUtilityClient(_httpClient, () => normalized.ApiKey);
    }

    private static ProviderProfile CreateTextureImageProviderProfile(TextureImageProviderProfileDefinition profile)
    {
        var normalized = profile.Normalize();
        return new ProviderProfile(
            ProviderKind.OpenAI,
            normalized.BaseUrl,
            normalized.EditEndpoint,
            normalized.ImageModel,
            !string.IsNullOrWhiteSpace(normalized.ApiKey));
    }

    private async Task<ProviderTestResult> TestTextureImageConnectionAsync()
    {
        var active = _controlPanel.GetReadyTextureImageProviderProfiles().FirstOrDefault();
        if (active == null)
        {
            return new ProviderTestResult(false, "请先保存可用的贴图图片服务配置。");
        }

        try
        {
            var normalized = TextureImageProviderProfileDefinition
                .FromLegacy(active.Config, active.ApiKey, priority: 0)
                .Normalize();
            return await new TextureImageEditClient(_httpClient, () => normalized.ApiKey)
                .TestConnectionAsync(normalized.ToConfig(), CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            return new ProviderTestResult(false, $"贴图图片服务连接失败：{ex.Message}");
        }
    }

    private static TranslationCacheQuery ParseTranslationQuery(HttpListenerRequest request)
    {
        var parameters = request.QueryString;
        return new TranslationCacheQuery(
            Search: parameters["search"],
            SortColumn: parameters["sort"] ?? "updated_utc",
            SortDescending: string.Equals(parameters["direction"], "desc", StringComparison.OrdinalIgnoreCase),
            Offset: int.TryParse(parameters["offset"], out var offset) ? offset : 0,
            Limit: int.TryParse(parameters["limit"], out var limit) ? limit : 100,
            ColumnFilters: ParseColumnFilters(parameters));
    }

    private static GlossaryQuery ParseGlossaryQuery(HttpListenerRequest request)
    {
        var parameters = request.QueryString;
        return new GlossaryQuery(
            Search: parameters["search"],
            SortColumn: parameters["sort"] ?? "updated_utc",
            SortDescending: string.Equals(parameters["direction"], "desc", StringComparison.OrdinalIgnoreCase),
            Offset: int.TryParse(parameters["offset"], out var offset) ? offset : 0,
            Limit: int.TryParse(parameters["limit"], out var limit) ? limit : 100,
            ColumnFilters: ParseGlossaryColumnFilters(parameters));
    }

    private static TextureCatalogQuery ParseTextureCatalogQuery(HttpListenerRequest request)
    {
        var parameters = request.QueryString;
        return new TextureCatalogQuery(
            SceneName: parameters["scene"],
            Offset: int.TryParse(parameters["offset"], out var offset) ? Math.Max(0, offset) : 0,
            Limit: int.TryParse(parameters["limit"], out var limit) ? limit : 20,
            TextStatus: parameters["textStatus"]);
    }

    private static string? ExtractTextureImageHash(string path)
    {
        const string prefix = "/api/textures/";
        const string suffix = "/image";
        if (!path.StartsWith(prefix, StringComparison.Ordinal) || !path.EndsWith(suffix, StringComparison.Ordinal))
        {
            return null;
        }

        var encoded = path.Substring(prefix.Length, path.Length - prefix.Length - suffix.Length);
        return Uri.UnescapeDataString(encoded);
    }

    private static TranslationCacheFilterOptionsQuery ParseTranslationFilterOptionsQuery(HttpListenerRequest request)
    {
        var parameters = request.QueryString;
        var column = TranslationCacheColumns.NormalizeColumn(parameters["column"]);
        return new TranslationCacheFilterOptionsQuery(
            Column: column,
            Search: parameters["search"],
            ColumnFilters: ParseColumnFilters(parameters)
                .Where(filter => !string.Equals(filter.Column, column, StringComparison.OrdinalIgnoreCase))
                .ToArray(),
            OptionSearch: parameters["optionSearch"],
            Limit: int.TryParse(parameters["limit"], out var limit) ? limit : 100);
    }

    private static GlossaryFilterOptionsQuery ParseGlossaryFilterOptionsQuery(HttpListenerRequest request)
    {
        var parameters = request.QueryString;
        var column = GlossaryColumns.NormalizeColumn(parameters["column"]);
        return new GlossaryFilterOptionsQuery(
            Column: column,
            Search: parameters["search"],
            ColumnFilters: ParseGlossaryColumnFilters(parameters)
                .Where(filter => !string.Equals(filter.Column, column, StringComparison.OrdinalIgnoreCase))
                .ToArray(),
            OptionSearch: parameters["optionSearch"],
            Limit: int.TryParse(parameters["limit"], out var limit) ? limit : 100);
    }

    private static IReadOnlyList<TranslationCacheColumnFilter> ParseColumnFilters(System.Collections.Specialized.NameValueCollection parameters)
    {
        var filters = new List<TranslationCacheColumnFilter>();
        foreach (var key in parameters.AllKeys)
        {
            if (key == null || !key.StartsWith("filter.", StringComparison.Ordinal))
            {
                continue;
            }

            var column = TranslationCacheColumns.NormalizeColumn(key.Substring("filter.".Length));
            if (column.Length == 0)
            {
                continue;
            }

            var values = parameters.GetValues(key)?
                .Select(value => string.Equals(value, TranslationCacheColumns.EmptyValueMarker, StringComparison.Ordinal)
                    ? null
                    : TranslationCacheColumns.NormalizeFilterValue(value))
                .ToArray() ?? Array.Empty<string?>();
            if (values.Length == 0)
            {
                continue;
            }

            filters.Add(new TranslationCacheColumnFilter(column, values));
        }

        return filters;
    }

    private static IReadOnlyList<GlossaryColumnFilter> ParseGlossaryColumnFilters(System.Collections.Specialized.NameValueCollection parameters)
    {
        var filters = new List<GlossaryColumnFilter>();
        foreach (var key in parameters.AllKeys)
        {
            if (key == null || !key.StartsWith("filter.", StringComparison.Ordinal))
            {
                continue;
            }

            var column = GlossaryColumns.NormalizeColumn(key.Substring("filter.".Length));
            if (column.Length == 0)
            {
                continue;
            }

            var values = parameters.GetValues(key)?
                .Select(value => string.Equals(value, GlossaryColumns.EmptyValueMarker, StringComparison.Ordinal)
                    ? null
                    : GlossaryColumns.NormalizeFilterValue(value))
                .ToArray() ?? Array.Empty<string?>();
            if (values.Length == 0)
            {
                continue;
            }

            filters.Add(new GlossaryColumnFilter(column, values));
        }

        return filters;
    }

    private void DeleteRenamedGlossaryTermIfNeeded(GlossaryTermRequest request, GlossaryTerm savedTerm)
    {
        if (string.IsNullOrWhiteSpace(request.OriginalSourceTerm))
        {
            return;
        }

        var originalLanguage = string.IsNullOrWhiteSpace(request.OriginalTargetLanguage)
            ? savedTerm.TargetLanguage
            : request.OriginalTargetLanguage!;
        var original = GlossaryTerm.CreateManual(
            request.OriginalSourceTerm!,
            savedTerm.TargetTerm,
            originalLanguage,
            request.Note);
        var normalizedOriginal = original.NormalizeForStorage();
        var normalizedSaved = savedTerm.NormalizeForStorage();

        if (!string.Equals(normalizedOriginal.TargetLanguage, normalizedSaved.TargetLanguage, StringComparison.Ordinal) ||
            !string.Equals(normalizedOriginal.NormalizedSourceTerm, normalizedSaved.NormalizedSourceTerm, StringComparison.Ordinal))
        {
            _glossary.Delete(original);
        }
    }

    private RetranslateQueueResult QueueRetranslations(IReadOnlyList<TranslationCacheEntry> entries)
    {
        var queued = 0;
        var preserved = 0;
        var config = _controlPanel.GetConfig();
        foreach (var entry in entries)
        {
            if (string.IsNullOrWhiteSpace(entry.SourceText))
            {
                continue;
            }

            var context = new TranslationCacheContext(entry.SceneName, entry.ComponentHierarchy, entry.ComponentType);
            var targetLanguage = string.IsNullOrWhiteSpace(entry.TargetLanguage)
                ? config.TargetLanguage
                : entry.TargetLanguage;
            if (!TextFilter.ShouldTranslate(entry.SourceText) ||
                TextFilter.IsAlreadyTargetLanguageSource(entry.SourceText, targetLanguage))
            {
                var preservedEntry = entry with
                {
                    TargetLanguage = targetLanguage,
                    ProviderKind = string.Empty,
                    ProviderBaseUrl = string.Empty,
                    ProviderEndpoint = string.Empty,
                    ProviderModel = string.Empty,
                    PromptPolicyVersion = TextPipeline.GetPromptPolicyVersion(config),
                    TranslatedText = entry.SourceText,
                    UpdatedUtc = DateTimeOffset.UtcNow
                };
                var previousTranslatedText = TryGetExistingTranslation(entry, out var existingTranslatedText)
                    ? existingTranslatedText
                    : entry.TranslatedText;
                _cache.Update(preservedEntry);
                PublishManualWriteback(preservedEntry, previousTranslatedText);
                preserved++;
                continue;
            }

            if (CachedEntryFailsCurrentQualityRules(entry, config))
            {
                _cache.Update(entry with
                {
                    ProviderKind = string.Empty,
                    ProviderBaseUrl = string.Empty,
                    ProviderEndpoint = string.Empty,
                    ProviderModel = string.Empty,
                    PromptPolicyVersion = TextPipeline.GetPromptPolicyVersion(config),
                    TranslatedText = null,
                    UpdatedUtc = DateTimeOffset.UtcNow
                });
            }

            if (_queue.Enqueue(TranslationJob.Create(
                "retranslate:" + entry.SourceText,
                entry.SourceText,
                TranslationPriority.Normal,
                context,
                publishResult: false,
                targetLanguage: entry.TargetLanguage)))
            {
                queued++;
            }
        }

        return new RetranslateQueueResult(queued, preserved);
    }

    private static bool CachedEntryFailsCurrentQualityRules(TranslationCacheEntry entry, RuntimeConfig config)
    {
        if (string.IsNullOrWhiteSpace(entry.TranslatedText))
        {
            return false;
        }

        var outputValidation = TranslationOutputValidator.ValidateSingle(
            entry.SourceText,
            entry.TranslatedText!,
            requireSameRichTextTags: true);
        if (!outputValidation.IsValid)
        {
            return true;
        }

        var itemContext = new PromptItemContext(
            0,
            entry.SceneName,
            entry.ComponentHierarchy,
            entry.ComponentType);
        return !TranslationQualityValidator.ValidateBatch(
            new[] { entry.SourceText },
            new[] { entry.TranslatedText! },
            new[] { itemContext },
            string.IsNullOrWhiteSpace(entry.TargetLanguage) ? config.TargetLanguage : entry.TargetLanguage,
            config.GameTitle,
            config.TranslationQuality).IsValid;
    }

    private sealed record RetranslateQueueResult(int QueuedCount, int PreservedCount);

    private bool TryGetExistingTranslation(TranslationCacheEntry entry, out string translatedText)
    {
        var providerKind = Enum.TryParse<ProviderKind>(entry.ProviderKind, ignoreCase: true, out var parsedProviderKind)
            ? parsedProviderKind
            : ProviderKind.OpenAI;
        var key = new TranslationCacheKey(
            entry.SourceText,
            entry.TargetLanguage,
            providerKind,
            entry.ProviderBaseUrl,
            entry.ProviderEndpoint,
            entry.ProviderModel,
            entry.PromptPolicyVersion);
        var context = new TranslationCacheContext(entry.SceneName, entry.ComponentHierarchy, entry.ComponentType);
        return _cache.TryGet(key, context, out translatedText);
    }

    private static TranslationCacheEntry NormalizeManualEntry(TranslationCacheEntry entry)
    {
        return entry with
        {
            TranslatedText = string.IsNullOrWhiteSpace(entry.TranslatedText) ? null : entry.TranslatedText,
            ReplacementFont = string.IsNullOrWhiteSpace(entry.ReplacementFont) ? null : entry.ReplacementFont.Trim()
        };
    }

    private bool PublishManualWriteback(TranslationCacheEntry entry, string? previousTranslatedText)
    {
        if (string.IsNullOrWhiteSpace(entry.SourceText) ||
            string.IsNullOrWhiteSpace(entry.TranslatedText))
        {
            return false;
        }

        var targetId = string.Empty;
        _highlighter?.TryResolveTargetId(TranslationHighlightRequest.FromEntry(entry), out targetId);
        if (string.IsNullOrEmpty(targetId) && string.IsNullOrWhiteSpace(entry.ComponentHierarchy))
        {
            return false;
        }

        _dispatcher.Publish(new TranslationResult(
            targetId,
            entry.SourceText,
            entry.TranslatedText!,
            ManualWritebackPriority,
            previousTranslatedText: previousTranslatedText,
            targetLanguage: entry.TargetLanguage,
            sceneName: entry.SceneName,
            componentHierarchy: entry.ComponentHierarchy,
            componentType: entry.ComponentType,
            updatedUtc: entry.UpdatedUtc,
            restoreSourceText: true));
        return true;
    }

    private bool PublishRestoreSourceWriteback(TranslationCacheEntry entry, string? previousTranslatedText)
    {
        var removedTranslatedText = string.IsNullOrWhiteSpace(previousTranslatedText)
            ? entry.TranslatedText
            : previousTranslatedText;
        if (string.IsNullOrWhiteSpace(entry.SourceText) ||
            string.IsNullOrWhiteSpace(removedTranslatedText))
        {
            return false;
        }

        var targetId = string.Empty;
        _highlighter?.TryResolveTargetId(TranslationHighlightRequest.FromEntry(entry), out targetId);
        if (string.IsNullOrEmpty(targetId) && string.IsNullOrWhiteSpace(entry.ComponentHierarchy))
        {
            return false;
        }

        _dispatcher.Publish(new TranslationResult(
            targetId,
            entry.SourceText,
            entry.SourceText,
            ManualWritebackPriority,
            previousTranslatedText: removedTranslatedText,
            targetLanguage: entry.TargetLanguage,
            sceneName: entry.SceneName,
            componentHierarchy: entry.ComponentHierarchy,
            componentType: entry.ComponentType,
            updatedUtc: entry.UpdatedUtc));
        return true;
    }

    private int PublishUpdatedWritebacks(DateTimeOffset updatedAfterUtc)
    {
        var page = _cache.Query(new TranslationCacheQuery(null, "updated_utc", true, 0, 500));
        var queued = 0;
        foreach (var entry in page.Items.Where(entry => entry.UpdatedUtc > updatedAfterUtc))
        {
            if (PublishManualWriteback(entry, previousTranslatedText: null))
            {
                queued++;
            }
        }

        return queued;
    }

    private static async Task<T?> ReadJsonAsync<T>(HttpListenerRequest request)
    {
        var body = await ReadBodyAsync(request).ConfigureAwait(false);
        return string.IsNullOrWhiteSpace(body) ? default : JsonConvert.DeserializeObject<T>(body);
    }

    private static async Task<string> ReadBodyAsync(HttpListenerRequest request)
    {
        var bytes = await ReadBytesAsync(request, MaxJsonRequestBytes).ConfigureAwait(false);
        return (request.ContentEncoding ?? Encoding.UTF8).GetString(bytes);
    }

    private static async Task<byte[]> ReadBytesAsync(HttpListenerRequest request, long maxBytes)
    {
        EnsureContentLengthWithinLimit(request, maxBytes);
        using var memory = new MemoryStream();
        if (request.InputStream.CanTimeout)
        {
            request.InputStream.ReadTimeout = 15000;
        }

        var chunk = new byte[81920];
        long total = 0;
        int read;
        while ((read = await request.InputStream.ReadAsync(chunk, 0, chunk.Length).ConfigureAwait(false)) > 0)
        {
            total += read;
            if (total > maxBytes)
            {
                throw new InvalidDataException("Request body is too large.");
            }

            memory.Write(chunk, 0, read);
        }

        return memory.ToArray();
    }

    private static void EnsureContentLengthWithinLimit(HttpListenerRequest request, long maxBytes)
    {
        if (request.ContentLength64 > maxBytes)
        {
            throw new InvalidDataException("Request body is too large.");
        }
    }

    private static async Task WriteJsonAsync(HttpListenerResponse response, object value)
    {
        response.ContentType = "application/json; charset=utf-8";
        await WriteTextAsync(response, JsonConvert.SerializeObject(value, Formatting.None)).ConfigureAwait(false);
    }

    private static async Task WriteHtmlAsync(HttpListenerResponse response)
    {
        response.ContentType = "text/html; charset=utf-8";
        await WriteTextAsync(response, ControlPanelHtml.Document).ConfigureAwait(false);
    }

    private static async Task WriteTextAsync(HttpListenerResponse response, string value)
    {
        using var writer = new StreamWriter(response.OutputStream, Utf8NoBom);
        await writer.WriteAsync(value).ConfigureAwait(false);
    }

    private static async Task WriteBytesAsync(HttpListenerResponse response, byte[] value)
    {
        response.ContentLength64 = value.Length;
        try
        {
            await response.OutputStream.WriteAsync(value, 0, value.Length).ConfigureAwait(false);
        }
        finally
        {
            response.OutputStream.Close();
        }
    }

    private string BuildTextureExportFileName()
    {
        var gameTitle = _controlPanel.GetConfig().GameTitle ?? "unknown-game";
        var safeGameTitle = new string(gameTitle
            .Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '-' : ch)
            .ToArray())
            .Trim();
        if (string.IsNullOrWhiteSpace(safeGameTitle))
        {
            safeGameTitle = "unknown-game";
        }

        return $"hunity-textures-{safeGameTitle}-{DateTime.Now:yyyyMMdd-HHmmss}.zip";
    }

    private static bool IsLoopback(HttpListenerContext context)
    {
        var address = context.Request.RemoteEndPoint?.Address;
        return address == null || IPAddress.IsLoopback(address);
    }

    private static string NormalizeLocalHost(string host)
    {
        if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(host, "127.0.0.1", StringComparison.OrdinalIgnoreCase))
        {
            return "127.0.0.1";
        }

        if (string.Equals(host, "::1", StringComparison.OrdinalIgnoreCase))
        {
            return "[::1]";
        }

        return "127.0.0.1";
    }

    private sealed class BoundedReadStream : Stream
    {
        private readonly Stream _inner;
        private readonly long _maxBytes;
        private long _readBytes;

        public BoundedReadStream(Stream inner, long maxBytes)
        {
            _inner = inner;
            _maxBytes = maxBytes;
        }

        public override bool CanRead => _inner.CanRead;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => _readBytes;
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var read = _inner.Read(buffer, offset, count);
            _readBytes += read;
            if (_readBytes > _maxBytes)
            {
                throw new InvalidDataException("Request body is too large.");
            }

            return read;
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotSupportedException();
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _inner.Dispose();
            }

            base.Dispose(disposing);
        }
    }

    private sealed class GlossaryTermRequest
    {
        public string? SourceTerm { get; set; }

        public string? TargetTerm { get; set; }

        public string? TargetLanguage { get; set; }

        public string? OriginalSourceTerm { get; set; }

        public string? OriginalTargetLanguage { get; set; }

        public string? Note { get; set; }

        public bool? Enabled { get; set; }

        public int? UsageCount { get; set; }
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _listener?.Close();
        _httpClient.Dispose();
        _cts?.Dispose();
    }
}

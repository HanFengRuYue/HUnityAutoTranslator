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
using HUnityAutoTranslator.Plugin.Unity;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace HUnityAutoTranslator.Plugin;

internal sealed class LocalHttpServer : IDisposable
{
    private const int ManualWritebackPriority = (int)TranslationPriority.VisibleUi + 100;

    private readonly ControlPanelService _controlPanel;
    private readonly ITranslationCache _cache;
    private readonly IGlossaryStore _glossary;
    private readonly TranslationJobQueue _queue;
    private readonly ResultDispatcher _dispatcher;
    private readonly UnityTextHighlighter? _highlighter;
    private readonly LlamaCppServerManager? _llamaCppServer;
    private readonly LlamaCppModelDownloadManager _llamaCppModelDownloads;
    private readonly HttpClient _httpClient = new();
    private readonly ProviderUtilityClient _providerUtilityClient;
    private readonly ManualLogSource _logger;
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
        LlamaCppServerManager? llamaCppServer,
        LlamaCppModelDownloadManager llamaCppModelDownloads,
        ManualLogSource logger)
    {
        _controlPanel = controlPanel;
        _cache = cache;
        _glossary = glossary;
        _queue = queue;
        _dispatcher = dispatcher;
        _highlighter = highlighter;
        _llamaCppServer = llamaCppServer;
        _llamaCppModelDownloads = llamaCppModelDownloads;
        _providerUtilityClient = new ProviderUtilityClient(_httpClient, _controlPanel.GetApiKey);
        _logger = logger;
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
                _ = Task.Run(() => HandleAsync(context), cancellationToken);
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
            else if (context.Request.HttpMethod == "POST" && path == "/api/fonts/pick")
            {
                await WriteJsonAsync(context.Response, WindowsFontFilePicker.PickFontFile()).ConfigureAwait(false);
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
                _controlPanel.SetLlamaCppStatus(status);
                await WriteJsonAsync(context.Response, status).ConfigureAwait(false);
            }
            else if (context.Request.HttpMethod == "POST" && path == "/api/llamacpp/stop")
            {
                var status = _llamaCppServer == null
                    ? LlamaCppServerStatus.Stopped(_controlPanel.GetConfig().LlamaCpp)
                    : _llamaCppServer.Stop(_controlPanel.GetConfig());
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
                _glossary.UpsertManual(term);
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

                var queued = QueueRetranslations(entries);
                await WriteJsonAsync(context.Response, new { RequestedCount = entries.Count, QueuedCount = queued }).ConfigureAwait(false);
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
            else if (context.Request.HttpMethod == "GET" && path == "/api/provider/models")
            {
                await WriteJsonAsync(
                    context.Response,
                    await _providerUtilityClient.FetchModelsAsync(_controlPanel.GetConfig().Provider, CancellationToken.None).ConfigureAwait(false)).ConfigureAwait(false);
            }
            else if (context.Request.HttpMethod == "GET" && path == "/api/provider/balance")
            {
                await WriteJsonAsync(
                    context.Response,
                    await _providerUtilityClient.FetchBalanceAsync(_controlPanel.GetConfig().Provider, CancellationToken.None).ConfigureAwait(false)).ConfigureAwait(false);
            }
            else if (context.Request.HttpMethod == "POST" && path == "/api/provider/test")
            {
                var config = _controlPanel.GetConfig();
                ProviderModelsResult result;
                if (config.Provider.Kind == ProviderKind.LlamaCpp)
                {
                    var ready = _llamaCppServer != null && await _llamaCppServer.IsReadyAsync(config, CancellationToken.None).ConfigureAwait(false);
                    var status = _llamaCppServer?.GetStatus(config)
                        ?? LlamaCppServerStatus.Error(config.LlamaCpp, string.Empty, "llama.cpp 本地模型管理器不可用。");
                    _controlPanel.SetLlamaCppStatus(status);
                    result = new ProviderModelsResult(
                        ready,
                        ready ? "llama.cpp 本地模型连接可用。" : status.Message,
                        Array.Empty<ProviderModelInfo>());
                }
                else
                {
                    result = await _providerUtilityClient.FetchModelsAsync(config.Provider, CancellationToken.None).ConfigureAwait(false);
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

        await WriteJsonAsync(response, _controlPanel.GetState(_queue.PendingCount, _cache.Count, _dispatcher.PendingCount)).ConfigureAwait(false);
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
            Limit: int.TryParse(parameters["limit"], out var limit) ? limit : 100);
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

    private int QueueRetranslations(IReadOnlyList<TranslationCacheEntry> entries)
    {
        var queued = 0;
        var config = _controlPanel.GetConfig();
        foreach (var entry in entries)
        {
            if (string.IsNullOrWhiteSpace(entry.SourceText))
            {
                continue;
            }

            var context = new TranslationCacheContext(entry.SceneName, entry.ComponentHierarchy, entry.ComponentType);
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

        return queued;
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
            config.GameTitle).IsValid;
    }

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
            TranslatedText = string.IsNullOrWhiteSpace(entry.TranslatedText) ? null : entry.TranslatedText
        };
    }

    private bool PublishManualWriteback(TranslationCacheEntry entry, string? previousTranslatedText)
    {
        if (string.IsNullOrWhiteSpace(entry.SourceText) ||
            string.IsNullOrWhiteSpace(entry.TranslatedText) ||
            string.Equals(entry.TranslatedText, previousTranslatedText, StringComparison.Ordinal))
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
        using var reader = new StreamReader(request.InputStream, request.ContentEncoding ?? Encoding.UTF8);
        return await reader.ReadToEndAsync().ConfigureAwait(false);
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
        using var writer = new StreamWriter(response.OutputStream, Encoding.UTF8);
        await writer.WriteAsync(value).ConfigureAwait(false);
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

    private sealed class GlossaryTermRequest
    {
        public string? SourceTerm { get; set; }

        public string? TargetTerm { get; set; }

        public string? TargetLanguage { get; set; }

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

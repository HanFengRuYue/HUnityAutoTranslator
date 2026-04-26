using System.Net;
using System.Net.Http;
using System.Text;
using BepInEx.Logging;
using HUnityAutoTranslator.Core.Caching;
using HUnityAutoTranslator.Core.Control;
using HUnityAutoTranslator.Core.Dispatching;
using HUnityAutoTranslator.Core.Providers;
using HUnityAutoTranslator.Core.Queueing;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace HUnityAutoTranslator.Plugin;

internal sealed class LocalHttpServer : IDisposable
{
    private readonly ControlPanelService _controlPanel;
    private readonly ITranslationCache _cache;
    private readonly TranslationJobQueue _queue;
    private readonly ResultDispatcher _dispatcher;
    private readonly HttpClient _httpClient = new();
    private readonly ProviderUtilityClient _providerUtilityClient;
    private readonly ManualLogSource _logger;
    private HttpListener? _listener;
    private CancellationTokenSource? _cts;

    public string Url { get; private set; } = string.Empty;

    public LocalHttpServer(
        ControlPanelService controlPanel,
        ITranslationCache cache,
        TranslationJobQueue queue,
        ResultDispatcher dispatcher,
        ManualLogSource logger)
    {
        _controlPanel = controlPanel;
        _cache = cache;
        _queue = queue;
        _dispatcher = dispatcher;
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
                _logger.LogWarning($"HTTP panel listener failed: {ex.Message}");
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
                _logger.LogInfo("Control panel config updated.");
                await WriteStateAsync(context.Response).ConfigureAwait(false);
            }
            else if (context.Request.HttpMethod == "POST" && path == "/api/key")
            {
                var body = await ReadBodyAsync(context.Request).ConfigureAwait(false);
                var apiKey = JObject.Parse(string.IsNullOrWhiteSpace(body) ? "{}" : body).Value<string>("ApiKey");
                _controlPanel.SetApiKey(apiKey ?? string.Empty);
                _logger.LogInfo(string.IsNullOrWhiteSpace(apiKey) ? "Control panel API key cleared." : "Control panel API key configured.");
                await WriteStateAsync(context.Response).ConfigureAwait(false);
            }
            else if (context.Request.HttpMethod == "GET" && path == "/api/translations")
            {
                await WriteJsonAsync(context.Response, _cache.Query(ParseTranslationQuery(context.Request))).ConfigureAwait(false);
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

                _cache.Update(entry);
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

                foreach (var entry in entries)
                {
                    _cache.Delete(entry);
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
                var format = context.Request.QueryString["format"] ?? "json";
                var body = await ReadBodyAsync(context.Request).ConfigureAwait(false);
                await WriteJsonAsync(context.Response, _cache.Import(body, format)).ConfigureAwait(false);
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
                var result = await _providerUtilityClient.FetchModelsAsync(_controlPanel.GetConfig().Provider, CancellationToken.None).ConfigureAwait(false);
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
            _logger.LogWarning($"HTTP panel request failed: {ex.Message}");
            context.Response.StatusCode = 500;
            await WriteTextAsync(context.Response, "请求处理失败。").ConfigureAwait(false);
        }
    }

    private async Task WriteStateAsync(HttpListenerResponse response)
    {
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
            Limit: int.TryParse(parameters["limit"], out var limit) ? limit : 100);
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

    public void Dispose()
    {
        _cts?.Cancel();
        _listener?.Close();
        _httpClient.Dispose();
        _cts?.Dispose();
    }
}

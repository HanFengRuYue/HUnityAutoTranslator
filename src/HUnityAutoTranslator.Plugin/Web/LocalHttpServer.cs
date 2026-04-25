using System.Net;
using System.Text;
using BepInEx.Logging;
using HUnityAutoTranslator.Core.Control;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace HUnityAutoTranslator.Plugin;

internal sealed class LocalHttpServer : IDisposable
{
    private readonly ControlPanelService _controlPanel;
    private readonly Func<int> _queueCountProvider;
    private readonly Func<int> _cacheCountProvider;
    private readonly ManualLogSource _logger;
    private HttpListener? _listener;
    private CancellationTokenSource? _cts;

    public string Url { get; private set; } = string.Empty;

    public LocalHttpServer(
        ControlPanelService controlPanel,
        Func<int> queueCountProvider,
        Func<int> cacheCountProvider,
        ManualLogSource logger)
    {
        _controlPanel = controlPanel;
        _queueCountProvider = queueCountProvider;
        _cacheCountProvider = cacheCountProvider;
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
                await WriteStateAsync(context.Response).ConfigureAwait(false);
            }
            else if (context.Request.HttpMethod == "POST" && path == "/api/key")
            {
                var body = await ReadBodyAsync(context.Request).ConfigureAwait(false);
                var apiKey = JObject.Parse(string.IsNullOrWhiteSpace(body) ? "{}" : body).Value<string>("ApiKey");
                _controlPanel.SetApiKey(apiKey ?? string.Empty);
                await WriteStateAsync(context.Response).ConfigureAwait(false);
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
        await WriteJsonAsync(response, _controlPanel.GetState(_queueCountProvider(), _cacheCountProvider())).ConfigureAwait(false);
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
        _cts?.Dispose();
    }
}

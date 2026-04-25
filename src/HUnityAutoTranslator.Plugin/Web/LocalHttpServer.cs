using System.Net;
using BepInEx.Logging;
using HUnityAutoTranslator.Core.Control;
using Newtonsoft.Json;

namespace HUnityAutoTranslator.Plugin;

internal sealed class LocalHttpServer : IDisposable
{
    private readonly ControlPanelService _controlPanel;
    private readonly ManualLogSource _logger;
    private HttpListener? _listener;
    private CancellationTokenSource? _cts;

    public string Url { get; private set; } = string.Empty;

    public LocalHttpServer(ControlPanelService controlPanel, ManualLogSource logger)
    {
        _controlPanel = controlPanel;
        _logger = logger;
    }

    public void Start(string host, int port)
    {
        var selectedPort = port == 0 ? 48110 : port;
        _listener = new HttpListener();
        Url = $"http://{host}:{selectedPort}/";
        _listener.Prefixes.Add(Url);
        _listener.Start();
        _cts = new CancellationTokenSource();
        _ = Task.Run(() => ServeAsync(_cts.Token));
    }

    private async Task ServeAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested && _listener?.IsListening == true)
        {
            try
            {
                var context = await _listener.GetContextAsync();
                var path = context.Request.Url?.AbsolutePath ?? "/";
                if (path == "/api/state")
                {
                    await WriteJsonAsync(context.Response, _controlPanel.GetState());
                }
                else
                {
                    await WriteHtmlAsync(context.Response);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"HTTP panel request failed: {ex.Message}");
            }
        }
    }

    private static async Task WriteJsonAsync(HttpListenerResponse response, object value)
    {
        response.ContentType = "application/json; charset=utf-8";
        using var writer = new StreamWriter(response.OutputStream);
        await writer.WriteAsync(JsonConvert.SerializeObject(value));
    }

    private static async Task WriteHtmlAsync(HttpListenerResponse response)
    {
        response.ContentType = "text/html; charset=utf-8";
        using var writer = new StreamWriter(response.OutputStream);
        await writer.WriteAsync("<!doctype html><meta charset=\"utf-8\"><title>HUnityAutoTranslator</title><h1>HUnityAutoTranslator</h1><div id=\"app\">控制面板运行中</div>");
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _listener?.Close();
    }
}

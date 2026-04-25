using System.Net.Http;
using BepInEx.Logging;
using HUnityAutoTranslator.Core.Caching;
using HUnityAutoTranslator.Core.Configuration;
using HUnityAutoTranslator.Core.Control;
using HUnityAutoTranslator.Core.Dispatching;
using HUnityAutoTranslator.Core.Providers;
using HUnityAutoTranslator.Core.Queueing;

namespace HUnityAutoTranslator.Plugin;

internal sealed class TranslationWorkerHost : IDisposable
{
    private readonly ControlPanelService _controlPanel;
    private readonly TranslationJobQueue _queue;
    private readonly ResultDispatcher _dispatcher;
    private readonly ITranslationCache _cache;
    private readonly ManualLogSource _logger;
    private readonly HttpClient _httpClient = new();
    private CancellationTokenSource? _cts;
    private Task? _task;

    public TranslationWorkerHost(
        ControlPanelService controlPanel,
        TranslationJobQueue queue,
        ResultDispatcher dispatcher,
        ITranslationCache cache,
        ManualLogSource logger)
    {
        _controlPanel = controlPanel;
        _queue = queue;
        _dispatcher = dispatcher;
        _cache = cache;
        _logger = logger;
    }

    public void Start()
    {
        _cts = new CancellationTokenSource();
        _task = Task.Run(() => RunAsync(_cts.Token));
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var config = _controlPanel.GetConfig();
                if (!config.Enabled || !config.Provider.ApiKeyConfigured)
                {
                    await Task.Delay(250, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                if (_queue.PendingCount == 0)
                {
                    await Task.Delay(40, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                var provider = CreateProvider(config);
                var pool = new TranslationWorkerPool(
                    _queue,
                    _dispatcher,
                    provider,
                    new ProviderRateLimiter(config.RequestsPerMinute),
                    config,
                    _cache);

                await pool.RunUntilIdleAsync(cancellationToken).ConfigureAwait(false);
                _controlPanel.SetLastError(null);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _controlPanel.SetLastError(ex.Message);
                _logger.LogWarning($"Translation worker failed: {ex.Message}");
                await Task.Delay(1000, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private ITranslationProvider CreateProvider(RuntimeConfig config)
    {
        return config.Provider.Kind == ProviderKind.OpenAI
            ? new OpenAiResponsesProvider(_httpClient, config.Provider, _controlPanel.GetApiKey)
            : new ChatCompletionsProvider(_httpClient, config.Provider, _controlPanel.GetApiKey);
    }

    public void Dispose()
    {
        _cts?.Cancel();
        try
        {
            _task?.Wait(TimeSpan.FromSeconds(1));
        }
        catch (AggregateException)
        {
        }

        _httpClient.Dispose();
        _cts?.Dispose();
    }
}

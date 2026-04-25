using HUnityAutoTranslator.Core.Configuration;

namespace HUnityAutoTranslator.Core.Control;

public sealed class ControlPanelService
{
    private readonly object _gate = new();
    private RuntimeConfig _config;
    private string? _apiKey;
    private bool _apiKeyConfigured;
    private string? _lastError;

    private ControlPanelService(RuntimeConfig config)
    {
        _config = config;
    }

    public static ControlPanelService CreateDefault()
    {
        return new ControlPanelService(RuntimeConfig.CreateDefault());
    }

    public ControlPanelState GetState(int queueCount = 0, int cacheCount = 0)
    {
        lock (_gate)
        {
            return new ControlPanelState(
                _config.Enabled,
                _config.TargetLanguage,
                _config.Provider.Kind,
                _config.Provider.BaseUrl,
                _config.Provider.Endpoint,
                _config.Provider.Model,
                _apiKeyConfigured,
                ApiKeyPreview: null,
                QueueCount: queueCount,
                CacheCount: cacheCount,
                _config.MaxConcurrentRequests,
                _config.RequestsPerMinute,
                _config.MaxBatchCharacters,
                (int)_config.ScanInterval.TotalMilliseconds,
                _config.MaxScanTargetsPerTick,
                _config.MaxWritebacksPerFrame,
                _config.EnableUgui,
                _config.EnableTmp,
                _config.EnableImgui,
                _lastError);
        }
    }

    public RuntimeConfig GetConfig()
    {
        lock (_gate)
        {
            return _config;
        }
    }

    public string? GetApiKey()
    {
        lock (_gate)
        {
            return _apiKey;
        }
    }

    public void UpdateConfig(UpdateConfigRequest request)
    {
        lock (_gate)
        {
            var provider = BuildProviderProfile(request);
            var targetLanguage = string.IsNullOrWhiteSpace(request.TargetLanguage)
                ? _config.TargetLanguage
                : request.TargetLanguage.Trim();
            var maxConcurrentRequests = request.MaxConcurrentRequests.HasValue
                ? Clamp(request.MaxConcurrentRequests.Value, 1, 16)
                : _config.MaxConcurrentRequests;
            var requestsPerMinute = request.RequestsPerMinute.HasValue
                ? Clamp(request.RequestsPerMinute.Value, 1, 600)
                : _config.RequestsPerMinute;
            var maxBatchCharacters = request.MaxBatchCharacters.HasValue
                ? Clamp(request.MaxBatchCharacters.Value, 256, 8000)
                : _config.MaxBatchCharacters;
            var scanIntervalMilliseconds = request.ScanIntervalMilliseconds.HasValue
                ? Clamp(request.ScanIntervalMilliseconds.Value, 100, 5000)
                : (int)_config.ScanInterval.TotalMilliseconds;
            var maxScanTargetsPerTick = request.MaxScanTargetsPerTick.HasValue
                ? Clamp(request.MaxScanTargetsPerTick.Value, 1, 4096)
                : _config.MaxScanTargetsPerTick;
            var maxWritebacksPerFrame = request.MaxWritebacksPerFrame.HasValue
                ? Clamp(request.MaxWritebacksPerFrame.Value, 1, 512)
                : _config.MaxWritebacksPerFrame;

            _config = _config with
            {
                Enabled = request.Enabled ?? _config.Enabled,
                TargetLanguage = targetLanguage,
                Style = request.Style ?? _config.Style,
                Provider = provider,
                MaxConcurrentRequests = maxConcurrentRequests,
                RequestsPerMinute = requestsPerMinute,
                MaxBatchCharacters = maxBatchCharacters,
                ScanInterval = TimeSpan.FromMilliseconds(scanIntervalMilliseconds),
                MaxScanTargetsPerTick = maxScanTargetsPerTick,
                MaxWritebacksPerFrame = maxWritebacksPerFrame,
                EnableUgui = request.EnableUgui ?? _config.EnableUgui,
                EnableTmp = request.EnableTmp ?? _config.EnableTmp,
                EnableImgui = request.EnableImgui ?? _config.EnableImgui
            };
        }
    }

    public void SetApiKey(string apiKey)
    {
        lock (_gate)
        {
            _apiKey = string.IsNullOrWhiteSpace(apiKey) ? null : apiKey.Trim();
            _apiKeyConfigured = !string.IsNullOrWhiteSpace(apiKey);
            _config = _config with
            {
                Provider = _config.Provider with { ApiKeyConfigured = _apiKeyConfigured }
            };
        }
    }

    public void SetLastError(string? lastError)
    {
        lock (_gate)
        {
            _lastError = lastError;
        }
    }

    private static int Clamp(int value, int min, int max)
    {
        return Math.Min(max, Math.Max(min, value));
    }

    private ProviderProfile BuildProviderProfile(UpdateConfigRequest request)
    {
        var provider = _config.Provider;
        if (request.ProviderKind.HasValue && request.ProviderKind.Value != provider.Kind)
        {
            provider = request.ProviderKind.Value switch
            {
                ProviderKind.OpenAI => ProviderProfile.DefaultOpenAi(),
                ProviderKind.DeepSeek => ProviderProfile.DefaultDeepSeek(),
                ProviderKind.OpenAICompatible => new ProviderProfile(
                    ProviderKind.OpenAICompatible,
                    "http://127.0.0.1:8000",
                    "/v1/chat/completions",
                    "local-model",
                    _apiKeyConfigured),
                _ => provider
            };
        }

        return provider with
        {
            BaseUrl = string.IsNullOrWhiteSpace(request.BaseUrl) ? provider.BaseUrl : request.BaseUrl.Trim(),
            Endpoint = string.IsNullOrWhiteSpace(request.Endpoint) ? provider.Endpoint : request.Endpoint.Trim(),
            Model = string.IsNullOrWhiteSpace(request.Model) ? provider.Model : request.Model.Trim(),
            ApiKeyConfigured = _apiKeyConfigured
        };
    }
}

using HUnityAutoTranslator.Core.Configuration;

namespace HUnityAutoTranslator.Core.Control;

public sealed class ControlPanelService
{
    private readonly object _gate = new();
    private RuntimeConfig _config;
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

    public ControlPanelState GetState()
    {
        lock (_gate)
        {
            return new ControlPanelState(
                _config.Enabled,
                _config.TargetLanguage,
                _config.Provider.Kind,
                _config.Provider.Model,
                _apiKeyConfigured,
                ApiKeyPreview: null,
                QueueCount: 0,
                CacheCount: 0,
                _config.MaxConcurrentRequests,
                _config.RequestsPerMinute,
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

    public void UpdateConfig(UpdateConfigRequest request)
    {
        lock (_gate)
        {
            var targetLanguage = string.IsNullOrWhiteSpace(request.TargetLanguage)
                ? _config.TargetLanguage
                : request.TargetLanguage.Trim();
            var maxConcurrentRequests = request.MaxConcurrentRequests.HasValue
                ? Clamp(request.MaxConcurrentRequests.Value, 1, 16)
                : _config.MaxConcurrentRequests;
            var requestsPerMinute = request.RequestsPerMinute.HasValue
                ? Clamp(request.RequestsPerMinute.Value, 1, 600)
                : _config.RequestsPerMinute;

            _config = _config with
            {
                TargetLanguage = targetLanguage,
                MaxConcurrentRequests = maxConcurrentRequests,
                RequestsPerMinute = requestsPerMinute
            };
        }
    }

    public void SetApiKey(string apiKey)
    {
        lock (_gate)
        {
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
}

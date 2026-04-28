using HUnityAutoTranslator.Core.Configuration;
using HUnityAutoTranslator.Core.Prompts;

namespace HUnityAutoTranslator.Core.Control;

public sealed class ControlPanelService
{
    private readonly object _gate = new();
    private readonly IControlPanelSettingsStore? _settingsStore;
    private readonly ControlPanelMetrics _metrics;
    private RuntimeConfig _config;
    private string? _apiKey;
    private bool _apiKeyConfigured;
    private string? _lastError;
    private string? _automaticReplacementFontName;
    private string? _automaticReplacementFontFile;
    private string? _automaticGameTitle;
    private LlamaCppServerStatus _llamaCppStatus;
    private ProviderStatus _providerStatus = new("unchecked", "尚未检测", null);

    private ControlPanelService(RuntimeConfig config, IControlPanelSettingsStore? settingsStore, ControlPanelMetrics metrics)
    {
        _config = config;
        _settingsStore = settingsStore;
        _metrics = metrics;
        _llamaCppStatus = LlamaCppServerStatus.Stopped(config.LlamaCpp);
    }

    public static ControlPanelService CreateDefault(ControlPanelMetrics? metrics = null)
    {
        return CreateDefault(settingsStore: null, metrics);
    }

    public static ControlPanelService CreateDefault(IControlPanelSettingsStore? settingsStore, ControlPanelMetrics? metrics = null)
    {
        var service = new ControlPanelService(RuntimeConfig.CreateDefault(), settingsStore, metrics ?? new ControlPanelMetrics());
        if (settingsStore != null)
        {
            service.Load(settingsStore.Load());
        }

        return service;
    }

    public ControlPanelState GetState(int queueCount = 0, int cacheCount = 0, int writebackQueueCount = 0)
    {
        lock (_gate)
        {
            var metrics = _metrics.Snapshot();
            var config = BuildEffectiveConfig(_config);
            return new ControlPanelState(
                config.Enabled,
                config.TargetLanguage,
                _config.GameTitle,
                _automaticGameTitle,
                config.Style,
                config.Provider.Kind,
                config.Provider.BaseUrl,
                config.Provider.Endpoint,
                config.Provider.Model,
                config.Provider.ApiKeyConfigured,
                ApiKeyPreview: null,
                AutoOpenControlPanel: config.AutoOpenControlPanel,
                OpenControlPanelHotkey: config.OpenControlPanelHotkey,
                ToggleTranslationHotkey: config.ToggleTranslationHotkey,
                ForceScanHotkey: config.ForceScanHotkey,
                ToggleFontHotkey: config.ToggleFontHotkey,
                QueueCount: queueCount,
                CacheCount: cacheCount,
                CapturedTextCount: metrics.CapturedTextCount,
                QueuedTextCount: metrics.QueuedTextCount,
                InFlightTranslationCount: metrics.InFlightTranslationCount,
                CompletedTranslationCount: metrics.CompletedTranslationCount,
                TotalTokenCount: metrics.TotalTokenCount,
                AverageTranslationMilliseconds: metrics.AverageTranslationMilliseconds,
                AverageCharactersPerSecond: metrics.AverageCharactersPerSecond,
                WritebackQueueCount: writebackQueueCount,
                ProviderStatus: _providerStatus,
                RecentTranslations: metrics.RecentTranslations,
                config.MaxConcurrentRequests,
                config.EffectiveMaxConcurrentRequests,
                config.RequestsPerMinute,
                config.MaxBatchCharacters,
                (int)config.ScanInterval.TotalMilliseconds,
                config.MaxScanTargetsPerTick,
                config.MaxWritebacksPerFrame,
                config.RequestTimeoutSeconds,
                config.ReasoningEffort,
                config.OutputVerbosity,
                config.DeepSeekThinkingMode,
                config.Temperature,
                config.CustomPrompt,
                PromptBuilder.BuildDefaultSystemPrompt(config.TargetLanguage, config.Style, config.GameTitle),
                config.PromptTemplates,
                PromptTemplateConfig.Default,
                config.MaxSourceTextLength,
                config.IgnoreInvisibleText,
                config.SkipNumericSymbolText,
                config.EnableCacheLookup,
                config.EnableTranslationDebugLogs,
                config.EnableTranslationContext,
                config.TranslationContextMaxExamples,
                config.TranslationContextMaxCharacters,
                config.EnableGlossary,
                config.EnableAutoTermExtraction,
                config.GlossaryMaxTerms,
                config.GlossaryMaxCharacters,
                config.ManualEditsOverrideAi,
                config.ReapplyRememberedTranslations,
                config.CacheRetentionDays,
                config.EnableUgui,
                config.EnableTmp,
                config.EnableImgui,
                config.EnableFontReplacement,
                config.ReplaceUguiFonts,
                config.ReplaceTmpFonts,
                config.ReplaceImguiFonts,
                config.AutoUseCjkFallbackFonts,
                config.ReplacementFontName,
                config.ReplacementFontFile,
                _automaticReplacementFontName,
                _automaticReplacementFontFile,
                config.FontSamplingPointSize,
                config.FontSizeAdjustmentMode,
                config.FontSizeAdjustmentValue,
                _lastError,
                config.LlamaCpp,
                NormalizeLlamaCppStatusForConfig());
        }
    }

    public RuntimeConfig GetConfig()
    {
        lock (_gate)
        {
            return BuildEffectiveConfig(_config);
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
            ApplyConfig(request);
            SaveSettings();
        }
    }

    public void SetApiKey(string apiKey)
    {
        lock (_gate)
        {
            ApplyApiKey(apiKey);
            SaveSettings();
        }
    }

    public void SetLastError(string? lastError)
    {
        lock (_gate)
        {
            _lastError = lastError;
        }
    }

    public void SetProviderStatus(ProviderStatus providerStatus)
    {
        lock (_gate)
        {
            _providerStatus = providerStatus;
        }
    }

    public void SetLlamaCppStatus(LlamaCppServerStatus status)
    {
        lock (_gate)
        {
            _llamaCppStatus = status;
        }
    }

    public void SetAutomaticGameTitle(string? gameTitle)
    {
        lock (_gate)
        {
            _automaticGameTitle = SelectOptionalText(gameTitle, fallback: null);
        }
    }

    public void SetAutomaticFontFallbacks(string? name, string? file)
    {
        lock (_gate)
        {
            _automaticReplacementFontName = SelectOptionalText(name, fallback: null);
            _automaticReplacementFontFile = SelectOptionalText(file, fallback: null);
        }
    }

    private static int Clamp(int value, int min, int max)
    {
        return Math.Min(max, Math.Max(min, value));
    }

    private static double Clamp(double value, double min, double max)
    {
        return Math.Min(max, Math.Max(min, value));
    }

    private static string SelectKnown(string? value, string fallback, params string[] allowed)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        var trimmed = value.Trim();
        return allowed.FirstOrDefault(item => string.Equals(item, trimmed, StringComparison.OrdinalIgnoreCase)) ?? fallback;
    }

    private static string? SelectOptionalText(string? value, string? fallback)
    {
        return value == null
            ? fallback
            : (string.IsNullOrWhiteSpace(value) ? null : value.Trim());
    }

    private static string SelectHotkey(string? value, string fallback)
    {
        return TryNormalizeHotkey(value, out var normalized) ? normalized : fallback;
    }

    private static bool TryNormalizeHotkey(string? value, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var parts = value.Split('+')
            .Select(part => part.Trim())
            .Where(part => part.Length > 0)
            .ToArray();
        if (parts.Length == 1 && string.Equals(parts[0], "None", StringComparison.OrdinalIgnoreCase))
        {
            normalized = "None";
            return true;
        }

        var ctrl = false;
        var shift = false;
        var alt = false;
        string? key = null;
        foreach (var part in parts)
        {
            if (string.Equals(part, "Ctrl", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(part, "Control", StringComparison.OrdinalIgnoreCase))
            {
                if (ctrl)
                {
                    return false;
                }

                ctrl = true;
                continue;
            }

            if (string.Equals(part, "Shift", StringComparison.OrdinalIgnoreCase))
            {
                if (shift)
                {
                    return false;
                }

                shift = true;
                continue;
            }

            if (string.Equals(part, "Alt", StringComparison.OrdinalIgnoreCase))
            {
                if (alt)
                {
                    return false;
                }

                alt = true;
                continue;
            }

            if (key != null || !TryNormalizeHotkeyKey(part, out key))
            {
                return false;
            }
        }

        if (key == null || (!ctrl && !shift && !alt))
        {
            return false;
        }

        var normalizedParts = new List<string>(4);
        if (ctrl)
        {
            normalizedParts.Add("Ctrl");
        }

        if (shift)
        {
            normalizedParts.Add("Shift");
        }

        if (alt)
        {
            normalizedParts.Add("Alt");
        }

        normalizedParts.Add(key);
        normalized = string.Join("+", normalizedParts);
        return true;
    }

    private static bool TryNormalizeHotkeyKey(string value, out string normalized)
    {
        normalized = string.Empty;
        var key = value.Trim();
        if (key.Length == 1)
        {
            var ch = char.ToUpperInvariant(key[0]);
            if ((ch >= 'A' && ch <= 'Z') || (ch >= '0' && ch <= '9'))
            {
                normalized = ch.ToString();
                return true;
            }
        }

        var knownKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "F1", "F2", "F3", "F4", "F5", "F6", "F7", "F8", "F9", "F10", "F11", "F12",
            "Space", "Enter", "Tab", "Backspace", "Escape", "Insert", "Delete",
            "Home", "End", "PageUp", "PageDown", "UpArrow", "DownArrow", "LeftArrow", "RightArrow"
        };
        if (!knownKeys.Contains(key))
        {
            return false;
        }

        normalized = knownKeys.First(item => string.Equals(item, key, StringComparison.OrdinalIgnoreCase));
        return true;
    }

    private void Load(ControlPanelSettings settings)
    {
        lock (_gate)
        {
            var encryptedApiKey = ApiKeyProtector.Unprotect(settings.EncryptedApiKey);
            var legacyApiKey = settings.ApiKey;
            ApplyApiKey(encryptedApiKey ?? legacyApiKey);
            ApplyConfig(settings.Config ?? new UpdateConfigRequest());
            if (!string.IsNullOrWhiteSpace(legacyApiKey))
            {
                SaveSettings();
            }
        }
    }

    private void ApplyConfig(UpdateConfigRequest request)
    {
        var llamaCpp = BuildLlamaCppConfig(request.LlamaCpp);
        var provider = BuildProviderProfile(request, llamaCpp);
        var targetLanguage = string.IsNullOrWhiteSpace(request.TargetLanguage)
            ? _config.TargetLanguage
            : request.TargetLanguage.Trim();
        var gameTitle = request.GameTitle == null
            ? _config.GameTitle
            : SelectOptionalText(request.GameTitle, fallback: null);
        var maxConcurrentRequests = request.MaxConcurrentRequests.HasValue
            ? RuntimeConfigLimits.ClampOnlineConcurrentRequests(request.MaxConcurrentRequests.Value)
            : _config.MaxConcurrentRequests;
        var requestsPerMinute = request.RequestsPerMinute.HasValue
            ? RuntimeConfigLimits.ClampRequestsPerMinute(request.RequestsPerMinute.Value)
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
        var requestTimeoutSeconds = request.RequestTimeoutSeconds.HasValue
            ? Clamp(request.RequestTimeoutSeconds.Value, 5, 180)
            : _config.RequestTimeoutSeconds;
        var httpPort = request.HttpPort.HasValue
            ? Clamp(request.HttpPort.Value, 1, 65535)
            : _config.HttpPort;
        var maxSourceTextLength = request.MaxSourceTextLength.HasValue
            ? Clamp(request.MaxSourceTextLength.Value, 20, 10000)
            : _config.MaxSourceTextLength;
        var cacheRetentionDays = request.CacheRetentionDays.HasValue
            ? Clamp(request.CacheRetentionDays.Value, 1, 3650)
            : _config.CacheRetentionDays;
        var translationContextMaxExamples = request.TranslationContextMaxExamples.HasValue
            ? Clamp(request.TranslationContextMaxExamples.Value, 0, 20)
            : _config.TranslationContextMaxExamples;
        var translationContextMaxCharacters = request.TranslationContextMaxCharacters.HasValue
            ? Clamp(request.TranslationContextMaxCharacters.Value, 0, 8000)
            : _config.TranslationContextMaxCharacters;
        var glossaryMaxTerms = request.GlossaryMaxTerms.HasValue
            ? Clamp(request.GlossaryMaxTerms.Value, 0, 100)
            : _config.GlossaryMaxTerms;
        var glossaryMaxCharacters = request.GlossaryMaxCharacters.HasValue
            ? Clamp(request.GlossaryMaxCharacters.Value, 0, 8000)
            : _config.GlossaryMaxCharacters;
        var fontSamplingPointSize = request.FontSamplingPointSize.HasValue
            ? Clamp(request.FontSamplingPointSize.Value, 16, 180)
            : _config.FontSamplingPointSize;
        var fontSizeAdjustmentValue = request.FontSizeAdjustmentValue.HasValue
            ? FontSizeAdjustment.ClampValue(request.FontSizeAdjustmentValue.Value)
            : _config.FontSizeAdjustmentValue;
        var temperature = request.ClearTemperature == true
            ? null
            : request.Temperature.HasValue
                ? Clamp(request.Temperature.Value, 0.0, 2.0)
                : _config.Temperature;
        var requestedCustomPrompt = request.CustomPrompt == null
            ? _config.CustomPrompt
            : (string.IsNullOrWhiteSpace(request.CustomPrompt) ? null : request.CustomPrompt.Trim());
        var promptTemplates = request.PromptTemplates == null
            ? _config.PromptTemplates
            : request.PromptTemplates.NormalizeAgainstDefaults();
        if (request.CustomPrompt != null && request.PromptTemplates == null)
        {
            promptTemplates = (promptTemplates with { SystemPrompt = requestedCustomPrompt }).NormalizeAgainstDefaults();
        }

        var customPrompt = request.PromptTemplates == null
            ? requestedCustomPrompt
            : promptTemplates.SystemPrompt;

        _config = _config with
        {
            Enabled = request.Enabled ?? _config.Enabled,
            AutoOpenControlPanel = request.AutoOpenControlPanel ?? _config.AutoOpenControlPanel,
            HttpPort = httpPort,
            OpenControlPanelHotkey = SelectHotkey(request.OpenControlPanelHotkey, _config.OpenControlPanelHotkey),
            ToggleTranslationHotkey = SelectHotkey(request.ToggleTranslationHotkey, _config.ToggleTranslationHotkey),
            ForceScanHotkey = SelectHotkey(request.ForceScanHotkey, _config.ForceScanHotkey),
            ToggleFontHotkey = SelectHotkey(request.ToggleFontHotkey, _config.ToggleFontHotkey),
            TargetLanguage = targetLanguage,
            GameTitle = gameTitle,
            Style = request.Style ?? _config.Style,
            Provider = provider,
            MaxConcurrentRequests = maxConcurrentRequests,
            RequestsPerMinute = requestsPerMinute,
            MaxBatchCharacters = maxBatchCharacters,
            ScanInterval = TimeSpan.FromMilliseconds(scanIntervalMilliseconds),
            MaxScanTargetsPerTick = maxScanTargetsPerTick,
            MaxWritebacksPerFrame = maxWritebacksPerFrame,
            RequestTimeoutSeconds = requestTimeoutSeconds,
            ReasoningEffort = SelectKnown(request.ReasoningEffort, _config.ReasoningEffort, "none", "low", "medium", "high", "xhigh", "max"),
            OutputVerbosity = SelectKnown(request.OutputVerbosity, _config.OutputVerbosity, "low", "medium", "high"),
            DeepSeekThinkingMode = SelectKnown(request.DeepSeekThinkingMode, _config.DeepSeekThinkingMode, "enabled", "disabled"),
            Temperature = temperature,
            CustomPrompt = customPrompt,
            PromptTemplates = promptTemplates,
            MaxSourceTextLength = maxSourceTextLength,
            IgnoreInvisibleText = request.IgnoreInvisibleText ?? _config.IgnoreInvisibleText,
            SkipNumericSymbolText = request.SkipNumericSymbolText ?? _config.SkipNumericSymbolText,
            EnableCacheLookup = request.EnableCacheLookup ?? _config.EnableCacheLookup,
            EnableTranslationDebugLogs = request.EnableTranslationDebugLogs ?? _config.EnableTranslationDebugLogs,
            EnableTranslationContext = request.EnableTranslationContext ?? _config.EnableTranslationContext,
            TranslationContextMaxExamples = translationContextMaxExamples,
            TranslationContextMaxCharacters = translationContextMaxCharacters,
            EnableGlossary = request.EnableGlossary ?? _config.EnableGlossary,
            EnableAutoTermExtraction = request.EnableAutoTermExtraction ?? _config.EnableAutoTermExtraction,
            GlossaryMaxTerms = glossaryMaxTerms,
            GlossaryMaxCharacters = glossaryMaxCharacters,
            ManualEditsOverrideAi = request.ManualEditsOverrideAi ?? _config.ManualEditsOverrideAi,
            ReapplyRememberedTranslations = request.ReapplyRememberedTranslations ?? _config.ReapplyRememberedTranslations,
            CacheRetentionDays = cacheRetentionDays,
            EnableUgui = request.EnableUgui ?? _config.EnableUgui,
            EnableTmp = request.EnableTmp ?? _config.EnableTmp,
            EnableImgui = request.EnableImgui ?? _config.EnableImgui,
            EnableFontReplacement = request.EnableFontReplacement ?? _config.EnableFontReplacement,
            ReplaceUguiFonts = request.ReplaceUguiFonts ?? _config.ReplaceUguiFonts,
            ReplaceTmpFonts = request.ReplaceTmpFonts ?? _config.ReplaceTmpFonts,
            ReplaceImguiFonts = request.ReplaceImguiFonts ?? _config.ReplaceImguiFonts,
            AutoUseCjkFallbackFonts = request.AutoUseCjkFallbackFonts ?? _config.AutoUseCjkFallbackFonts,
            ReplacementFontName = SelectOptionalText(request.ReplacementFontName, _config.ReplacementFontName),
            ReplacementFontFile = SelectOptionalText(request.ReplacementFontFile, _config.ReplacementFontFile),
            FontSamplingPointSize = fontSamplingPointSize,
            FontSizeAdjustmentMode = request.FontSizeAdjustmentMode ?? _config.FontSizeAdjustmentMode,
            FontSizeAdjustmentValue = fontSizeAdjustmentValue,
            LlamaCpp = llamaCpp
        };
    }

    private void ApplyApiKey(string? apiKey)
    {
        _apiKey = string.IsNullOrWhiteSpace(apiKey) ? null : apiKey.Trim();
        _apiKeyConfigured = !string.IsNullOrWhiteSpace(_apiKey);
        _config = _config with
        {
            Provider = _config.Provider with { ApiKeyConfigured = IsApiKeyConfiguredForProvider(_config.Provider.Kind) }
        };
    }

    private bool IsApiKeyConfiguredForProvider(ProviderKind providerKind)
    {
        return providerKind == ProviderKind.LlamaCpp || _apiKeyConfigured;
    }

    private void SaveSettings()
    {
        _settingsStore?.Save(new ControlPanelSettings
        {
            Config = new UpdateConfigRequest(
                TargetLanguage: _config.TargetLanguage,
                GameTitle: _config.GameTitle,
                MaxConcurrentRequests: _config.MaxConcurrentRequests,
                RequestsPerMinute: _config.RequestsPerMinute,
                Enabled: _config.Enabled,
                AutoOpenControlPanel: _config.AutoOpenControlPanel,
                HttpPort: _config.HttpPort,
                OpenControlPanelHotkey: _config.OpenControlPanelHotkey,
                ToggleTranslationHotkey: _config.ToggleTranslationHotkey,
                ForceScanHotkey: _config.ForceScanHotkey,
                ToggleFontHotkey: _config.ToggleFontHotkey,
                ProviderKind: _config.Provider.Kind,
                BaseUrl: _config.Provider.BaseUrl,
                Endpoint: _config.Provider.Endpoint,
                Model: _config.Provider.Model,
                Style: _config.Style,
                MaxBatchCharacters: _config.MaxBatchCharacters,
                ScanIntervalMilliseconds: (int)_config.ScanInterval.TotalMilliseconds,
                MaxScanTargetsPerTick: _config.MaxScanTargetsPerTick,
                MaxWritebacksPerFrame: _config.MaxWritebacksPerFrame,
                RequestTimeoutSeconds: _config.RequestTimeoutSeconds,
                ReasoningEffort: _config.ReasoningEffort,
                OutputVerbosity: _config.OutputVerbosity,
                DeepSeekThinkingMode: _config.DeepSeekThinkingMode,
                Temperature: _config.Temperature,
                CustomPrompt: _config.CustomPrompt,
                PromptTemplates: _config.PromptTemplates,
                MaxSourceTextLength: _config.MaxSourceTextLength,
                IgnoreInvisibleText: _config.IgnoreInvisibleText,
                SkipNumericSymbolText: _config.SkipNumericSymbolText,
                EnableCacheLookup: _config.EnableCacheLookup,
                EnableTranslationDebugLogs: _config.EnableTranslationDebugLogs,
                EnableTranslationContext: _config.EnableTranslationContext,
                TranslationContextMaxExamples: _config.TranslationContextMaxExamples,
                TranslationContextMaxCharacters: _config.TranslationContextMaxCharacters,
                EnableGlossary: _config.EnableGlossary,
                EnableAutoTermExtraction: _config.EnableAutoTermExtraction,
                GlossaryMaxTerms: _config.GlossaryMaxTerms,
                GlossaryMaxCharacters: _config.GlossaryMaxCharacters,
                ManualEditsOverrideAi: _config.ManualEditsOverrideAi,
                ReapplyRememberedTranslations: _config.ReapplyRememberedTranslations,
                CacheRetentionDays: _config.CacheRetentionDays,
                EnableUgui: _config.EnableUgui,
                EnableTmp: _config.EnableTmp,
                EnableImgui: _config.EnableImgui,
                EnableFontReplacement: _config.EnableFontReplacement,
                ReplaceUguiFonts: _config.ReplaceUguiFonts,
                ReplaceTmpFonts: _config.ReplaceTmpFonts,
                ReplaceImguiFonts: _config.ReplaceImguiFonts,
                AutoUseCjkFallbackFonts: _config.AutoUseCjkFallbackFonts,
                ReplacementFontName: _config.ReplacementFontName,
                ReplacementFontFile: _config.ReplacementFontFile,
                FontSamplingPointSize: _config.FontSamplingPointSize,
                FontSizeAdjustmentMode: _config.FontSizeAdjustmentMode,
                FontSizeAdjustmentValue: _config.FontSizeAdjustmentValue,
                LlamaCpp: _config.LlamaCpp),
            ApiKey = null,
            EncryptedApiKey = string.IsNullOrWhiteSpace(_apiKey) ? null : ApiKeyProtector.Protect(_apiKey)
        });
    }

    private ProviderProfile BuildProviderProfile(UpdateConfigRequest request, LlamaCppConfig llamaCpp)
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
                ProviderKind.LlamaCpp => ProviderProfile.DefaultLlamaCpp(),
                _ => provider
            };
        }

        if (provider.Kind == ProviderKind.LlamaCpp)
        {
            return provider with
            {
                BaseUrl = "http://127.0.0.1:0",
                Endpoint = "/v1/chat/completions",
                Model = "local-model",
                ApiKeyConfigured = true
            };
        }

        return provider with
        {
            BaseUrl = string.IsNullOrWhiteSpace(request.BaseUrl) ? provider.BaseUrl : request.BaseUrl.Trim(),
            Endpoint = string.IsNullOrWhiteSpace(request.Endpoint) ? provider.Endpoint : request.Endpoint.Trim(),
            Model = string.IsNullOrWhiteSpace(request.Model) ? provider.Model : request.Model.Trim(),
            ApiKeyConfigured = IsApiKeyConfiguredForProvider(provider.Kind)
        };
    }

    private LlamaCppConfig BuildLlamaCppConfig(LlamaCppConfig? request)
    {
        if (request == null)
        {
            return _config.LlamaCpp;
        }

        return new LlamaCppConfig(
            SelectOptionalText(request.ModelPath, fallback: null),
            Clamp(request.ContextSize, 512, 131072),
            Clamp(request.GpuLayers, 0, 999),
            RuntimeConfigLimits.ClampLlamaCppParallelSlots(request.ParallelSlots));
    }

    private RuntimeConfig BuildEffectiveConfig(RuntimeConfig config)
    {
        var effective = config with
        {
            GameTitle = SelectOptionalText(config.GameTitle, _automaticGameTitle)
        };

        if (effective.Provider.Kind != ProviderKind.LlamaCpp)
        {
            return effective;
        }

        var port = _llamaCppStatus.Port;
        return effective with
        {
            Provider = effective.Provider with
            {
                BaseUrl = $"http://127.0.0.1:{port}",
                Endpoint = "/v1/chat/completions",
                ApiKeyConfigured = true
            }
        };
    }

    private LlamaCppServerStatus NormalizeLlamaCppStatusForConfig()
    {
        return _llamaCppStatus with
        {
            ModelPath = _config.LlamaCpp.ModelPath
        };
    }
}

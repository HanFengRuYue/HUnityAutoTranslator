using HUnityAutoTranslator.Core.Configuration;

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
    private ProviderStatus _providerStatus = new("unchecked", "尚未检测", null);

    private ControlPanelService(RuntimeConfig config, IControlPanelSettingsStore? settingsStore, ControlPanelMetrics metrics)
    {
        _config = config;
        _settingsStore = settingsStore;
        _metrics = metrics;
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
            return new ControlPanelState(
                _config.Enabled,
                _config.TargetLanguage,
                _config.Style,
                _config.Provider.Kind,
                _config.Provider.BaseUrl,
                _config.Provider.Endpoint,
                _config.Provider.Model,
                _apiKeyConfigured,
                ApiKeyPreview: null,
                AutoOpenControlPanel: _config.AutoOpenControlPanel,
                OpenControlPanelHotkey: _config.OpenControlPanelHotkey,
                ToggleTranslationHotkey: _config.ToggleTranslationHotkey,
                ForceScanHotkey: _config.ForceScanHotkey,
                ToggleFontHotkey: _config.ToggleFontHotkey,
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
                _config.MaxConcurrentRequests,
                _config.RequestsPerMinute,
                _config.MaxBatchCharacters,
                (int)_config.ScanInterval.TotalMilliseconds,
                _config.MaxScanTargetsPerTick,
                _config.MaxWritebacksPerFrame,
                _config.RequestTimeoutSeconds,
                _config.ReasoningEffort,
                _config.OutputVerbosity,
                _config.DeepSeekThinkingMode,
                _config.Temperature,
                _config.CustomInstruction,
                _config.CustomPrompt,
                _config.MaxSourceTextLength,
                _config.IgnoreInvisibleText,
                _config.SkipNumericSymbolText,
                _config.EnableCacheLookup,
                _config.EnableTranslationContext,
                _config.TranslationContextMaxExamples,
                _config.TranslationContextMaxCharacters,
                _config.EnableGlossary,
                _config.EnableAutoTermExtraction,
                _config.GlossaryMaxTerms,
                _config.GlossaryMaxCharacters,
                _config.ManualEditsOverrideAi,
                _config.ReapplyRememberedTranslations,
                _config.CacheRetentionDays,
                _config.EnableUgui,
                _config.EnableTmp,
                _config.EnableImgui,
                _config.EnableFontReplacement,
                _config.ReplaceUguiFonts,
                _config.ReplaceTmpFonts,
                _config.ReplaceImguiFonts,
                _config.AutoUseCjkFallbackFonts,
                _config.ReplacementFontName,
                _config.ReplacementFontFile,
                _automaticReplacementFontName,
                _automaticReplacementFontFile,
                _config.FontSamplingPointSize,
                _config.FontSizeAdjustmentMode,
                _config.FontSizeAdjustmentValue,
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
        var requestTimeoutSeconds = request.RequestTimeoutSeconds.HasValue
            ? Clamp(request.RequestTimeoutSeconds.Value, 5, 180)
            : _config.RequestTimeoutSeconds;
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
        var customInstruction = request.CustomInstruction == null
            ? _config.CustomInstruction
            : (string.IsNullOrWhiteSpace(request.CustomInstruction) ? null : request.CustomInstruction.Trim());
        var customPrompt = request.CustomPrompt == null
            ? _config.CustomPrompt
            : (string.IsNullOrWhiteSpace(request.CustomPrompt) ? null : request.CustomPrompt.Trim());

        _config = _config with
        {
            Enabled = request.Enabled ?? _config.Enabled,
            AutoOpenControlPanel = request.AutoOpenControlPanel ?? _config.AutoOpenControlPanel,
            OpenControlPanelHotkey = SelectHotkey(request.OpenControlPanelHotkey, _config.OpenControlPanelHotkey),
            ToggleTranslationHotkey = SelectHotkey(request.ToggleTranslationHotkey, _config.ToggleTranslationHotkey),
            ForceScanHotkey = SelectHotkey(request.ForceScanHotkey, _config.ForceScanHotkey),
            ToggleFontHotkey = SelectHotkey(request.ToggleFontHotkey, _config.ToggleFontHotkey),
            TargetLanguage = targetLanguage,
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
            CustomInstruction = customInstruction,
            CustomPrompt = customPrompt,
            MaxSourceTextLength = maxSourceTextLength,
            IgnoreInvisibleText = request.IgnoreInvisibleText ?? _config.IgnoreInvisibleText,
            SkipNumericSymbolText = request.SkipNumericSymbolText ?? _config.SkipNumericSymbolText,
            EnableCacheLookup = request.EnableCacheLookup ?? _config.EnableCacheLookup,
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
            FontSizeAdjustmentValue = fontSizeAdjustmentValue
        };
    }

    private void ApplyApiKey(string? apiKey)
    {
        _apiKey = string.IsNullOrWhiteSpace(apiKey) ? null : apiKey.Trim();
        _apiKeyConfigured = !string.IsNullOrWhiteSpace(_apiKey);
        _config = _config with
        {
            Provider = _config.Provider with { ApiKeyConfigured = _apiKeyConfigured }
        };
    }

    private void SaveSettings()
    {
        _settingsStore?.Save(new ControlPanelSettings
        {
            Config = new UpdateConfigRequest(
                TargetLanguage: _config.TargetLanguage,
                MaxConcurrentRequests: _config.MaxConcurrentRequests,
                RequestsPerMinute: _config.RequestsPerMinute,
                Enabled: _config.Enabled,
                AutoOpenControlPanel: _config.AutoOpenControlPanel,
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
                CustomInstruction: _config.CustomInstruction,
                CustomPrompt: _config.CustomPrompt,
                MaxSourceTextLength: _config.MaxSourceTextLength,
                IgnoreInvisibleText: _config.IgnoreInvisibleText,
                SkipNumericSymbolText: _config.SkipNumericSymbolText,
                EnableCacheLookup: _config.EnableCacheLookup,
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
                FontSizeAdjustmentValue: _config.FontSizeAdjustmentValue),
            ApiKey = null,
            EncryptedApiKey = string.IsNullOrWhiteSpace(_apiKey) ? null : ApiKeyProtector.Protect(_apiKey)
        });
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

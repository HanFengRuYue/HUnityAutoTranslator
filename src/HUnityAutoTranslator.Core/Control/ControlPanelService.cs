using HUnityAutoTranslator.Core.Configuration;
using HUnityAutoTranslator.Core.Prompts;
using HUnityAutoTranslator.Core.Providers;

namespace HUnityAutoTranslator.Core.Control;

public sealed class ControlPanelService
{
    private static readonly TimeSpan ProviderCooldownDuration = TimeSpan.FromMinutes(2);
    private const string DefaultPluginVersion = "0.1.1";
    private const string MissingBepInExVersion = "未检测到";
    private const string ProjectAuthor = "HanFengRuYue";
    private const string ProjectRepositoryUrl = "https://github.com/HanFengRuYue/HUnityAutoTranslator";

    private readonly object _gate = new();
    private readonly IControlPanelSettingsStore? _settingsStore;
    private readonly IProviderProfileStore? _providerProfileStore;
    private readonly ITextureImageProviderProfileStore? _textureImageProviderProfileStore;
    private readonly ControlPanelMetrics _metrics;
    private readonly List<ProviderProfileDefinition> _providerProfiles = new();
    private readonly List<TextureImageProviderProfileDefinition> _textureImageProviderProfiles = new();
    private readonly Dictionary<string, ProviderFailureState> _providerFailures = new(StringComparer.OrdinalIgnoreCase);
    private RuntimeConfig _config;
    private string? _apiKey;
    private bool _apiKeyConfigured;
    private string? _textureImageApiKey;
    private string? _lastError;
    private string? _automaticReplacementFontName;
    private string? _automaticReplacementFontFile;
    private string? _automaticGameTitle;
    private string _pluginVersion = DefaultPluginVersion;
    private string _bepInExVersion = MissingBepInExVersion;
    private LlamaCppServerStatus _llamaCppStatus;
    private ProviderStatus _providerStatus = new("unchecked", "尚未检测", null);

    private ControlPanelService(
        RuntimeConfig config,
        IControlPanelSettingsStore? settingsStore,
        IProviderProfileStore? providerProfileStore,
        ITextureImageProviderProfileStore? textureImageProviderProfileStore,
        ControlPanelMetrics metrics)
    {
        _config = config;
        _settingsStore = settingsStore;
        _providerProfileStore = providerProfileStore;
        _textureImageProviderProfileStore = textureImageProviderProfileStore;
        _metrics = metrics;
        _llamaCppStatus = LlamaCppServerStatus.Stopped(config.LlamaCpp);
    }

    public static ControlPanelService CreateDefault(ControlPanelMetrics? metrics = null)
    {
        return CreateDefault(settingsStore: null, metrics);
    }

    public static ControlPanelService CreateDefault(IControlPanelSettingsStore? settingsStore, ControlPanelMetrics? metrics = null)
    {
        return CreateDefault(settingsStore, providerProfileStore: null, metrics);
    }

    public static ControlPanelService CreateDefault(
        IControlPanelSettingsStore? settingsStore,
        IProviderProfileStore? providerProfileStore,
        ControlPanelMetrics? metrics = null)
    {
        return CreateDefault(settingsStore, providerProfileStore, textureImageProviderProfileStore: null, metrics);
    }

    public static ControlPanelService CreateDefault(
        IControlPanelSettingsStore? settingsStore,
        IProviderProfileStore? providerProfileStore,
        ITextureImageProviderProfileStore? textureImageProviderProfileStore,
        ControlPanelMetrics? metrics = null)
    {
        var service = new ControlPanelService(
            RuntimeConfig.CreateDefault(),
            settingsStore,
            providerProfileStore,
            textureImageProviderProfileStore,
            metrics ?? new ControlPanelMetrics());
        if (settingsStore != null)
        {
            service.Load(settingsStore.Load());
        }

        service.LoadProviderProfiles();
        service.LoadTextureImageProviderProfiles();
        return service;
    }

    public ControlPanelState GetState(
        int queueCount = 0,
        int cacheCount = 0,
        int writebackQueueCount = 0,
        SelfCheckReport? selfCheck = null,
        MemoryDiagnosticsSnapshot? memoryDiagnostics = null)
    {
        lock (_gate)
        {
            var metrics = _metrics.Snapshot();
            var config = BuildEffectiveConfig(_config);
            var activeProfile = ResolveActiveProviderProfile();
            var providerProfiles = BuildProviderProfileStates(activeProfile?.Id);
            var activeTextureProfile = ResolveActiveTextureImageProviderProfile();
            var textureProfiles = BuildTextureImageProviderProfileStates();
            var effectiveMemoryDiagnostics = (memoryDiagnostics ?? MemoryDiagnosticsSnapshot.Empty)
                .WithRuntimeCounts(queueCount, writebackQueueCount, metrics.CapturedKeyTrackerCount);
            return new ControlPanelState(
                config.Enabled,
                config.TargetLanguage,
                _pluginVersion,
                _bepInExVersion,
                ProjectAuthor,
                ProjectRepositoryUrl,
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
                config.OpenAICompatibleCustomHeaders,
                config.OpenAICompatibleExtraBodyJson,
                config.Temperature,
                config.CustomPrompt,
                PromptBuilder.BuildDefaultSystemPrompt(config.TargetLanguage, config.Style, config.GameTitle),
                config.PromptTemplates,
                PromptTemplateConfig.Default,
                config.TranslationQuality,
                config.MaxSourceTextLength,
                config.IgnoreInvisibleText,
                config.SkipNumericSymbolText,
                config.PreTranslateInactiveText,
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
                config.EnableTmpNativeAutoSize,
                _lastError,
                config.TextureImageTranslation,
                activeTextureProfile != null || !string.IsNullOrWhiteSpace(_textureImageApiKey),
                config.LlamaCpp,
                NormalizeLlamaCppStatusForConfig(),
                providerProfiles,
                activeProfile?.Id,
                activeProfile?.Name,
                activeProfile?.Kind,
                activeProfile?.Model,
                metrics.ActiveTranslationProvider,
                textureProfiles,
                activeTextureProfile?.Id,
                activeTextureProfile?.Name,
                activeTextureProfile?.ImageModel,
                selfCheck,
                effectiveMemoryDiagnostics);
        }
    }

    public RuntimeConfig GetConfig()
    {
        lock (_gate)
        {
            return BuildEffectiveConfig(_config);
        }
    }

    public void SetRuntimeVersions(string pluginVersion, string? bepInExVersion)
    {
        lock (_gate)
        {
            _pluginVersion = NormalizeVersionValue(pluginVersion, "未知");
            _bepInExVersion = NormalizeVersionValue(bepInExVersion, MissingBepInExVersion);
        }
    }

    private static string NormalizeVersionValue(string? value, string fallback)
    {
        return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    }

    public string? GetApiKey()
    {
        lock (_gate)
        {
            return ResolveActiveProviderProfile()?.ApiKey ?? _apiKey;
        }
    }

    public string? GetTextureImageApiKey()
    {
        lock (_gate)
        {
            return ResolveActiveTextureImageProviderProfile()?.ApiKey ?? _textureImageApiKey;
        }
    }

    public IReadOnlyList<ProviderRuntimeProfile> GetReadyProviderRuntimeProfiles()
    {
        lock (_gate)
        {
            ClearExpiredProviderCooldowns();
            return _providerProfiles
                .Select(profile => profile.Normalize())
                .Where(profile => profile.Enabled)
                .Where(IsProviderProfileReady)
                .Where(profile => !IsProviderCooling(profile.Id))
                .OrderBy(profile => profile.Priority)
                .ThenBy(profile => profile.Name, StringComparer.OrdinalIgnoreCase)
                .Select(ProviderRuntimeProfile.Create)
                .ToArray();
        }
    }

    public bool HasReadyProviderRuntimeProfile()
    {
        lock (_gate)
        {
            return GetReadyProviderRuntimeProfiles().Count > 0;
        }
    }

    public bool TryGetProviderRuntimeProfile(string id, out ProviderRuntimeProfile profile)
    {
        lock (_gate)
        {
            var definition = _providerProfiles
                .Select(item => item.Normalize())
                .FirstOrDefault(item => string.Equals(item.Id, id, StringComparison.OrdinalIgnoreCase));
            if (definition == null)
            {
                profile = null!;
                return false;
            }

            profile = ProviderRuntimeProfile.Create(definition);
            return true;
        }
    }

    public ProviderRuntimeProfile CreateDraftProviderRuntimeProfile(ProviderProfileUpdateRequest? request)
    {
        lock (_gate)
        {
            var update = request ?? new ProviderProfileUpdateRequest();
            var normalizedId = string.IsNullOrWhiteSpace(update.Id)
                ? null
                : ProviderProfileDefinition.NormalizeId(update.Id);
            var current = normalizedId == null
                ? null
                : _providerProfiles.FirstOrDefault(profile => string.Equals(profile.Id, normalizedId, StringComparison.OrdinalIgnoreCase));
            var kind = update.Kind ?? current?.Kind ?? ProviderKind.OpenAI;
            var priority = current?.Priority ?? (_providerProfiles.Count == 0 ? 0 : _providerProfiles.Max(profile => profile.Priority) + 1);
            var draft = ApplyProviderProfileUpdate(
                current ?? ProviderProfileDefinition.CreateDefault(update.Name, kind, priority),
                update).Normalize();

            return ProviderRuntimeProfile.Create(draft);
        }
    }

    public IReadOnlyList<TextureImageProviderRuntimeProfile> GetReadyTextureImageProviderProfiles()
    {
        lock (_gate)
        {
            return _textureImageProviderProfiles
                .Select(profile => profile.Normalize())
                .Where(profile => profile.Enabled)
                .Where(IsTextureImageProviderProfileReady)
                .OrderBy(profile => profile.Priority)
                .ThenBy(profile => profile.Name, StringComparer.OrdinalIgnoreCase)
                .Select(profile => new TextureImageProviderRuntimeProfile(
                    profile.Id,
                    profile.Name,
                    profile.ToConfig(),
                    profile.ApiKey!))
                .ToArray();
        }
    }

    public bool TryGetTextureImageProviderProfile(string id, out TextureImageProviderProfileDefinition profile)
    {
        lock (_gate)
        {
            var definition = _textureImageProviderProfiles
                .Select(item => item.Normalize())
                .FirstOrDefault(item => string.Equals(item.Id, id, StringComparison.OrdinalIgnoreCase));
            if (definition == null)
            {
                profile = null!;
                return false;
            }

            profile = definition;
            return true;
        }
    }

    public TextureImageProviderProfileState CreateTextureImageProviderProfile(TextureImageProviderProfileUpdateRequest? request)
    {
        lock (_gate)
        {
            var priority = _textureImageProviderProfiles.Count == 0 ? 0 : _textureImageProviderProfiles.Max(profile => profile.Priority) + 1;
            var profile = ApplyTextureImageProviderProfileUpdate(
                TextureImageProviderProfileDefinition.CreateDefault(request?.Name, priority),
                request ?? new TextureImageProviderProfileUpdateRequest())
                .Normalize();
            _textureImageProviderProfiles.Add(profile);
            NormalizeTextureImageProviderPriorities();
            SaveTextureImageProviderProfile(profile);
            return BuildTextureImageProviderProfileState(profile);
        }
    }

    public TextureImageProviderProfileState UpdateTextureImageProviderProfile(string id, TextureImageProviderProfileUpdateRequest request)
    {
        lock (_gate)
        {
            var normalizedId = TextureImageProviderProfileDefinition.NormalizeId(id);
            var index = _textureImageProviderProfiles.FindIndex(profile => string.Equals(profile.Id, normalizedId, StringComparison.OrdinalIgnoreCase));
            if (index < 0)
            {
                throw new InvalidOperationException("Texture image provider profile was not found.");
            }

            var updated = ApplyTextureImageProviderProfileUpdate(_textureImageProviderProfiles[index], request).Normalize();
            _textureImageProviderProfiles[index] = updated;
            NormalizeTextureImageProviderPriorities();
            SaveTextureImageProviderProfile(updated);
            return BuildTextureImageProviderProfileState(updated);
        }
    }

    public bool DeleteTextureImageProviderProfile(string id)
    {
        lock (_gate)
        {
            var normalizedId = TextureImageProviderProfileDefinition.NormalizeId(id);
            var removed = _textureImageProviderProfiles.RemoveAll(profile => string.Equals(profile.Id, normalizedId, StringComparison.OrdinalIgnoreCase)) > 0;
            if (!removed)
            {
                return false;
            }

            _textureImageProviderProfileStore?.Delete(normalizedId);
            NormalizeTextureImageProviderPriorities();
            return true;
        }
    }

    public TextureImageProviderProfileState MoveTextureImageProviderProfile(string id, int direction)
    {
        lock (_gate)
        {
            var ordered = _textureImageProviderProfiles
                .OrderBy(profile => profile.Priority)
                .ThenBy(profile => profile.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
            var normalizedId = TextureImageProviderProfileDefinition.NormalizeId(id);
            var index = ordered.FindIndex(profile => string.Equals(profile.Id, normalizedId, StringComparison.OrdinalIgnoreCase));
            if (index < 0)
            {
                throw new InvalidOperationException("Texture image provider profile was not found.");
            }

            var target = Math.Max(0, Math.Min(ordered.Count - 1, index + direction));
            if (target != index)
            {
                var item = ordered[index];
                ordered.RemoveAt(index);
                ordered.Insert(target, item);
                _textureImageProviderProfiles.Clear();
                for (var i = 0; i < ordered.Count; i++)
                {
                    var updated = ordered[i] with { Priority = i };
                    _textureImageProviderProfiles.Add(updated);
                    SaveTextureImageProviderProfile(updated);
                }
            }

            var moved = _textureImageProviderProfiles.First(profile => string.Equals(profile.Id, normalizedId, StringComparison.OrdinalIgnoreCase));
            return BuildTextureImageProviderProfileState(moved);
        }
    }

    public string ExportTextureImageProviderProfile(string id)
    {
        lock (_gate)
        {
            return _textureImageProviderProfileStore?.Export(TextureImageProviderProfileDefinition.NormalizeId(id))
                ?? throw new InvalidOperationException("Texture image provider profile store is not configured.");
        }
    }

    public TextureImageProviderProfileImportResult ImportTextureImageProviderProfile(string content)
    {
        lock (_gate)
        {
            if (_textureImageProviderProfileStore == null)
            {
                return new TextureImageProviderProfileImportResult(false, "贴图图片服务配置存储不可用。", null);
            }

            try
            {
                var imported = _textureImageProviderProfileStore.Import(
                    content,
                    _textureImageProviderProfiles.Select(profile => profile.Id).ToArray()).Normalize();
                _textureImageProviderProfiles.RemoveAll(profile => string.Equals(profile.Id, imported.Id, StringComparison.OrdinalIgnoreCase));
                _textureImageProviderProfiles.Add(imported);
                NormalizeTextureImageProviderPriorities();
                return new TextureImageProviderProfileImportResult(
                    true,
                    "贴图图片服务配置已导入。",
                    BuildTextureImageProviderProfileState(imported));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or Newtonsoft.Json.JsonException or FormatException or System.Security.Cryptography.CryptographicException)
            {
                return new TextureImageProviderProfileImportResult(false, "导入失败：" + ex.Message, null);
            }
        }
    }

    public ProviderProfileState CreateProviderProfile(ProviderProfileUpdateRequest? request)
    {
        lock (_gate)
        {
            var priority = _providerProfiles.Count == 0 ? 0 : _providerProfiles.Max(profile => profile.Priority) + 1;
            var kind = request?.Kind ?? ProviderKind.OpenAI;
            if (kind == ProviderKind.LlamaCpp && HasLlamaCppProfileExcept(null))
            {
                throw new InvalidOperationException("只能创建一个本地模型配置。");
            }

            var profile = ApplyProviderProfileUpdate(
                ProviderProfileDefinition.CreateDefault(request?.Name, kind, priority),
                request ?? new ProviderProfileUpdateRequest())
                .Normalize();
            if (profile.Kind == ProviderKind.LlamaCpp && HasLlamaCppProfileExcept(profile.Id))
            {
                throw new InvalidOperationException("只能创建一个本地模型配置。");
            }

            _providerProfiles.Add(profile);
            NormalizeProviderPriorities();
            _providerFailures.Remove(profile.Id);
            SaveProviderProfile(profile);
            return BuildProviderProfileState(profile, ResolveActiveProviderProfile()?.Id);
        }
    }

    public ProviderProfileState UpdateProviderProfile(string id, ProviderProfileUpdateRequest request)
    {
        lock (_gate)
        {
            var normalizedId = ProviderProfileDefinition.NormalizeId(id);
            var index = _providerProfiles.FindIndex(profile => string.Equals(profile.Id, normalizedId, StringComparison.OrdinalIgnoreCase));
            if (index < 0)
            {
                throw new InvalidOperationException("Provider profile was not found.");
            }

            var updated = ApplyProviderProfileUpdate(_providerProfiles[index], request).Normalize();
            if (updated.Kind == ProviderKind.LlamaCpp && HasLlamaCppProfileExcept(updated.Id))
            {
                throw new InvalidOperationException("只能创建一个本地模型配置。");
            }

            _providerProfiles[index] = updated;
            NormalizeProviderPriorities();
            _providerFailures.Remove(updated.Id);
            SaveProviderProfile(updated);
            return BuildProviderProfileState(updated, ResolveActiveProviderProfile()?.Id);
        }
    }

    public bool DeleteProviderProfile(string id)
    {
        lock (_gate)
        {
            var normalizedId = ProviderProfileDefinition.NormalizeId(id);
            var removed = _providerProfiles.RemoveAll(profile => string.Equals(profile.Id, normalizedId, StringComparison.OrdinalIgnoreCase)) > 0;
            if (!removed)
            {
                return false;
            }

            _providerFailures.Remove(normalizedId);
            _providerProfileStore?.Delete(normalizedId);
            NormalizeProviderPriorities();
            return true;
        }
    }

    public ProviderProfileState MoveProviderProfile(string id, int direction)
    {
        lock (_gate)
        {
            var ordered = _providerProfiles
                .OrderBy(profile => profile.Priority)
                .ThenBy(profile => profile.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
            var normalizedId = ProviderProfileDefinition.NormalizeId(id);
            var index = ordered.FindIndex(profile => string.Equals(profile.Id, normalizedId, StringComparison.OrdinalIgnoreCase));
            if (index < 0)
            {
                throw new InvalidOperationException("Provider profile was not found.");
            }

            var target = Math.Max(0, Math.Min(ordered.Count - 1, index + direction));
            if (target != index)
            {
                var item = ordered[index];
                ordered.RemoveAt(index);
                ordered.Insert(target, item);
                _providerProfiles.Clear();
                for (var i = 0; i < ordered.Count; i++)
                {
                    var updated = ordered[i] with { Priority = i };
                    _providerProfiles.Add(updated);
                    SaveProviderProfile(updated);
                }
            }

            var moved = _providerProfiles.First(profile => string.Equals(profile.Id, normalizedId, StringComparison.OrdinalIgnoreCase));
            return BuildProviderProfileState(moved, ResolveActiveProviderProfile()?.Id);
        }
    }

    public string ExportProviderProfile(string id)
    {
        lock (_gate)
        {
            return _providerProfileStore?.Export(ProviderProfileDefinition.NormalizeId(id))
                ?? throw new InvalidOperationException("Provider profile store is not configured.");
        }
    }

    public ProviderProfileImportResult ImportProviderProfile(string content)
    {
        lock (_gate)
        {
            if (_providerProfileStore == null)
            {
                return new ProviderProfileImportResult(false, "服务商配置存储不可用。", null);
            }

            try
            {
                var imported = _providerProfileStore.Import(
                    content,
                    _providerProfiles.Select(profile => profile.Id).ToArray()).Normalize();
                if (imported.Kind == ProviderKind.LlamaCpp && HasLlamaCppProfileExcept(imported.Id))
                {
                    _providerProfileStore.Delete(imported.Id);
                    return new ProviderProfileImportResult(false, "只能创建一个本地模型配置。", null);
                }

                _providerProfiles.RemoveAll(profile => string.Equals(profile.Id, imported.Id, StringComparison.OrdinalIgnoreCase));
                _providerProfiles.Add(imported);
                NormalizeProviderPriorities();
                return new ProviderProfileImportResult(
                    true,
                    "服务商配置已导入。",
                    BuildProviderProfileState(imported, ResolveActiveProviderProfile()?.Id));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or Newtonsoft.Json.JsonException or FormatException or System.Security.Cryptography.CryptographicException)
            {
                return new ProviderProfileImportResult(false, "导入失败：" + ex.Message, null);
            }
        }
    }

    public bool RegisterProviderProfileFailure(ProviderRuntimeProfile profile, string errorMessage)
    {
        lock (_gate)
        {
            if (!_providerFailures.TryGetValue(profile.Id, out var state))
            {
                state = new ProviderFailureState();
                _providerFailures[profile.Id] = state;
            }

            state.ConsecutiveFailureCount++;
            state.LastError = errorMessage;
            if (profile.Profile.Kind == ProviderKind.LlamaCpp)
            {
                return false;
            }

            if (state.ConsecutiveFailureCount < 2)
            {
                return false;
            }

            state.CooldownUntilUtc = DateTimeOffset.UtcNow + ProviderCooldownDuration;
            _providerStatus = new ProviderStatus("warning", $"服务商配置“{profile.Name}”连续失败，已切换到下一优先级。", DateTimeOffset.UtcNow);
            return true;
        }
    }

    public void SetProviderProfileLlamaCppAutoStartOnStartup(string id, bool enabled)
    {
        lock (_gate)
        {
            var normalizedId = ProviderProfileDefinition.NormalizeId(id);
            var index = _providerProfiles.FindIndex(profile => string.Equals(profile.Id, normalizedId, StringComparison.OrdinalIgnoreCase));
            if (index < 0)
            {
                throw new InvalidOperationException("Provider profile was not found.");
            }

            var profile = _providerProfiles[index].Normalize();
            if (profile.Kind != ProviderKind.LlamaCpp)
            {
                return;
            }

            var updated = profile with
            {
                LlamaCpp = (profile.LlamaCpp ?? LlamaCppConfig.Default()) with
                {
                    AutoStartOnStartup = enabled
                }
            };
            updated = updated.Normalize();
            _providerProfiles[index] = updated;
            _providerFailures.Remove(updated.Id);
            SaveProviderProfile(updated);
        }
    }

    public void RegisterProviderProfileSuccess(ProviderRuntimeProfile profile)
    {
        lock (_gate)
        {
            _providerFailures.Remove(profile.Id);
            _providerStatus = new ProviderStatus("ok", $"服务商配置“{profile.Name}”连接可用。", DateTimeOffset.UtcNow);
        }
    }

    public void UpdateConfig(UpdateConfigRequest request)
    {
        lock (_gate)
        {
            ApplyConfig(request);
            if (request.TextureImageTranslation != null && _textureImageProviderProfileStore != null)
            {
                SyncTextureImageProviderProfileFromLegacyConfig();
            }

            SaveSettings();
        }
    }

    public void SetApiKey(string apiKey)
    {
        lock (_gate)
        {
            var normalized = SelectOptionalText(apiKey, fallback: null);
            var profile = ResolveActiveProviderProfile() ??
                _providerProfiles.OrderBy(item => item.Priority).FirstOrDefault() ??
                ProviderProfileDefinition.CreateDefault("OpenAI", ProviderKind.OpenAI, _providerProfiles.Count);
            var updated = profile with { ApiKey = normalized };
            _providerProfiles.RemoveAll(item => string.Equals(item.Id, updated.Id, StringComparison.OrdinalIgnoreCase));
            _providerProfiles.Add(updated.Normalize());
            NormalizeProviderPriorities();
            SaveProviderProfile(updated.Normalize());
        }
    }

    public void SetTextureImageApiKey(string apiKey)
    {
        lock (_gate)
        {
            var normalized = SelectOptionalText(apiKey, fallback: null);
            if (_textureImageProviderProfileStore != null)
            {
                var profile = ResolveActiveTextureImageProviderProfile() ??
                    _textureImageProviderProfiles.OrderBy(item => item.Priority).FirstOrDefault() ??
                    TextureImageProviderProfileDefinition.FromLegacy(
                        _config.TextureImageTranslation,
                        apiKey: null,
                        _textureImageProviderProfiles.Count);
                var updated = profile with { ApiKey = normalized };
                _textureImageProviderProfiles.RemoveAll(item => string.Equals(item.Id, updated.Id, StringComparison.OrdinalIgnoreCase));
                _textureImageProviderProfiles.Add(updated.Normalize());
                NormalizeTextureImageProviderPriorities();
                SaveTextureImageProviderProfile(updated.Normalize());
                _textureImageApiKey = null;
            }
            else
            {
                _textureImageApiKey = normalized;
            }

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

    public void SetLlamaCppAutoStartOnStartup(bool enabled)
    {
        lock (_gate)
        {
            _config = _config with
            {
                LlamaCpp = _config.LlamaCpp with { AutoStartOnStartup = enabled }
            };
            SaveSettings();
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

    private void LoadProviderProfiles()
    {
        if (_providerProfileStore == null)
        {
            return;
        }

        lock (_gate)
        {
            _providerProfiles.Clear();
            _providerProfiles.AddRange(_providerProfileStore.LoadAll().Select(profile => profile.Normalize()));
            NormalizeProviderPriorities();
        }
    }

    private void LoadTextureImageProviderProfiles()
    {
        if (_textureImageProviderProfileStore == null)
        {
            return;
        }

        lock (_gate)
        {
            _textureImageProviderProfiles.Clear();
            _textureImageProviderProfiles.AddRange(_textureImageProviderProfileStore.LoadAll().Select(profile => profile.Normalize()));
            NormalizeTextureImageProviderPriorities();

            var hasLegacySecret = !string.IsNullOrWhiteSpace(_textureImageApiKey);
            var hasLegacyConfig = HasNonDefaultTextureImageConfig(_config.TextureImageTranslation);
            if (_textureImageProviderProfiles.Count == 0 && (hasLegacySecret || hasLegacyConfig))
            {
                var migrated = TextureImageProviderProfileDefinition.FromLegacy(
                    _config.TextureImageTranslation,
                    _textureImageApiKey,
                    priority: 0);
                _textureImageProviderProfiles.Add(migrated);
                SaveTextureImageProviderProfile(migrated);
                _textureImageApiKey = null;
                SaveSettings();
            }
        }
    }

    private IReadOnlyList<TextureImageProviderProfileState> BuildTextureImageProviderProfileStates()
    {
        var active = ResolveActiveTextureImageProviderProfile();
        return _textureImageProviderProfiles
            .Select(profile => profile.Normalize())
            .OrderBy(profile => profile.Priority)
            .ThenBy(profile => profile.Name, StringComparer.OrdinalIgnoreCase)
            .Select(profile => BuildTextureImageProviderProfileState(profile, active?.Id))
            .ToArray();
    }

    private TextureImageProviderProfileState BuildTextureImageProviderProfileState(TextureImageProviderProfileDefinition profile)
    {
        return BuildTextureImageProviderProfileState(profile, ResolveActiveTextureImageProviderProfile()?.Id);
    }

    private static TextureImageProviderProfileState BuildTextureImageProviderProfileState(TextureImageProviderProfileDefinition profile, string? activeProfileId)
    {
        var normalized = profile.Normalize();
        _ = activeProfileId;
        return new TextureImageProviderProfileState(
            normalized.Id,
            normalized.Name,
            normalized.Enabled,
            normalized.Priority,
            normalized.BaseUrl,
            normalized.EditEndpoint,
            normalized.VisionEndpoint,
            normalized.ImageModel,
            normalized.VisionModel,
            normalized.Quality,
            normalized.TimeoutSeconds,
            normalized.MaxConcurrentRequests,
            normalized.EnableVisionConfirmation,
            !string.IsNullOrWhiteSpace(normalized.ApiKey),
            ApiKeyPreview: null);
    }

    private TextureImageProviderProfileDefinition? ResolveActiveTextureImageProviderProfile()
    {
        return _textureImageProviderProfiles
            .Select(profile => profile.Normalize())
            .Where(profile => profile.Enabled)
            .Where(IsTextureImageProviderProfileReady)
            .OrderBy(profile => profile.Priority)
            .ThenBy(profile => profile.Name, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }

    private static bool IsTextureImageProviderProfileReady(TextureImageProviderProfileDefinition profile)
    {
        return !string.IsNullOrWhiteSpace(profile.ApiKey);
    }

    private void SaveTextureImageProviderProfile(TextureImageProviderProfileDefinition profile)
    {
        _textureImageProviderProfileStore?.Save(profile);
    }

    private void NormalizeTextureImageProviderPriorities()
    {
        var ordered = _textureImageProviderProfiles
            .Select(profile => profile.Normalize())
            .OrderBy(profile => profile.Priority)
            .ThenBy(profile => profile.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        _textureImageProviderProfiles.Clear();
        for (var i = 0; i < ordered.Length; i++)
        {
            _textureImageProviderProfiles.Add(ordered[i] with { Priority = i });
        }
    }

    private void SyncTextureImageProviderProfileFromLegacyConfig()
    {
        var config = _config.TextureImageTranslation;
        var current = ResolveActiveTextureImageProviderProfile() ??
            _textureImageProviderProfiles.OrderBy(profile => profile.Priority).FirstOrDefault();
        var updated = current == null
            ? TextureImageProviderProfileDefinition.FromLegacy(config, _textureImageApiKey, _textureImageProviderProfiles.Count)
            : current with
            {
                Enabled = config.Enabled,
                BaseUrl = config.BaseUrl,
                EditEndpoint = config.EditEndpoint,
                VisionEndpoint = config.VisionEndpoint,
                ImageModel = config.ImageModel,
                VisionModel = config.VisionModel,
                Quality = config.Quality,
                TimeoutSeconds = config.TimeoutSeconds,
                MaxConcurrentRequests = config.MaxConcurrentRequests,
                EnableVisionConfirmation = config.EnableVisionConfirmation,
                ApiKey = current.ApiKey ?? _textureImageApiKey
            };

        updated = updated.Normalize();
        _textureImageProviderProfiles.RemoveAll(profile => string.Equals(profile.Id, updated.Id, StringComparison.OrdinalIgnoreCase));
        _textureImageProviderProfiles.Add(updated);
        NormalizeTextureImageProviderPriorities();
        SaveTextureImageProviderProfile(updated);
        _textureImageApiKey = null;
    }

    private static TextureImageProviderProfileDefinition ApplyTextureImageProviderProfileUpdate(
        TextureImageProviderProfileDefinition current,
        TextureImageProviderProfileUpdateRequest request)
    {
        var apiKey = request.ClearApiKey == true
            ? null
            : request.ApiKey != null
                ? SelectOptionalText(request.ApiKey, fallback: null)
                : current.ApiKey;

        return current with
        {
            Id = request.Id == null ? current.Id : TextureImageProviderProfileDefinition.NormalizeId(request.Id),
            Name = request.Name == null ? current.Name : SelectOptionalText(request.Name, current.Name) ?? current.Name,
            Enabled = request.Enabled ?? current.Enabled,
            Priority = request.Priority ?? current.Priority,
            BaseUrl = request.BaseUrl == null ? current.BaseUrl : SelectOptionalText(request.BaseUrl, current.BaseUrl) ?? current.BaseUrl,
            EditEndpoint = request.EditEndpoint == null ? current.EditEndpoint : NormalizeEndpoint(request.EditEndpoint, current.EditEndpoint),
            VisionEndpoint = request.VisionEndpoint == null ? current.VisionEndpoint : NormalizeEndpoint(request.VisionEndpoint, current.VisionEndpoint),
            ImageModel = request.ImageModel == null ? current.ImageModel : SelectOptionalText(request.ImageModel, current.ImageModel) ?? current.ImageModel,
            VisionModel = request.VisionModel == null ? current.VisionModel : SelectOptionalText(request.VisionModel, current.VisionModel) ?? current.VisionModel,
            Quality = request.Quality ?? current.Quality,
            TimeoutSeconds = request.TimeoutSeconds ?? current.TimeoutSeconds,
            MaxConcurrentRequests = request.MaxConcurrentRequests ?? current.MaxConcurrentRequests,
            EnableVisionConfirmation = request.EnableVisionConfirmation ?? current.EnableVisionConfirmation,
            ApiKey = apiKey
        };
    }

    private static bool HasNonDefaultTextureImageConfig(TextureImageTranslationConfig config)
    {
        var defaults = TextureImageTranslationConfig.Default();
        return config.Enabled != defaults.Enabled ||
            !string.Equals(config.BaseUrl, defaults.BaseUrl, StringComparison.Ordinal) ||
            !string.Equals(config.EditEndpoint, defaults.EditEndpoint, StringComparison.Ordinal) ||
            !string.Equals(config.VisionEndpoint, defaults.VisionEndpoint, StringComparison.Ordinal) ||
            !string.Equals(config.ImageModel, defaults.ImageModel, StringComparison.Ordinal) ||
            !string.Equals(config.VisionModel, defaults.VisionModel, StringComparison.Ordinal) ||
            !string.Equals(config.Quality, defaults.Quality, StringComparison.OrdinalIgnoreCase) ||
            config.TimeoutSeconds != defaults.TimeoutSeconds ||
            config.MaxConcurrentRequests != defaults.MaxConcurrentRequests ||
            config.EnableVisionConfirmation != defaults.EnableVisionConfirmation;
    }

    private IReadOnlyList<ProviderProfileState> BuildProviderProfileStates(string? activeProfileId)
    {
        ClearExpiredProviderCooldowns();
        return _providerProfiles
            .Select(profile => profile.Normalize())
            .OrderBy(profile => profile.Priority)
            .ThenBy(profile => profile.Name, StringComparer.OrdinalIgnoreCase)
            .Select(profile => BuildProviderProfileState(profile, activeProfileId))
            .ToArray();
    }

    private ProviderProfileState BuildProviderProfileState(ProviderProfileDefinition profile, string? activeProfileId)
    {
        _providerFailures.TryGetValue(profile.Id, out var failure);
        var cooldownRemaining = 0;
        if (failure?.CooldownUntilUtc != null)
        {
            cooldownRemaining = Math.Max(0, (int)Math.Ceiling((failure.CooldownUntilUtc.Value - DateTimeOffset.UtcNow).TotalSeconds));
        }

        return new ProviderProfileState(
            profile.Id,
            profile.Name,
            profile.Enabled,
            profile.Priority,
            profile.Kind,
            profile.BaseUrl,
            profile.Endpoint,
            profile.Model,
            profile.Kind == ProviderKind.LlamaCpp || !string.IsNullOrWhiteSpace(profile.ApiKey),
            ApiKeyPreview: null,
            profile.MaxConcurrentRequests,
            profile.RequestsPerMinute,
            profile.RequestTimeoutSeconds,
            profile.ReasoningEffort,
            profile.OutputVerbosity,
            profile.DeepSeekThinkingMode,
            profile.OpenAICompatibleCustomHeaders,
            profile.OpenAICompatibleExtraBodyJson,
            profile.LlamaCpp,
            profile.Temperature,
            string.Equals(profile.Id, activeProfileId, StringComparison.OrdinalIgnoreCase),
            failure?.ConsecutiveFailureCount ?? 0,
            cooldownRemaining,
            failure?.LastError,
            profile.PresetId);
    }

    private ProviderProfileDefinition? ResolveActiveProviderProfile()
    {
        ClearExpiredProviderCooldowns();
        return _providerProfiles
            .Select(profile => profile.Normalize())
            .Where(profile => profile.Enabled)
            .Where(IsProviderProfileReady)
            .Where(profile => !IsProviderCooling(profile.Id))
            .OrderBy(profile => profile.Priority)
            .ThenBy(profile => profile.Name, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }

    private static bool IsProviderProfileReady(ProviderProfileDefinition profile)
    {
        return profile.Kind == ProviderKind.LlamaCpp
            ? !string.IsNullOrWhiteSpace(profile.LlamaCpp?.ModelPath)
            : profile.Kind == ProviderKind.OpenAICompatible || !string.IsNullOrWhiteSpace(profile.ApiKey);
    }

    private bool HasLlamaCppProfileExcept(string? id)
    {
        return _providerProfiles
            .Select(profile => profile.Normalize())
            .Any(profile =>
                profile.Kind == ProviderKind.LlamaCpp &&
                !string.Equals(profile.Id, id, StringComparison.OrdinalIgnoreCase));
    }

    private bool IsProviderCooling(string id)
    {
        return _providerFailures.TryGetValue(id, out var state) &&
            state.CooldownUntilUtc.HasValue &&
            state.CooldownUntilUtc.Value > DateTimeOffset.UtcNow;
    }

    private void ClearExpiredProviderCooldowns()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var pair in _providerFailures.ToArray())
        {
            if (pair.Value.CooldownUntilUtc.HasValue && pair.Value.CooldownUntilUtc.Value <= now)
            {
                _providerFailures.Remove(pair.Key);
            }
        }
    }

    private void SaveProviderProfile(ProviderProfileDefinition profile)
    {
        _providerProfileStore?.Save(profile);
    }

    private void NormalizeProviderPriorities()
    {
        var ordered = _providerProfiles
            .Select(profile => profile.Normalize())
            .OrderBy(profile => profile.Priority)
            .ThenBy(profile => profile.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        _providerProfiles.Clear();
        for (var i = 0; i < ordered.Length; i++)
        {
            _providerProfiles.Add(ordered[i] with { Priority = i });
        }
    }

    private static ProviderProfileDefinition ApplyProviderProfileUpdate(
        ProviderProfileDefinition current,
        ProviderProfileUpdateRequest request)
    {
        var kind = request.Kind ?? current.Kind;
        var apiKey = request.ClearApiKey == true
            ? null
            : request.ApiKey != null
                ? SelectOptionalText(request.ApiKey, fallback: null)
                : current.ApiKey;
        var temperature = request.ClearTemperature == true
            ? null
            : request.Temperature.HasValue
                ? Math.Min(2.0, Math.Max(0.0, request.Temperature.Value))
                : current.Temperature;
        var customHeaders = request.OpenAICompatibleCustomHeaders == null
            ? current.OpenAICompatibleCustomHeaders
            : OpenAICompatibleRequestOptions.NormalizeCustomHeaders(request.OpenAICompatibleCustomHeaders, current.OpenAICompatibleCustomHeaders);
        var extraBodyJson = request.OpenAICompatibleExtraBodyJson == null
            ? current.OpenAICompatibleExtraBodyJson
            : OpenAICompatibleRequestOptions.NormalizeExtraBodyJson(request.OpenAICompatibleExtraBodyJson, current.OpenAICompatibleExtraBodyJson);
        var presetId = request.ClearPresetId == true
            ? null
            : request.PresetId ?? current.PresetId;

        return current with
        {
            Id = request.Id == null ? current.Id : ProviderProfileDefinition.NormalizeId(request.Id),
            Name = request.Name == null ? current.Name : SelectOptionalText(request.Name, current.Name) ?? current.Name,
            Enabled = request.Enabled ?? current.Enabled,
            Priority = request.Priority ?? current.Priority,
            Kind = ProviderProfileDefinition.IsSupportedProfileKind(kind) ? kind : current.Kind,
            BaseUrl = request.BaseUrl == null ? current.BaseUrl : SelectOptionalText(request.BaseUrl, current.BaseUrl) ?? current.BaseUrl,
            Endpoint = request.Endpoint == null ? current.Endpoint : SelectOptionalText(request.Endpoint, current.Endpoint) ?? current.Endpoint,
            Model = request.Model == null ? current.Model : SelectOptionalText(request.Model, current.Model) ?? current.Model,
            ApiKey = apiKey,
            MaxConcurrentRequests = request.MaxConcurrentRequests ?? current.MaxConcurrentRequests,
            RequestsPerMinute = request.RequestsPerMinute ?? current.RequestsPerMinute,
            RequestTimeoutSeconds = request.RequestTimeoutSeconds ?? current.RequestTimeoutSeconds,
            ReasoningEffort = request.ReasoningEffort ?? current.ReasoningEffort,
            OutputVerbosity = request.OutputVerbosity ?? current.OutputVerbosity,
            DeepSeekThinkingMode = request.DeepSeekThinkingMode ?? current.DeepSeekThinkingMode,
            OpenAICompatibleCustomHeaders = customHeaders,
            OpenAICompatibleExtraBodyJson = extraBodyJson,
            LlamaCpp = request.LlamaCpp ?? current.LlamaCpp,
            Temperature = temperature,
            PresetId = presetId
        };
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

        var parts = value.Split(new[] { '+' })
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
            ApplyConfig(settings.Config ?? new UpdateConfigRequest());
            _textureImageApiKey = ApiKeyProtector.Unprotect(settings.TextureImageEncryptedSecret);
        }
    }

    private void ApplyConfig(UpdateConfigRequest request)
    {
        var llamaCpp = BuildLlamaCppConfig(request.LlamaCpp);
        var openAICompatibleCustomHeaders = OpenAICompatibleRequestOptions.NormalizeCustomHeaders(
            request.OpenAICompatibleCustomHeaders,
            _config.OpenAICompatibleCustomHeaders);
        var openAICompatibleExtraBodyJson = OpenAICompatibleRequestOptions.NormalizeExtraBodyJson(
            request.OpenAICompatibleExtraBodyJson,
            _config.OpenAICompatibleExtraBodyJson);
        var provider = BuildProviderProfile(request, llamaCpp, openAICompatibleCustomHeaders, openAICompatibleExtraBodyJson);
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
        var textureImageTranslation = BuildTextureImageTranslationConfig(request.TextureImageTranslation);
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
        var translationQuality = request.TranslationQuality == null
            ? _config.TranslationQuality
            : request.TranslationQuality.Normalize();
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
            OpenAICompatibleCustomHeaders = openAICompatibleCustomHeaders,
            OpenAICompatibleExtraBodyJson = openAICompatibleExtraBodyJson,
            Temperature = temperature,
            CustomPrompt = customPrompt,
            PromptTemplates = promptTemplates,
            TranslationQuality = translationQuality,
            MaxSourceTextLength = maxSourceTextLength,
            IgnoreInvisibleText = request.IgnoreInvisibleText ?? _config.IgnoreInvisibleText,
            SkipNumericSymbolText = request.SkipNumericSymbolText ?? _config.SkipNumericSymbolText,
            PreTranslateInactiveText = request.PreTranslateInactiveText ?? _config.PreTranslateInactiveText,
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
            EnableTmpNativeAutoSize = request.EnableTmpNativeAutoSize ?? _config.EnableTmpNativeAutoSize,
            TextureImageTranslation = textureImageTranslation,
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
                Style: _config.Style,
                MaxBatchCharacters: _config.MaxBatchCharacters,
                ScanIntervalMilliseconds: (int)_config.ScanInterval.TotalMilliseconds,
                MaxScanTargetsPerTick: _config.MaxScanTargetsPerTick,
                MaxWritebacksPerFrame: _config.MaxWritebacksPerFrame,
                CustomPrompt: _config.CustomPrompt,
                PromptTemplates: _config.PromptTemplates,
                TranslationQuality: _config.TranslationQuality,
                MaxSourceTextLength: _config.MaxSourceTextLength,
                IgnoreInvisibleText: _config.IgnoreInvisibleText,
                SkipNumericSymbolText: _config.SkipNumericSymbolText,
                PreTranslateInactiveText: _config.PreTranslateInactiveText,
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
                EnableTmpNativeAutoSize: _config.EnableTmpNativeAutoSize,
                TextureImageTranslation: _config.TextureImageTranslation,
                LlamaCpp: _config.LlamaCpp)
            ,
            TextureImageEncryptedSecret = null
        });
    }

    private ProviderProfile BuildProviderProfile(
        UpdateConfigRequest request,
        LlamaCppConfig llamaCpp,
        string? openAICompatibleCustomHeaders,
        string? openAICompatibleExtraBodyJson)
    {
        var provider = _config.Provider;
        if (request.ProviderKind == ProviderKind.LlamaCpp)
        {
            provider = ProviderProfile.DefaultLlamaCpp();
        }
        else if (request.ProviderKind.HasValue && request.ProviderKind.Value != ProviderKind.LlamaCpp && provider.Kind == ProviderKind.LlamaCpp)
        {
            provider = ProviderProfile.DefaultOpenAi();
        }

        if (provider.Kind == ProviderKind.LlamaCpp)
        {
            return provider with
            {
                BaseUrl = "http://127.0.0.1:0",
                Endpoint = "/v1/chat/completions",
                Model = "local-model",
                ApiKeyConfigured = true,
                OpenAICompatibleCustomHeaders = null,
                OpenAICompatibleExtraBodyJson = null
            };
        }

        return provider with
        {
            ApiKeyConfigured = false,
            OpenAICompatibleCustomHeaders = null,
            OpenAICompatibleExtraBodyJson = null
        };
    }

    private LlamaCppConfig BuildLlamaCppConfig(LlamaCppConfig? request)
    {
        if (request == null)
        {
            return _config.LlamaCpp;
        }

        var batchSize = RuntimeConfigLimits.ClampLlamaCppBatchSize(request.BatchSize);
        return new LlamaCppConfig(
            SelectOptionalText(request.ModelPath, fallback: null),
            Clamp(request.ContextSize, 512, 131072),
            Clamp(request.GpuLayers, 0, 999),
            RuntimeConfigLimits.ClampLlamaCppParallelSlots(request.ParallelSlots),
            batchSize,
            RuntimeConfigLimits.ClampLlamaCppUBatchSize(request.UBatchSize, batchSize),
            RuntimeConfigLimits.NormalizeLlamaCppFlashAttentionMode(request.FlashAttentionMode),
            request.AutoStartOnStartup,
            RuntimeConfigLimits.ClampLlamaCppCacheReuseTokens(request.CacheReuseTokens));
    }

    private TextureImageTranslationConfig BuildTextureImageTranslationConfig(TextureImageTranslationConfig? request)
    {
        if (request == null)
        {
            return _config.TextureImageTranslation;
        }

        var defaults = TextureImageTranslationConfig.Default();
        var current = _config.TextureImageTranslation ?? defaults;
        var baseUrl = SelectOptionalText(request.BaseUrl, current.BaseUrl) ?? defaults.BaseUrl;
        var editEndpoint = NormalizeEndpoint(request.EditEndpoint, current.EditEndpoint);
        var visionEndpoint = NormalizeEndpoint(request.VisionEndpoint, current.VisionEndpoint);
        var imageModel = SelectOptionalText(request.ImageModel, current.ImageModel) ?? defaults.ImageModel;
        var visionModel = SelectOptionalText(request.VisionModel, current.VisionModel) ?? defaults.VisionModel;
        var quality = SelectKnown(request.Quality, current.Quality, "low", "medium", "high", "auto");
        return new TextureImageTranslationConfig(
            request.Enabled,
            baseUrl.TrimEnd(new[] { '/' }),
            editEndpoint,
            visionEndpoint,
            imageModel,
            visionModel,
            quality,
            Clamp(request.TimeoutSeconds, 30, 300),
            Clamp(request.MaxConcurrentRequests, 1, 4),
            request.EnableVisionConfirmation);
    }

    private static string NormalizeEndpoint(string? value, string fallback)
    {
        var selected = SelectOptionalText(value, fallback) ?? fallback;
        return selected.StartsWith("/", StringComparison.Ordinal)
            ? selected
            : "/" + selected;
    }

    private RuntimeConfig BuildEffectiveConfig(RuntimeConfig config)
    {
        var effective = config with
        {
            GameTitle = SelectOptionalText(config.GameTitle, _automaticGameTitle)
        };

        var activeProfile = ResolveActiveProviderProfile();
        if (activeProfile != null)
        {
            effective = ProviderRuntimeProfile.Create(activeProfile).ApplyTo(effective);
        }
        else if (effective.Provider.Kind != ProviderKind.LlamaCpp)
        {
            effective = effective with { Provider = effective.Provider with { ApiKeyConfigured = false } };
        }

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

    private sealed class ProviderFailureState
    {
        public int ConsecutiveFailureCount { get; set; }

        public DateTimeOffset? CooldownUntilUtc { get; set; }

        public string? LastError { get; set; }
    }
}

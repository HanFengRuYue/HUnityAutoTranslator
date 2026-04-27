using FluentAssertions;
using HUnityAutoTranslator.Core.Configuration;
using HUnityAutoTranslator.Core.Control;

namespace HUnityAutoTranslator.Core.Tests.Control;

public sealed class ControlPanelServiceTests
{
    [Fact]
    public void Snapshot_reports_pipeline_metrics_and_recent_translations()
    {
        var metrics = new ControlPanelMetrics();
        var service = ControlPanelService.CreateDefault(metrics);

        metrics.RecordCaptured();
        metrics.RecordQueued();
        metrics.RecordTranslationStarted();
        metrics.RecordTranslationCompleted(new RecentTranslationPreview(
            SourceText: "Start Game",
            TranslatedText: "Start translated",
            TargetLanguage: "zh-Hans",
            Provider: "OpenAI",
            Model: "gpt-5.5",
            Context: "MainMenu/Button",
            CompletedUtc: DateTimeOffset.Parse("2026-04-26T00:00:00Z")),
            totalTokens: 123,
            elapsed: TimeSpan.FromMilliseconds(250));

        var state = service.GetState(queueCount: 1, cacheCount: 5, writebackQueueCount: 2);

        state.CapturedTextCount.Should().Be(1);
        state.QueuedTextCount.Should().Be(1);
        state.InFlightTranslationCount.Should().Be(0);
        state.CompletedTranslationCount.Should().Be(1);
        state.WritebackQueueCount.Should().Be(2);
        state.TotalTokenCount.Should().Be(123);
        state.AverageTranslationMilliseconds.Should().Be(250);
        state.AverageCharactersPerSecond.Should().BeGreaterThan(0);
        state.RecentTranslations.Should().ContainSingle();
        state.RecentTranslations[0].TranslatedText.Should().Be("Start translated");
    }

    [Fact]
    public void Snapshot_masks_api_key_and_reports_runtime_status()
    {
        var service = ControlPanelService.CreateDefault();
        service.SetApiKey("secret-value");

        var state = service.GetState();

        state.ApiKeyConfigured.Should().BeTrue();
        state.ApiKeyPreview.Should().BeNull();
        service.GetApiKey().Should().Be("secret-value");
        state.TargetLanguage.Should().Be("zh-Hans");
    }

    [Fact]
    public void UpdateConfig_changes_target_language_and_concurrency()
    {
        var service = ControlPanelService.CreateDefault();

        service.UpdateConfig(new UpdateConfigRequest(TargetLanguage: "ja", MaxConcurrentRequests: 8, RequestsPerMinute: 90));

        var state = service.GetState();
        state.TargetLanguage.Should().Be("ja");
        state.MaxConcurrentRequests.Should().Be(8);
        state.RequestsPerMinute.Should().Be(90);
    }

    [Fact]
    public void Snapshot_reports_auto_open_control_panel_enabled_by_default()
    {
        var service = ControlPanelService.CreateDefault();

        var state = service.GetState();

        state.AutoOpenControlPanel.Should().BeTrue();
        service.GetConfig().AutoOpenControlPanel.Should().BeTrue();
    }

    [Fact]
    public void CreateDefault_loads_saved_auto_open_control_panel_setting()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "com.hanfeng.hunityautotranslator.cfg");
        var first = ControlPanelService.CreateDefault(new CfgControlPanelSettingsStore(path));

        first.UpdateConfig(new UpdateConfigRequest(AutoOpenControlPanel: false));

        var second = ControlPanelService.CreateDefault(new CfgControlPanelSettingsStore(path));

        second.GetState().AutoOpenControlPanel.Should().BeFalse();
        second.GetConfig().AutoOpenControlPanel.Should().BeFalse();
    }

    [Fact]
    public void Snapshot_reports_default_runtime_hotkeys()
    {
        var service = ControlPanelService.CreateDefault();

        var state = service.GetState();

        state.OpenControlPanelHotkey.Should().Be("Alt+H");
        state.ToggleTranslationHotkey.Should().Be("Alt+F");
        state.ForceScanHotkey.Should().Be("Alt+G");
        state.ToggleFontHotkey.Should().Be("Alt+D");
    }

    [Fact]
    public void CreateDefault_loads_saved_runtime_hotkeys()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "com.hanfeng.hunityautotranslator.cfg");
        var first = ControlPanelService.CreateDefault(new CfgControlPanelSettingsStore(path));

        first.UpdateConfig(new UpdateConfigRequest(
            OpenControlPanelHotkey: "Ctrl+Shift+P",
            ToggleTranslationHotkey: "Alt+T",
            ForceScanHotkey: "Ctrl+G",
            ToggleFontHotkey: "Shift+F8"));

        var second = ControlPanelService.CreateDefault(new CfgControlPanelSettingsStore(path));
        var state = second.GetState();

        state.OpenControlPanelHotkey.Should().Be("Ctrl+Shift+P");
        state.ToggleTranslationHotkey.Should().Be("Alt+T");
        state.ForceScanHotkey.Should().Be("Ctrl+G");
        state.ToggleFontHotkey.Should().Be("Shift+F8");
    }

    [Fact]
    public void UpdateConfig_keeps_previous_runtime_hotkeys_when_values_are_empty_or_invalid()
    {
        var service = ControlPanelService.CreateDefault();

        service.UpdateConfig(new UpdateConfigRequest(
            OpenControlPanelHotkey: "Ctrl+Shift+P",
            ToggleTranslationHotkey: "Alt+T",
            ForceScanHotkey: "Ctrl+G",
            ToggleFontHotkey: "Shift+F8"));

        service.UpdateConfig(new UpdateConfigRequest(
            OpenControlPanelHotkey: "",
            ToggleTranslationHotkey: "Alt+F+G",
            ForceScanHotkey: "Ctrl+",
            ToggleFontHotkey: "Mouse4"));

        var state = service.GetState();
        state.OpenControlPanelHotkey.Should().Be("Ctrl+Shift+P");
        state.ToggleTranslationHotkey.Should().Be("Alt+T");
        state.ForceScanHotkey.Should().Be("Ctrl+G");
        state.ToggleFontHotkey.Should().Be("Shift+F8");
    }

    [Fact]
    public void UpdateConfig_changes_provider_profile_and_runtime_switches()
    {
        var service = ControlPanelService.CreateDefault();

        service.UpdateConfig(new UpdateConfigRequest(
            TargetLanguage: "ko",
            MaxConcurrentRequests: 150,
            RequestsPerMinute: 700,
            Enabled: false,
            ProviderKind: ProviderKind.OpenAICompatible,
            BaseUrl: "http://127.0.0.1:9000",
            Endpoint: "/v1/chat/completions",
            Model: "local-model",
            EnableUgui: false,
            EnableTmp: false,
            EnableImgui: true,
            MaxScanTargetsPerTick: 12,
            MaxWritebacksPerFrame: 64));

        var state = service.GetState(queueCount: 5, cacheCount: 9);
        var config = service.GetConfig();

        state.Enabled.Should().BeFalse();
        state.ProviderKind.Should().Be(ProviderKind.OpenAICompatible);
        state.BaseUrl.Should().Be("http://127.0.0.1:9000");
        state.Endpoint.Should().Be("/v1/chat/completions");
        state.Model.Should().Be("local-model");
        state.QueueCount.Should().Be(5);
        state.CacheCount.Should().Be(9);
        state.MaxConcurrentRequests.Should().Be(100);
        state.EffectiveMaxConcurrentRequests.Should().Be(100);
        state.RequestsPerMinute.Should().Be(600);
        config.EnableUgui.Should().BeFalse();
        config.EnableTmp.Should().BeFalse();
        config.EnableImgui.Should().BeTrue();
        config.MaxScanTargetsPerTick.Should().Be(12);
        config.MaxWritebacksPerFrame.Should().Be(64);
    }

    [Fact]
    public void UpdateConfig_changes_to_llamacpp_without_requiring_api_key()
    {
        var service = ControlPanelService.CreateDefault();

        service.UpdateConfig(new UpdateConfigRequest(
            ProviderKind: ProviderKind.LlamaCpp,
            Model: "qwen-game-ui",
            LlamaCpp: new LlamaCppConfig(
                ModelPath: @"D:\Models\game-ui.gguf",
                ContextSize: 8192,
                GpuLayers: 80,
                ParallelSlots: 2)));

        var state = service.GetState();
        var config = service.GetConfig();

        state.ProviderKind.Should().Be(ProviderKind.LlamaCpp);
        state.ApiKeyConfigured.Should().BeTrue();
        state.BaseUrl.Should().Be("http://127.0.0.1:0");
        state.Endpoint.Should().Be("/v1/chat/completions");
        state.Model.Should().Be("qwen-game-ui");
        state.LlamaCpp.ModelPath.Should().Be(@"D:\Models\game-ui.gguf");
        state.LlamaCpp.ContextSize.Should().Be(8192);
        state.LlamaCpp.GpuLayers.Should().Be(80);
        state.LlamaCpp.ParallelSlots.Should().Be(2);
        state.EffectiveMaxConcurrentRequests.Should().Be(2);
        state.LlamaCppStatus.State.Should().Be("stopped");
        state.LlamaCppStatus.Port.Should().Be(0);
        state.LlamaCppStatus.Installed.Should().BeFalse();
        config.Provider.ApiKeyConfigured.Should().BeTrue();
    }

    [Fact]
    public void UpdateConfig_clamps_llamacpp_settings_to_safe_ranges()
    {
        var service = ControlPanelService.CreateDefault();

        service.UpdateConfig(new UpdateConfigRequest(
            ProviderKind: ProviderKind.LlamaCpp,
            LlamaCpp: new LlamaCppConfig(
                ModelPath: "  ",
                ContextSize: 64,
                GpuLayers: -4,
                ParallelSlots: 99)));

        var state = service.GetState();

        state.LlamaCpp.ModelPath.Should().BeNull();
        state.LlamaCpp.ContextSize.Should().Be(512);
        state.LlamaCpp.GpuLayers.Should().Be(0);
        state.LlamaCpp.ParallelSlots.Should().Be(16);
    }

    [Fact]
    public void SetLlamaCppStatus_reports_runtime_port_without_persisting_it_to_config()
    {
        var service = ControlPanelService.CreateDefault();
        service.UpdateConfig(new UpdateConfigRequest(ProviderKind: ProviderKind.LlamaCpp));

        service.SetLlamaCppStatus(LlamaCppServerStatus.Running(
            service.GetConfig().LlamaCpp,
            backend: "Vulkan",
            port: 51234,
            release: "b8943",
            variant: "Vulkan",
            serverPath: @"D:\Game\BepInEx\plugins\HUnityAutoTranslator\llama.cpp\llama-server.exe"));

        var state = service.GetState();

        state.LlamaCppStatus.Port.Should().Be(51234);
        state.LlamaCppStatus.Installed.Should().BeTrue();
        state.LlamaCppStatus.Release.Should().Be("b8943");
        state.LlamaCppStatus.Variant.Should().Be("Vulkan");
        service.GetConfig().Provider.BaseUrl.Should().Be("http://127.0.0.1:51234");
    }

    [Fact]
    public void CreateDefault_does_not_persist_llamacpp_runtime_port()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "com.hanfeng.hunityautotranslator.cfg");
        var first = ControlPanelService.CreateDefault(new CfgControlPanelSettingsStore(path));
        first.UpdateConfig(new UpdateConfigRequest(ProviderKind: ProviderKind.LlamaCpp));
        first.SetLlamaCppStatus(LlamaCppServerStatus.Running(
            first.GetConfig().LlamaCpp,
            backend: "Vulkan",
            port: 51234,
            release: "b8943",
            variant: "Vulkan",
            serverPath: @"D:\Game\BepInEx\plugins\HUnityAutoTranslator\llama.cpp\llama-server.exe"));

        first.UpdateConfig(new UpdateConfigRequest(Model: "qwen-game-ui"));
        var second = ControlPanelService.CreateDefault(new CfgControlPanelSettingsStore(path));

        second.GetState().LlamaCppStatus.Port.Should().Be(0);
        second.GetConfig().Provider.BaseUrl.Should().Be("http://127.0.0.1:0");
    }

    [Fact]
    public void CreateDefault_loads_saved_config_and_api_key_from_cfg_store()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "com.hanfeng.hunityautotranslator.cfg");
        var first = ControlPanelService.CreateDefault(new CfgControlPanelSettingsStore(path));

        first.UpdateConfig(new UpdateConfigRequest(
            TargetLanguage: "ja",
            MaxConcurrentRequests: 7,
            RequestsPerMinute: 123,
            Enabled: false,
            ProviderKind: ProviderKind.OpenAICompatible,
            BaseUrl: "http://127.0.0.1:9000",
            Endpoint: "/v1/chat/completions",
            Model: "local-model",
            EnableUgui: false,
            EnableTmp: true,
            EnableImgui: false,
            MaxScanTargetsPerTick: 33,
            MaxWritebacksPerFrame: 44));
        first.SetApiKey("secret-value");

        var second = ControlPanelService.CreateDefault(new CfgControlPanelSettingsStore(path));

        var state = second.GetState();
        state.Enabled.Should().BeFalse();
        state.TargetLanguage.Should().Be("ja");
        state.ProviderKind.Should().Be(ProviderKind.OpenAICompatible);
        state.BaseUrl.Should().Be("http://127.0.0.1:9000");
        state.Endpoint.Should().Be("/v1/chat/completions");
        state.Model.Should().Be("local-model");
        state.ApiKeyConfigured.Should().BeTrue();
        second.GetApiKey().Should().Be("secret-value");
        state.MaxConcurrentRequests.Should().Be(7);
        state.RequestsPerMinute.Should().Be(123);
        state.EnableUgui.Should().BeFalse();
        state.EnableTmp.Should().BeTrue();
        state.EnableImgui.Should().BeFalse();
        state.MaxScanTargetsPerTick.Should().Be(33);
        state.MaxWritebacksPerFrame.Should().Be(44);
    }

    [Fact]
    public void Cfg_store_persists_api_key_encrypted()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "com.hanfeng.hunityautotranslator.cfg");
        var first = ControlPanelService.CreateDefault(new CfgControlPanelSettingsStore(path));

        first.SetApiKey("secret-value");

        var cfg = File.ReadAllText(path);
        cfg.Should().Contain("EncryptedApiKey");
        cfg.Should().NotContain("secret-value");

        var second = ControlPanelService.CreateDefault(new CfgControlPanelSettingsStore(path));
        second.GetApiKey().Should().Be("secret-value");
        second.GetState().ApiKeyConfigured.Should().BeTrue();
    }

    [Fact]
    public void Cfg_store_encrypts_plaintext_api_key_on_load()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "com.hanfeng.hunityautotranslator.cfg");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, """
            [基础]
            TargetLanguage = ja

            [翻译服务]
            ApiKey = legacy-secret
            EncryptedApiKey =
            """);

        var service = ControlPanelService.CreateDefault(new CfgControlPanelSettingsStore(path));
        service.GetApiKey().Should().Be("legacy-secret");
        service.GetState().TargetLanguage.Should().Be("ja");

        var cfg = File.ReadAllText(path);
        cfg.Should().Contain("EncryptedApiKey");
        cfg.Should().NotContain("legacy-secret");

        var reloaded = ControlPanelService.CreateDefault(new CfgControlPanelSettingsStore(path));
        reloaded.GetApiKey().Should().Be("legacy-secret");
        reloaded.GetState().TargetLanguage.Should().Be("ja");
    }

    [Fact]
    public void CreateDefault_loads_expanded_plugin_and_ai_settings()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "com.hanfeng.hunityautotranslator.cfg");
        var first = ControlPanelService.CreateDefault(new CfgControlPanelSettingsStore(path));

        first.UpdateConfig(new UpdateConfigRequest(
            TargetLanguage: "zh-Hant",
            RequestTimeoutSeconds: 45,
            ReasoningEffort: "low",
            OutputVerbosity: "low",
            DeepSeekThinkingMode: "disabled",
            Temperature: 0.2,
            CustomPrompt: "Translate into {TargetLanguage}.",
            MaxSourceTextLength: 800,
            IgnoreInvisibleText: true,
            SkipNumericSymbolText: true,
            EnableCacheLookup: true,
            EnableTranslationContext: true,
            TranslationContextMaxExamples: 7,
            TranslationContextMaxCharacters: 2400,
            ManualEditsOverrideAi: true,
            ReapplyRememberedTranslations: true,
            CacheRetentionDays: 180));

        var second = ControlPanelService.CreateDefault(new CfgControlPanelSettingsStore(path));
        var state = second.GetState();

        state.RequestTimeoutSeconds.Should().Be(45);
        state.ReasoningEffort.Should().Be("low");
        state.OutputVerbosity.Should().Be("low");
        state.DeepSeekThinkingMode.Should().Be("disabled");
        state.Temperature.Should().Be(0.2);
        state.CustomPrompt.Should().Be("Translate into {TargetLanguage}.");
        state.DefaultSystemPrompt.Should().Contain("Target language: Traditional Chinese.");
        state.DefaultSystemPrompt.Should().Contain("Style: Natural localization is allowed");
        state.MaxSourceTextLength.Should().Be(800);
        state.IgnoreInvisibleText.Should().BeTrue();
        state.SkipNumericSymbolText.Should().BeTrue();
        state.EnableCacheLookup.Should().BeTrue();
        state.EnableTranslationContext.Should().BeTrue();
        state.TranslationContextMaxExamples.Should().Be(7);
        state.TranslationContextMaxCharacters.Should().Be(2400);
        state.ManualEditsOverrideAi.Should().BeTrue();
        state.ReapplyRememberedTranslations.Should().BeTrue();
        state.CacheRetentionDays.Should().Be(180);
    }

    [Fact]
    public void UpdateConfig_can_clear_temperature()
    {
        var service = ControlPanelService.CreateDefault();

        service.UpdateConfig(new UpdateConfigRequest(Temperature: 0.7));
        service.GetState().Temperature.Should().Be(0.7);

        service.UpdateConfig(new UpdateConfigRequest(ClearTemperature: true));

        service.GetState().Temperature.Should().BeNull();
        service.GetConfig().Temperature.Should().BeNull();
    }

    [Fact]
    public void UpdateConfig_can_clear_custom_prompt_and_restore_default_prompt_preview()
    {
        var service = ControlPanelService.CreateDefault();
        var defaultPrompt = service.GetState().DefaultSystemPrompt;

        service.UpdateConfig(new UpdateConfigRequest(CustomPrompt: "Output {TargetLanguage}."));
        var customized = service.GetState();

        customized.CustomPrompt.Should().Be("Output {TargetLanguage}.");
        customized.DefaultSystemPrompt.Should().Be(defaultPrompt);

        service.UpdateConfig(new UpdateConfigRequest(CustomPrompt: string.Empty));
        var restored = service.GetState();

        restored.CustomPrompt.Should().BeNull();
        restored.DefaultSystemPrompt.Should().Be(defaultPrompt);
    }

    [Fact]
    public void CreateDefault_loads_glossary_settings_and_keeps_auto_extraction_disabled_by_default()
    {
        var defaults = RuntimeConfig.CreateDefault();
        defaults.EnableGlossary.Should().BeTrue();
        defaults.EnableAutoTermExtraction.Should().BeFalse();
        defaults.GlossaryMaxTerms.Should().BeGreaterThan(0);
        defaults.GlossaryMaxCharacters.Should().BeGreaterThan(0);

        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "com.hanfeng.hunityautotranslator.cfg");
        var first = ControlPanelService.CreateDefault(new CfgControlPanelSettingsStore(path));
        first.UpdateConfig(new UpdateConfigRequest(
            EnableGlossary: false,
            EnableAutoTermExtraction: true,
            GlossaryMaxTerms: 7,
            GlossaryMaxCharacters: 500));

        var second = ControlPanelService.CreateDefault(new CfgControlPanelSettingsStore(path));
        var state = second.GetState();

        state.EnableGlossary.Should().BeFalse();
        state.EnableAutoTermExtraction.Should().BeTrue();
        state.GlossaryMaxTerms.Should().Be(7);
        state.GlossaryMaxCharacters.Should().Be(500);
    }

    [Fact]
    public void CreateDefault_loads_font_replacement_settings()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "com.hanfeng.hunityautotranslator.cfg");
        var first = ControlPanelService.CreateDefault(new CfgControlPanelSettingsStore(path));

        first.UpdateConfig(new UpdateConfigRequest(
            EnableFontReplacement: true,
            ReplaceUguiFonts: false,
            ReplaceTmpFonts: true,
            ReplaceImguiFonts: false,
            AutoUseCjkFallbackFonts: false,
            ReplacementFontName: "Noto Sans SC",
            ReplacementFontFile: @"C:\Fonts\NotoSansSC-Regular.otf",
            FontSamplingPointSize: 220));

        var second = ControlPanelService.CreateDefault(new CfgControlPanelSettingsStore(path));
        var state = second.GetState();

        state.EnableFontReplacement.Should().BeTrue();
        state.ReplaceUguiFonts.Should().BeFalse();
        state.ReplaceTmpFonts.Should().BeTrue();
        state.ReplaceImguiFonts.Should().BeFalse();
        state.AutoUseCjkFallbackFonts.Should().BeFalse();
        state.ReplacementFontName.Should().Be("Noto Sans SC");
        state.ReplacementFontFile.Should().Be(@"C:\Fonts\NotoSansSC-Regular.otf");
        state.FontSamplingPointSize.Should().Be(180);
        second.GetConfig().FontSamplingPointSize.Should().Be(180);
    }

    [Fact]
    public void CreateDefault_loads_font_size_adjustment_settings()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "com.hanfeng.hunityautotranslator.cfg");
        var first = ControlPanelService.CreateDefault(new CfgControlPanelSettingsStore(path));

        first.UpdateConfig(new UpdateConfigRequest(
            FontSizeAdjustmentMode: FontSizeAdjustmentMode.Percent,
            FontSizeAdjustmentValue: -10));

        var second = ControlPanelService.CreateDefault(new CfgControlPanelSettingsStore(path));
        var state = second.GetState();

        state.FontSizeAdjustmentMode.Should().Be(FontSizeAdjustmentMode.Percent);
        state.FontSizeAdjustmentValue.Should().Be(-10);
        second.GetConfig().FontSizeAdjustmentMode.Should().Be(FontSizeAdjustmentMode.Percent);
        second.GetConfig().FontSizeAdjustmentValue.Should().Be(-10);
    }

    [Fact]
    public void UpdateConfig_clamps_font_size_adjustment_to_safe_range()
    {
        var service = ControlPanelService.CreateDefault();

        service.UpdateConfig(new UpdateConfigRequest(
            FontSizeAdjustmentMode: FontSizeAdjustmentMode.Percent,
            FontSizeAdjustmentValue: -200));

        service.GetConfig().FontSizeAdjustmentValue.Should().Be(-99);
        service.UpdateConfig(new UpdateConfigRequest(FontSizeAdjustmentValue: 500));
        service.GetConfig().FontSizeAdjustmentValue.Should().Be(300);
    }

    [Fact]
    public void Automatic_font_fallbacks_are_reported_in_state_without_persisting_settings()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "com.hanfeng.hunityautotranslator.cfg");
        var first = ControlPanelService.CreateDefault(new CfgControlPanelSettingsStore(path));

        first.SetAutomaticFontFallbacks(null, @" C:\Windows\Fonts\NotoSansSC-VF.ttf ");
        first.UpdateConfig(new UpdateConfigRequest(MaxConcurrentRequests: 6));

        var state = first.GetState();
        state.AutomaticReplacementFontName.Should().BeNull();
        state.AutomaticReplacementFontFile.Should().Be(@"C:\Windows\Fonts\NotoSansSC-VF.ttf");
        state.ReplacementFontName.Should().BeNull();
        state.ReplacementFontFile.Should().BeNull();

        var second = ControlPanelService.CreateDefault(new CfgControlPanelSettingsStore(path));
        var reloaded = second.GetState();
        reloaded.AutomaticReplacementFontName.Should().BeNull();
        reloaded.AutomaticReplacementFontFile.Should().BeNull();
        reloaded.ReplacementFontName.Should().BeNull();
        reloaded.ReplacementFontFile.Should().BeNull();
        reloaded.MaxConcurrentRequests.Should().Be(6);
    }
}

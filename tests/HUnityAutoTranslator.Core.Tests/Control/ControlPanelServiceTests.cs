using FluentAssertions;
using HUnityAutoTranslator.Core.Caching;
using HUnityAutoTranslator.Core.Configuration;
using HUnityAutoTranslator.Core.Control;
using HUnityAutoTranslator.Core.Prompts;

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
            CompletedUtc: DateTimeOffset.Parse("2026-04-26T00:00:00Z"),
            ProviderProfileId: "primary",
            ProviderProfileName: "OpenAI 主配置",
            ProviderProfileKind: "OpenAI"),
            totalTokens: 123,
            elapsed: TimeSpan.FromMilliseconds(250));
        metrics.RecordTranslationRequestFinished();

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
        state.RecentTranslations[0].ProviderProfileName.Should().Be("OpenAI 主配置");
        state.RecentTranslations[0].ProviderProfileKind.Should().Be("OpenAI");
    }

    [Fact]
    public void Snapshot_reports_memory_diagnostics()
    {
        var metrics = new ControlPanelMetrics();
        var config = RuntimeConfig.CreateDefault();
        var service = ControlPanelService.CreateDefault(metrics);
        metrics.RecordCaptured(TranslationCacheKey.Create("Start", config.TargetLanguage, config.Provider, "policy"));

        var state = service.GetState(
            queueCount: 7,
            cacheCount: 11,
            writebackQueueCount: 3,
            memoryDiagnostics: new MemoryDiagnosticsSnapshot(
                ManagedMemoryBytes: 1024,
                UnityAllocatedMemoryBytes: 2048,
                UnityReservedMemoryBytes: 4096,
                UnityMonoHeapBytes: 512,
                QueueCount: 0,
                WritebackQueueCount: 0,
                CapturedKeyTrackerCount: 0,
                RegisteredTextTargetCount: 4,
                FontCacheCount: 5,
                TmpFontAssetCacheCount: 6,
                ImguiFontResolutionCacheCount: 7,
                TextureRecordCount: 8,
                ReplacementTextureCount: 9,
                TexturePngBytes: 10));

        state.MemoryDiagnostics.Should().NotBeNull();
        state.MemoryDiagnostics!.QueueCount.Should().Be(7);
        state.MemoryDiagnostics.WritebackQueueCount.Should().Be(3);
        state.MemoryDiagnostics.CapturedKeyTrackerCount.Should().Be(1);
        state.MemoryDiagnostics.FontCacheCount.Should().Be(5);
        state.MemoryDiagnostics.TextureRecordCount.Should().Be(8);
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
    public void Snapshot_reports_project_metadata_for_version_page()
    {
        var service = ControlPanelService.CreateDefault();

        service.SetRuntimeVersions("9.8.7", "6.0.0.0");

        var state = service.GetState();

        state.PluginVersion.Should().Be("9.8.7");
        state.BepInExVersion.Should().Be("6.0.0.0");
        state.ProjectAuthor.Should().Be("HanFengRuYue");
        state.ProjectRepositoryUrl.Should().Be("https://github.com/HanFengRuYue/HUnityAutoTranslator");
    }

    [Fact]
    public void Snapshot_reports_no_active_provider_profile_when_queue_is_empty()
    {
        var service = ControlPanelService.CreateDefault();

        var state = service.GetState();

        state.ProviderProfiles.Should().BeEmpty();
        state.ActiveProviderProfileId.Should().BeNull();
        state.ActiveProviderProfileName.Should().BeNull();
        state.ActiveProviderProfileKind.Should().BeNull();
        state.ActiveProviderProfileModel.Should().BeNull();
        state.ApiKeyConfigured.Should().BeFalse();
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
    public void Automatic_game_title_is_effective_without_being_persisted()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "com.hanfeng.hunityautotranslator.cfg");
        var first = ControlPanelService.CreateDefault(new CfgControlPanelSettingsStore(path));

        first.SetAutomaticGameTitle("The Glitched Attraction");
        first.UpdateConfig(new UpdateConfigRequest(TargetLanguage: "ja"));

        var state = first.GetState();
        state.GameTitle.Should().BeNull();
        state.AutomaticGameTitle.Should().Be("The Glitched Attraction");
        state.DefaultSystemPrompt.Should().Contain("Game title: The Glitched Attraction.");
        first.GetConfig().GameTitle.Should().Be("The Glitched Attraction");

        var cfg = File.ReadAllText(path);
        cfg.Should().Contain("GameTitle =");
        cfg.Should().NotContain("GameTitle = The Glitched Attraction");

        var second = ControlPanelService.CreateDefault(new CfgControlPanelSettingsStore(path));
        second.GetConfig().GameTitle.Should().BeNull();
    }

    [Fact]
    public void Manual_game_title_overrides_automatic_game_title()
    {
        var service = ControlPanelService.CreateDefault();

        service.SetAutomaticGameTitle("The Glitched Attraction");
        service.UpdateConfig(new UpdateConfigRequest(GameTitle: "Manual Test Game"));

        var state = service.GetState();
        state.GameTitle.Should().Be("Manual Test Game");
        state.AutomaticGameTitle.Should().Be("The Glitched Attraction");
        service.GetConfig().GameTitle.Should().Be("Manual Test Game");
        state.DefaultSystemPrompt.Should().Contain("Game title: Manual Test Game.");
        state.DefaultSystemPrompt.Should().NotContain("Game title: The Glitched Attraction.");
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

        service.CreateProviderProfile(new ProviderProfileUpdateRequest(
            Name: "兼容网关",
            Kind: ProviderKind.OpenAICompatible,
            BaseUrl: "http://127.0.0.1:9000",
            Endpoint: "/v1/chat/completions",
            Model: "local-model",
            MaxConcurrentRequests: 150,
            RequestsPerMinute: 700));
        service.UpdateConfig(new UpdateConfigRequest(
            TargetLanguage: "ko",
            Enabled: false,
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
        state.RequestsPerMinute.Should().Be(700);
        config.EnableUgui.Should().BeFalse();
        config.EnableTmp.Should().BeFalse();
        config.EnableImgui.Should().BeTrue();
        config.MaxScanTargetsPerTick.Should().Be(12);
        config.MaxWritebacksPerFrame.Should().Be(64);
    }

    [Fact]
    public void UpdateConfig_saves_openai_compatible_advanced_options_without_reserved_headers()
    {
        var service = ControlPanelService.CreateDefault();

        service.CreateProviderProfile(new ProviderProfileUpdateRequest(
            Name: "兼容网关",
            Kind: ProviderKind.OpenAICompatible,
            OpenAICompatibleCustomHeaders: """
                X-App-Title: HUnity
                Authorization: Bearer wrong
                Content-Type: text/plain
                # ignored comment
                X-Feature: translation
                """,
            OpenAICompatibleExtraBodyJson: """{"stream":false,"metadata":{"source":"panel"}}"""));

        var state = service.GetState();
        var config = service.GetConfig();

        state.OpenAICompatibleCustomHeaders.Should().Be("X-App-Title: HUnity\nX-Feature: translation");
        state.OpenAICompatibleExtraBodyJson.Should().Be("""{"stream":false,"metadata":{"source":"panel"}}""");
        config.OpenAICompatibleCustomHeaders.Should().Be("X-App-Title: HUnity\nX-Feature: translation");
        config.OpenAICompatibleExtraBodyJson.Should().Be("""{"stream":false,"metadata":{"source":"panel"}}""");
        config.Provider.OpenAICompatibleCustomHeaders.Should().Be("X-App-Title: HUnity\nX-Feature: translation");
        config.Provider.OpenAICompatibleExtraBodyJson.Should().Be("""{"stream":false,"metadata":{"source":"panel"}}""");
    }

    [Fact]
    public void UpdateConfig_rejects_invalid_openai_compatible_advanced_options_without_crashing()
    {
        var service = ControlPanelService.CreateDefault();
        var profile = service.CreateProviderProfile(new ProviderProfileUpdateRequest(
            Name: "兼容网关",
            Kind: ProviderKind.OpenAICompatible,
            OpenAICompatibleCustomHeaders: "X-Gateway: one",
            OpenAICompatibleExtraBodyJson: """{"stream":false}"""));

        service.UpdateProviderProfile(profile.Id, new ProviderProfileUpdateRequest(
            OpenAICompatibleCustomHeaders: """
                MissingSeparator
                Authorization: Bearer wrong
                """,
            OpenAICompatibleExtraBodyJson: """["not-an-object"]"""));

        var state = service.GetState();

        state.OpenAICompatibleCustomHeaders.Should().Be("X-Gateway: one");
        state.OpenAICompatibleExtraBodyJson.Should().Be("""{"stream":false}""");
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
                ParallelSlots: 2,
                BatchSize: 4096,
                UBatchSize: 1024,
                FlashAttentionMode: "on")));

        var state = service.GetState();
        var config = service.GetConfig();

        state.ProviderKind.Should().Be(ProviderKind.LlamaCpp);
        state.ApiKeyConfigured.Should().BeTrue();
        state.BaseUrl.Should().Be("http://127.0.0.1:0");
        state.Endpoint.Should().Be("/v1/chat/completions");
        state.Model.Should().Be("local-model");
        state.LlamaCpp.ModelPath.Should().Be(@"D:\Models\game-ui.gguf");
        state.LlamaCpp.ContextSize.Should().Be(8192);
        state.LlamaCpp.GpuLayers.Should().Be(80);
        state.LlamaCpp.ParallelSlots.Should().Be(2);
        state.LlamaCpp.BatchSize.Should().Be(4096);
        state.LlamaCpp.UBatchSize.Should().Be(1024);
        state.LlamaCpp.FlashAttentionMode.Should().Be("on");
        state.LlamaCpp.AutoStartOnStartup.Should().BeFalse();
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
                ParallelSlots: 99,
                BatchSize: 32,
                UBatchSize: 99999,
                FlashAttentionMode: "maybe")));

        var state = service.GetState();

        state.LlamaCpp.ModelPath.Should().BeNull();
        state.LlamaCpp.ContextSize.Should().Be(512);
        state.LlamaCpp.GpuLayers.Should().Be(0);
        state.LlamaCpp.ParallelSlots.Should().Be(16);
        state.LlamaCpp.BatchSize.Should().Be(128);
        state.LlamaCpp.UBatchSize.Should().Be(128);
        state.LlamaCpp.FlashAttentionMode.Should().Be("auto");
        state.LlamaCpp.AutoStartOnStartup.Should().BeFalse();
    }

    [Fact]
    public void Provider_profiles_allow_one_llamacpp_profile_without_api_key_and_require_model_path_for_ready_queue()
    {
        var service = ControlPanelService.CreateDefault();

        var local = service.CreateProviderProfile(new ProviderProfileUpdateRequest(
            Name: "本地 Qwen",
            Kind: ProviderKind.LlamaCpp,
            LlamaCpp: LlamaCppConfig.Default() with { ModelPath = null, ParallelSlots = 3 }));

        var state = service.GetState();
        state.ProviderProfiles.Should().ContainSingle();
        state.ProviderProfiles![0].Kind.Should().Be(ProviderKind.LlamaCpp);
        state.ProviderProfiles[0].ApiKeyConfigured.Should().BeTrue();
        state.ProviderProfiles[0].LlamaCpp.Should().NotBeNull();
        service.GetReadyProviderRuntimeProfiles().Should().BeEmpty();

        service.UpdateProviderProfile(local.Id, new ProviderProfileUpdateRequest(
            LlamaCpp: LlamaCppConfig.Default() with
            {
                ModelPath = @"D:\Models\qwen.gguf",
                ParallelSlots = 2,
                BatchSize = 4096,
                UBatchSize = 1024,
                FlashAttentionMode = "on"
            }));

        var ready = service.GetReadyProviderRuntimeProfiles().Should().ContainSingle().Which;
        ready.Profile.Kind.Should().Be(ProviderKind.LlamaCpp);
        ready.ApiKey.Should().BeNull();
        ready.LlamaCpp.Should().NotBeNull();
        ready.LlamaCpp!.ModelPath.Should().Be(@"D:\Models\qwen.gguf");
        ready.MaxConcurrentRequests.Should().Be(2);
    }

    [Fact]
    public void LlamaCpp_provider_profile_failures_do_not_enter_cooldown()
    {
        var service = ControlPanelService.CreateDefault();
        service.CreateProviderProfile(new ProviderProfileUpdateRequest(
            Name: "本地 Qwen",
            Kind: ProviderKind.LlamaCpp,
            LlamaCpp: LlamaCppConfig.Default() with { ModelPath = @"D:\Models\qwen.gguf" }));
        var ready = service.GetReadyProviderRuntimeProfiles().Should().ContainSingle().Which;

        service.RegisterProviderProfileFailure(ready, "正在启动 llama.cpp 本地模型。").Should().BeFalse();
        service.RegisterProviderProfileFailure(ready, "正在启动 llama.cpp 本地模型。").Should().BeFalse();

        var state = service.GetState().ProviderProfiles.Should().ContainSingle().Which;
        state.ConsecutiveFailureCount.Should().Be(2);
        state.CooldownRemainingSeconds.Should().Be(0);
        state.LastError.Should().Be("正在启动 llama.cpp 本地模型。");
        service.GetReadyProviderRuntimeProfiles().Should().ContainSingle();
    }

    [Fact]
    public void Updating_provider_profile_clears_previous_failure_state()
    {
        var service = ControlPanelService.CreateDefault();
        var profile = service.CreateProviderProfile(new ProviderProfileUpdateRequest(
            Name: "OpenAI 主配置",
            Kind: ProviderKind.OpenAI,
            ApiKey: "sk-test"));
        var ready = service.GetReadyProviderRuntimeProfiles().Should().ContainSingle().Which;
        service.RegisterProviderProfileFailure(ready, "第一次失败").Should().BeFalse();
        service.RegisterProviderProfileFailure(ready, "第二次失败").Should().BeTrue();
        service.GetState().ProviderProfiles![0].CooldownRemainingSeconds.Should().BeGreaterThan(0);

        service.UpdateProviderProfile(profile.Id, new ProviderProfileUpdateRequest(
            Name: "OpenAI 主配置已保存",
            ApiKey: "sk-test"));

        var state = service.GetState().ProviderProfiles.Should().ContainSingle().Which;
        state.Name.Should().Be("OpenAI 主配置已保存");
        state.ConsecutiveFailureCount.Should().Be(0);
        state.CooldownRemainingSeconds.Should().Be(0);
        state.LastError.Should().BeNull();
        service.GetReadyProviderRuntimeProfiles().Should().ContainSingle();
    }

    [Fact]
    public void SetProviderProfileLlamaCppAutoStartOnStartup_updates_profile_without_changing_global_config()
    {
        var service = ControlPanelService.CreateDefault();
        var local = service.CreateProviderProfile(new ProviderProfileUpdateRequest(
            Name: "本地 Qwen",
            Kind: ProviderKind.LlamaCpp,
            LlamaCpp: LlamaCppConfig.Default() with { ModelPath = @"D:\Models\qwen.gguf" }));

        service.SetProviderProfileLlamaCppAutoStartOnStartup(local.Id, true);

        var state = service.GetState().ProviderProfiles.Should().ContainSingle().Which;
        state.LlamaCpp!.AutoStartOnStartup.Should().BeTrue();

        service.SetProviderProfileLlamaCppAutoStartOnStartup(local.Id, false);

        service.GetState().ProviderProfiles![0].LlamaCpp!.AutoStartOnStartup.Should().BeFalse();
    }

    [Fact]
    public void Provider_profiles_use_provider_specific_high_throughput_default_rpm()
    {
        var service = ControlPanelService.CreateDefault();

        var openAi = service.CreateProviderProfile(new ProviderProfileUpdateRequest(
            Name: "OpenAI",
            Kind: ProviderKind.OpenAI));
        var deepSeek = service.CreateProviderProfile(new ProviderProfileUpdateRequest(
            Name: "DeepSeek",
            Kind: ProviderKind.DeepSeek));
        var compatible = service.CreateProviderProfile(new ProviderProfileUpdateRequest(
            Name: "兼容网关",
            Kind: ProviderKind.OpenAICompatible));

        openAi.RequestsPerMinute.Should().Be(500);
        deepSeek.RequestsPerMinute.Should().Be(15000);
        compatible.RequestsPerMinute.Should().Be(15000);
    }

    [Fact]
    public void Provider_profiles_report_active_profile_details_for_status_page()
    {
        var service = ControlPanelService.CreateDefault();

        service.CreateProviderProfile(new ProviderProfileUpdateRequest(
            Name: "DeepSeek 主配置",
            Kind: ProviderKind.DeepSeek,
            Model: "deepseek-v4-flash",
            ApiKey: "secret"));

        var state = service.GetState();

        state.ActiveProviderProfileName.Should().Be("DeepSeek 主配置");
        state.ActiveProviderProfileKind.Should().Be(ProviderKind.DeepSeek);
        state.ActiveProviderProfileModel.Should().Be("deepseek-v4-flash");
    }

    [Fact]
    public void Snapshot_reports_current_runtime_provider_only_while_translation_is_in_flight()
    {
        var metrics = new ControlPanelMetrics();
        var service = ControlPanelService.CreateDefault(metrics);
        var runtimeProfile = ProviderRuntimeProfile.Create(
            ProviderProfileDefinition.CreateDefault("DeepSeek runtime", ProviderKind.DeepSeek, priority: 0) with
            {
                ApiKey = "key"
            });

        metrics.RecordTranslationStarted();
        metrics.RecordProviderAttempt(runtimeProfile);

        var activeState = service.GetState();
        activeState.ActiveTranslationProvider.Should().NotBeNull();
        activeState.ActiveTranslationProvider!.Name.Should().Be("DeepSeek runtime");
        activeState.ActiveTranslationProvider.Kind.Should().Be(ProviderKind.DeepSeek);

        metrics.RecordTranslationRequestFinished();

        service.GetState().ActiveTranslationProvider.Should().BeNull();
    }

    [Fact]
    public void Provider_profiles_reject_second_llamacpp_profile()
    {
        var service = ControlPanelService.CreateDefault();

        service.CreateProviderProfile(new ProviderProfileUpdateRequest(
            Name: "本地模型",
            Kind: ProviderKind.LlamaCpp,
            LlamaCpp: LlamaCppConfig.Default() with { ModelPath = @"D:\Models\one.gguf" }));

        var act = () => service.CreateProviderProfile(new ProviderProfileUpdateRequest(
            Name: "第二个本地模型",
            Kind: ProviderKind.LlamaCpp,
            LlamaCpp: LlamaCppConfig.Default() with { ModelPath = @"D:\Models\two.gguf" }));

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*只能创建一个本地模型配置*");
    }

    [Fact]
    public void Provider_profiles_mix_online_and_llamacpp_by_priority()
    {
        var service = ControlPanelService.CreateDefault();
        var online = service.CreateProviderProfile(new ProviderProfileUpdateRequest(
            Name: "兼容网关",
            Kind: ProviderKind.OpenAICompatible,
            BaseUrl: "http://127.0.0.1:9000",
            Endpoint: "/v1/chat/completions",
            Model: "gateway-model"));
        var local = service.CreateProviderProfile(new ProviderProfileUpdateRequest(
            Name: "本地模型",
            Kind: ProviderKind.LlamaCpp,
            LlamaCpp: LlamaCppConfig.Default() with { ModelPath = @"D:\Models\local.gguf", ParallelSlots = 2 }));

        service.GetReadyProviderRuntimeProfiles().Select(profile => profile.Id)
            .Should().Equal(online.Id, local.Id);

        service.MoveProviderProfile(local.Id, -1);

        service.GetReadyProviderRuntimeProfiles().Select(profile => profile.Id)
            .Should().Equal(local.Id, online.Id);
    }

    [Fact]
    public void CreateDefault_does_not_migrate_legacy_llamacpp_cfg_into_provider_profiles()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var path = Path.Combine(root, "com.hanfeng.hunityautotranslator.cfg");
        Directory.CreateDirectory(root);
        File.WriteAllText(path, """
            [基础]

            ProviderKind = LlamaCpp

            [llama.cpp]

            ModelPath = D:\Models\legacy.gguf
            ContextSize = 4096
            GpuLayers = 999
            ParallelSlots = 1
            """);

        var service = ControlPanelService.CreateDefault(
            new CfgControlPanelSettingsStore(path),
            new EncryptedProviderProfileStore(Path.Combine(root, "providers")));

        service.GetState().ProviderProfiles.Should().BeEmpty();
        service.GetConfig().LlamaCpp.ModelPath.Should().Be(@"D:\Models\legacy.gguf");
    }

    [Fact]
    public void SetLlamaCppAutoStartOnStartup_persists_without_changing_runtime_port()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "com.hanfeng.hunityautotranslator.cfg");
        var first = ControlPanelService.CreateDefault(new CfgControlPanelSettingsStore(path));
        first.UpdateConfig(new UpdateConfigRequest(
            ProviderKind: ProviderKind.LlamaCpp,
            LlamaCpp: new LlamaCppConfig(
                ModelPath: @"D:\Models\game-ui.gguf",
                ContextSize: 4096,
                GpuLayers: 999,
                ParallelSlots: 1,
                BatchSize: 2048,
                UBatchSize: 512,
                FlashAttentionMode: "auto")));
        first.SetLlamaCppStatus(LlamaCppServerStatus.Running(
            first.GetConfig().LlamaCpp,
            backend: "Vulkan",
            port: 51234,
            release: "b8943",
            variant: "Vulkan",
            serverPath: @"D:\Game\BepInEx\plugins\HUnityAutoTranslator\llama.cpp\llama-server.exe"));

        first.SetLlamaCppAutoStartOnStartup(true);
        var second = ControlPanelService.CreateDefault(new CfgControlPanelSettingsStore(path));

        second.GetConfig().LlamaCpp.AutoStartOnStartup.Should().BeTrue();
        second.GetState().LlamaCppStatus.Port.Should().Be(0);
        second.GetConfig().Provider.BaseUrl.Should().Be("http://127.0.0.1:0");

        second.SetLlamaCppAutoStartOnStartup(false);
        var third = ControlPanelService.CreateDefault(new CfgControlPanelSettingsStore(path));

        third.GetConfig().LlamaCpp.AutoStartOnStartup.Should().BeFalse();
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
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var path = Path.Combine(root, "com.hanfeng.hunityautotranslator.cfg");
        var profileStore = new EncryptedProviderProfileStore(Path.Combine(root, "providers"));
        var first = ControlPanelService.CreateDefault(new CfgControlPanelSettingsStore(path), profileStore);

        first.UpdateConfig(new UpdateConfigRequest(
            TargetLanguage: "ja",
            Enabled: false,
            EnableUgui: false,
            EnableTmp: true,
            EnableImgui: false,
            MaxScanTargetsPerTick: 33,
            MaxWritebacksPerFrame: 44));
        first.CreateProviderProfile(new ProviderProfileUpdateRequest(
            Name: "兼容网关",
            Kind: ProviderKind.OpenAICompatible,
            BaseUrl: "http://127.0.0.1:9000",
            Endpoint: "/v1/chat/completions",
            Model: "local-model",
            ApiKey: "secret-value",
            MaxConcurrentRequests: 7,
            RequestsPerMinute: 123,
            OpenAICompatibleCustomHeaders: "X-App-Title: HUnity",
            OpenAICompatibleExtraBodyJson: """{"stream":false}"""));

        var second = ControlPanelService.CreateDefault(new CfgControlPanelSettingsStore(path), profileStore);

        var state = second.GetState();
        state.Enabled.Should().BeFalse();
        state.TargetLanguage.Should().Be("ja");
        state.ProviderKind.Should().Be(ProviderKind.OpenAICompatible);
        state.BaseUrl.Should().Be("http://127.0.0.1:9000");
        state.Endpoint.Should().Be("/v1/chat/completions");
        state.Model.Should().Be("local-model");
        state.OpenAICompatibleCustomHeaders.Should().Be("X-App-Title: HUnity");
        state.OpenAICompatibleExtraBodyJson.Should().Be("""{"stream":false}""");
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
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var path = Path.Combine(root, "com.hanfeng.hunityautotranslator.cfg");
        var profileStore = new EncryptedProviderProfileStore(Path.Combine(root, "providers"));
        var first = ControlPanelService.CreateDefault(new CfgControlPanelSettingsStore(path), profileStore);

        first.SetApiKey("secret-value");

        var cfg = File.ReadAllText(path);
        cfg.Should().NotContain("EncryptedApiKey");
        cfg.Should().NotContain("secret-value");
        var profileFile = Directory.GetFiles(Path.Combine(root, "providers"), "*.hutprovider").Should().ContainSingle().Which;
        File.ReadAllText(profileFile).Should().NotContain("secret-value");

        var second = ControlPanelService.CreateDefault(new CfgControlPanelSettingsStore(path), profileStore);
        second.GetApiKey().Should().Be("secret-value");
        second.GetState().ApiKeyConfigured.Should().BeTrue();
    }

    [Fact]
    public void Cfg_store_ignores_legacy_plaintext_api_key_and_preserves_section_on_save()
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
        service.GetApiKey().Should().BeNull();
        service.GetState().TargetLanguage.Should().Be("ja");
        service.UpdateConfig(new UpdateConfigRequest(TargetLanguage: "ko"));

        var cfg = File.ReadAllText(path);
        cfg.Should().Contain("EncryptedApiKey");
        cfg.Should().Contain("legacy-secret");

        var reloaded = ControlPanelService.CreateDefault(new CfgControlPanelSettingsStore(path));
        reloaded.GetApiKey().Should().BeNull();
        reloaded.GetState().TargetLanguage.Should().Be("ko");
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
            EnableTranslationDebugLogs: true,
            EnableTranslationContext: true,
            TranslationContextMaxExamples: 7,
            TranslationContextMaxCharacters: 2400,
            ManualEditsOverrideAi: true,
            ReapplyRememberedTranslations: true));

        var second = ControlPanelService.CreateDefault(new CfgControlPanelSettingsStore(path));
        var state = second.GetState();

        state.RequestTimeoutSeconds.Should().Be(30);
        state.ReasoningEffort.Should().Be("none");
        state.OutputVerbosity.Should().Be("low");
        state.DeepSeekThinkingMode.Should().Be("disabled");
        state.Temperature.Should().BeNull();
        state.CustomPrompt.Should().Be("Translate into {TargetLanguage}.");
        state.DefaultSystemPrompt.Should().Contain("Target language: Traditional Chinese.");
        state.DefaultSystemPrompt.Should().Contain("Style: Natural localization is allowed");
        state.MaxSourceTextLength.Should().Be(800);
        state.IgnoreInvisibleText.Should().BeTrue();
        state.SkipNumericSymbolText.Should().BeTrue();
        state.EnableCacheLookup.Should().BeTrue();
        state.EnableTranslationDebugLogs.Should().BeTrue();
        state.EnableTranslationContext.Should().BeTrue();
        state.TranslationContextMaxExamples.Should().Be(7);
        state.TranslationContextMaxCharacters.Should().Be(2400);
        state.ManualEditsOverrideAi.Should().BeTrue();
        state.ReapplyRememberedTranslations.Should().BeTrue();
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
    public void UpdateConfig_exposes_prompt_template_overrides_and_defaults()
    {
        var service = ControlPanelService.CreateDefault();

        service.UpdateConfig(new UpdateConfigRequest(
            PromptTemplates: new PromptTemplateConfig(
                SystemPrompt: "Translate to {TargetLanguage}.",
                BatchUserPrompt: "Use JSON {InputJson}")));

        var state = service.GetState();

        state.DefaultPromptTemplates.SystemPrompt.Should().Contain("{TargetLanguage}");
        state.DefaultPromptTemplates.BatchUserPrompt.Should().Contain("{InputJson}");
        state.PromptTemplates.SystemPrompt.Should().Be("Translate to {TargetLanguage}.");
        state.PromptTemplates.BatchUserPrompt.Should().Be("Use JSON {InputJson}");
        state.CustomPrompt.Should().Be("Translate to {TargetLanguage}.");
        service.GetConfig().PromptTemplates.SystemPrompt.Should().Be("Translate to {TargetLanguage}.");
    }

    [Fact]
    public void UpdateConfig_maps_legacy_custom_prompt_to_system_prompt_template()
    {
        var service = ControlPanelService.CreateDefault();

        service.UpdateConfig(new UpdateConfigRequest(CustomPrompt: "Legacy {TargetLanguage}."));

        var state = service.GetState();

        state.CustomPrompt.Should().Be("Legacy {TargetLanguage}.");
        state.PromptTemplates.SystemPrompt.Should().Be("Legacy {TargetLanguage}.");
        service.GetConfig().PromptTemplates.SystemPrompt.Should().Be("Legacy {TargetLanguage}.");
    }

    [Fact]
    public void UpdateConfig_rejects_prompt_templates_missing_required_placeholders()
    {
        var service = ControlPanelService.CreateDefault();

        service.UpdateConfig(new UpdateConfigRequest(
            PromptTemplates: new PromptTemplateConfig(
                BatchUserPrompt: "Missing input json",
                GlossaryTermsSection: "Missing glossary json",
                QualityRepairPrompt: "Missing repair context {SourceText} {InvalidTranslation} {FailureReason}")));

        var state = service.GetState();

        state.PromptTemplates.BatchUserPrompt.Should().BeNull();
        state.PromptTemplates.GlossaryTermsSection.Should().BeNull();
        state.PromptTemplates.QualityRepairPrompt.Should().BeNull();
        service.GetConfig().PromptTemplates.HasOverrides.Should().BeFalse();
    }

    [Fact]
    public void UpdateConfig_saves_translation_quality_settings()
    {
        var service = ControlPanelService.CreateDefault();
        var request = TranslationQualityConfig.Default() with
        {
            Mode = "custom",
            Enabled = false,
            AllowAlreadyTargetLanguageSource = false,
            EnableRepair = false,
            MaxRetryCount = 1,
            RejectShortSettingValue = false,
            ShortSettingValueMinSourceLength = 3,
            ShortSettingValueMaxTranslationTextElements = 2
        };

        service.UpdateConfig(new UpdateConfigRequest(TranslationQuality: request));

        var state = service.GetState();
        state.TranslationQuality.Should().Be(request);
        service.GetConfig().TranslationQuality.Should().Be(request);
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
    public void CreateDefault_loads_tmp_native_auto_size_setting_and_defaults_off()
    {
        var defaults = ControlPanelService.CreateDefault();

        defaults.GetState().EnableTmpNativeAutoSize.Should().BeFalse();
        defaults.GetConfig().EnableTmpNativeAutoSize.Should().BeFalse();

        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "com.hanfeng.hunityautotranslator.cfg");
        var first = ControlPanelService.CreateDefault(new CfgControlPanelSettingsStore(path));

        first.UpdateConfig(new UpdateConfigRequest(EnableTmpNativeAutoSize: true));

        var second = ControlPanelService.CreateDefault(new CfgControlPanelSettingsStore(path));

        second.GetState().EnableTmpNativeAutoSize.Should().BeTrue();
        second.GetConfig().EnableTmpNativeAutoSize.Should().BeTrue();
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
    public void Draft_provider_profile_uses_current_form_without_saving_profile()
    {
        var service = ControlPanelService.CreateDefault();

        var draft = service.CreateDraftProviderRuntimeProfile(new ProviderProfileUpdateRequest(
            Name: "DeepSeek 草稿",
            Kind: ProviderKind.DeepSeek,
            BaseUrl: " https://api.deepseek.com ",
            Endpoint: " /chat/completions ",
            Model: " deepseek-chat ",
            ApiKey: " sk-draft ",
            RequestsPerMinute: 15000));

        draft.Name.Should().Be("DeepSeek 草稿");
        draft.Profile.Kind.Should().Be(ProviderKind.DeepSeek);
        draft.Profile.BaseUrl.Should().Be("https://api.deepseek.com");
        draft.Profile.Endpoint.Should().Be("/chat/completions");
        draft.Profile.Model.Should().Be("deepseek-chat");
        draft.ApiKey.Should().Be("sk-draft");
        service.GetState().ProviderProfiles.Should().BeEmpty();
    }

    [Fact]
    public void Draft_provider_profile_reuses_saved_key_when_editing_existing_profile()
    {
        var service = ControlPanelService.CreateDefault();
        var saved = service.CreateProviderProfile(new ProviderProfileUpdateRequest(
            Name: "Saved DeepSeek",
            Kind: ProviderKind.DeepSeek,
            ApiKey: "sk-saved",
            Model: "deepseek-chat"));

        var draft = service.CreateDraftProviderRuntimeProfile(new ProviderProfileUpdateRequest(
            Id: saved.Id,
            Name: "Edited DeepSeek",
            Kind: ProviderKind.DeepSeek,
            Model: "deepseek-v4-flash"));

        draft.Id.Should().Be(saved.Id);
        draft.Name.Should().Be("Edited DeepSeek");
        draft.Profile.Model.Should().Be("deepseek-v4-flash");
        draft.ApiKey.Should().Be("sk-saved");
        service.GetState().ProviderProfiles.Should().ContainSingle().Which.Name.Should().Be("Saved DeepSeek");
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

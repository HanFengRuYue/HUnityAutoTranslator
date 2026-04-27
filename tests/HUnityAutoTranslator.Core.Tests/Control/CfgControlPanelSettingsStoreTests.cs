using FluentAssertions;
using HUnityAutoTranslator.Core.Configuration;
using HUnityAutoTranslator.Core.Control;
using HUnityAutoTranslator.Core.Prompts;

namespace HUnityAutoTranslator.Core.Tests.Control;

public sealed class CfgControlPanelSettingsStoreTests
{
    [Fact]
    public void CreateDefault_generates_commented_cfg_when_file_is_missing()
    {
        var path = NewCfgPath();

        ControlPanelService.CreateDefault(new CfgControlPanelSettingsStore(path));

        File.Exists(path).Should().BeTrue();
        var cfg = File.ReadAllText(path);
        cfg.Should().Contain("# HUnityAutoTranslator 配置文件");
        cfg.Should().Contain("[基础]");
        cfg.Should().Contain("Enabled = true");
        cfg.Should().Contain("ProviderKind = OpenAI");
        cfg.Should().Contain("ApiKey =");
    }

    [Fact]
    public void Save_generates_chinese_sections_comments_and_named_enums()
    {
        var path = NewCfgPath();
        var store = new CfgControlPanelSettingsStore(path);

        store.Save(new ControlPanelSettings
        {
            Config = new UpdateConfigRequest(
                Enabled: true,
                TargetLanguage: "zh-Hans",
                AutoOpenControlPanel: true,
                HttpPort: 48110,
                ProviderKind: ProviderKind.LlamaCpp,
                BaseUrl: "http://127.0.0.1:0",
                Endpoint: "/v1/chat/completions",
                Model: "local-model",
                Style: TranslationStyle.Localized,
                Temperature: null,
                CustomPrompt: null,
                FontSizeAdjustmentMode: FontSizeAdjustmentMode.Percent,
                FontSizeAdjustmentValue: -10,
                LlamaCpp: new LlamaCppConfig(null, 4096, 999, 1))
        });

        var cfg = File.ReadAllText(path);

        cfg.Should().Contain("[基础]");
        cfg.Should().Contain("[快捷键]");
        cfg.Should().Contain("[翻译服务]");
        cfg.Should().Contain("[扫描与写回]");
        cfg.Should().Contain("[缓存与上下文]");
        cfg.Should().Contain("[术语库]");
        cfg.Should().Contain("[字体]");
        cfg.Should().Contain("[llama.cpp]");
        cfg.Should().Contain("# 是否启用自动翻译。");
        cfg.Should().Contain("# 翻译服务商。");
        cfg.Should().Contain("# API Key 临时填写处。");
        cfg.Should().Contain("ProviderKind = LlamaCpp");
        cfg.Should().Contain("Style = Localized");
        cfg.Should().Contain("FontSizeAdjustmentMode = Percent");
        cfg.Should().NotContain("ProviderKind = 3");
        cfg.Should().NotContain("Style = 2");
        cfg.Should().NotContain("FontSizeAdjustmentMode = 2");
    }

    [Fact]
    public void CreateDefault_loads_manual_cfg_values_and_reuses_existing_clamps()
    {
        var path = NewCfgPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, """
            [基础]
            Enabled = false
            TargetLanguage = ja
            AutoOpenControlPanel = false
            HttpPort = 70000

            [快捷键]
            OpenControlPanelHotkey = Ctrl+Shift+P
            ToggleTranslationHotkey = Alt+T
            ForceScanHotkey = Ctrl+G
            ToggleFontHotkey = Shift+F8

            [翻译服务]
            ProviderKind = DeepSeek
            BaseUrl = https://api.deepseek.com
            Endpoint = /chat/completions
            Model = deepseek-v4-pro
            Style = UiConcise
            RequestTimeoutSeconds = 2
            ReasoningEffort = max
            OutputVerbosity = high
            DeepSeekThinkingMode = enabled
            Temperature = 1.25
            CustomPrompt = Line 1\nLine 2

            [扫描与写回]
            MaxConcurrentRequests = 150
            RequestsPerMinute = 700
            MaxBatchCharacters = 128
            ScanIntervalMilliseconds = 50
            MaxScanTargetsPerTick = 0
            MaxWritebacksPerFrame = 999
            MaxSourceTextLength = 5
            IgnoreInvisibleText = false
            SkipNumericSymbolText = false
            EnableUgui = false
            EnableTmp = true
            EnableImgui = false

            [缓存与上下文]
            EnableCacheLookup = false
            EnableTranslationContext = true
            TranslationContextMaxExamples = 99
            TranslationContextMaxCharacters = 9000
            ManualEditsOverrideAi = false
            ReapplyRememberedTranslations = false
            CacheRetentionDays = 9999

            [术语库]
            EnableGlossary = false
            EnableAutoTermExtraction = true
            GlossaryMaxTerms = 101
            GlossaryMaxCharacters = 9000

            [字体]
            EnableFontReplacement = false
            ReplaceUguiFonts = false
            ReplaceTmpFonts = true
            ReplaceImguiFonts = false
            AutoUseCjkFallbackFonts = false
            ReplacementFontName = Noto Sans SC
            ReplacementFontFile = C:\Fonts\NotoSansSC-Regular.otf
            FontSamplingPointSize = 220
            FontSizeAdjustmentMode = Points
            FontSizeAdjustmentValue = -200

            [llama.cpp]
            ModelPath = D:\Models\game-ui.gguf
            ContextSize = 64
            GpuLayers = -5
            ParallelSlots = 99
            """);

        var service = ControlPanelService.CreateDefault(new CfgControlPanelSettingsStore(path));
        var config = service.GetConfig();

        config.Enabled.Should().BeFalse();
        config.TargetLanguage.Should().Be("ja");
        config.AutoOpenControlPanel.Should().BeFalse();
        config.HttpHost.Should().Be("127.0.0.1");
        config.HttpPort.Should().Be(65535);
        config.OpenControlPanelHotkey.Should().Be("Ctrl+Shift+P");
        config.ToggleTranslationHotkey.Should().Be("Alt+T");
        config.ForceScanHotkey.Should().Be("Ctrl+G");
        config.ToggleFontHotkey.Should().Be("Shift+F8");
        config.Provider.Kind.Should().Be(ProviderKind.DeepSeek);
        config.Provider.Model.Should().Be("deepseek-v4-pro");
        config.Style.Should().Be(TranslationStyle.UiConcise);
        config.RequestTimeoutSeconds.Should().Be(5);
        config.MaxConcurrentRequests.Should().Be(100);
        config.RequestsPerMinute.Should().Be(600);
        config.MaxBatchCharacters.Should().Be(256);
        config.ScanInterval.TotalMilliseconds.Should().Be(100);
        config.MaxScanTargetsPerTick.Should().Be(1);
        config.MaxWritebacksPerFrame.Should().Be(512);
        config.MaxSourceTextLength.Should().Be(20);
        config.TranslationContextMaxExamples.Should().Be(20);
        config.TranslationContextMaxCharacters.Should().Be(8000);
        config.GlossaryMaxTerms.Should().Be(100);
        config.GlossaryMaxCharacters.Should().Be(8000);
        config.FontSamplingPointSize.Should().Be(180);
        config.FontSizeAdjustmentMode.Should().Be(FontSizeAdjustmentMode.Points);
        config.FontSizeAdjustmentValue.Should().Be(-99);
        config.CustomPrompt.Should().Be("Line 1\nLine 2");
        config.ReplacementFontFile.Should().Be(@"C:\Fonts\NotoSansSC-Regular.otf");
        config.LlamaCpp.ModelPath.Should().Be(@"D:\Models\game-ui.gguf");
        config.LlamaCpp.ContextSize.Should().Be(512);
        config.LlamaCpp.GpuLayers.Should().Be(0);
        config.LlamaCpp.ParallelSlots.Should().Be(16);
    }

    [Fact]
    public void Generated_cfg_documents_online_concurrency_limit_and_llamacpp_slots()
    {
        var path = NewCfgPath();

        ControlPanelService.CreateDefault(new CfgControlPanelSettingsStore(path));

        var cfg = File.ReadAllText(path);
        cfg.Should().Contain("MaxConcurrentRequests = 4");
        cfg.Should().Contain("范围：1 到 100");
        cfg.Should().Contain("llama.cpp 使用 ParallelSlots");
    }

    [Fact]
    public void Plaintext_api_key_is_encrypted_and_removed_after_load()
    {
        var path = NewCfgPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, """
            [翻译服务]
            ProviderKind = OpenAI
            ApiKey = secret-value
            EncryptedApiKey =
            """);

        var service = ControlPanelService.CreateDefault(new CfgControlPanelSettingsStore(path));

        service.GetApiKey().Should().Be("secret-value");
        service.GetState().ApiKeyConfigured.Should().BeTrue();
        var cfg = File.ReadAllText(path);
        cfg.Should().Contain("ApiKey =");
        cfg.Should().Contain("EncryptedApiKey = ");
        cfg.Should().NotContain("secret-value");
    }

    private static string NewCfgPath()
    {
        return Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "com.hanfeng.hunityautotranslator.cfg");
    }
}

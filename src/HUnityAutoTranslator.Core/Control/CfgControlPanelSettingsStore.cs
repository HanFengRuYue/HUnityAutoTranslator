using System.Globalization;
using System.Text;
using HUnityAutoTranslator.Core.Configuration;
using HUnityAutoTranslator.Core.Prompts;

namespace HUnityAutoTranslator.Core.Control;

public sealed class CfgControlPanelSettingsStore : IControlPanelSettingsStore
{
    private const string BasicSection = "基础";
    private const string HotkeySection = "快捷键";
    private const string ProviderSection = "翻译服务";
    private const string ScanSection = "扫描与写回";
    private const string CacheSection = "缓存与上下文";
    private const string GlossarySection = "术语库";
    private const string PromptTemplateSection = "提示词模板";
    private const string FontSection = "字体";
    private const string TextureImageSection = "贴图文字翻译";
    private const string LlamaCppSection = "llama.cpp";

    private readonly string _filePath;
    private string? _legacyProviderSectionText;

    public CfgControlPanelSettingsStore(string filePath)
    {
        _filePath = filePath;
    }

    public ControlPanelSettings Load()
    {
        if (!File.Exists(_filePath))
        {
            var settings = new ControlPanelSettings();
            Save(settings);
            return settings;
        }

        try
        {
            var lines = File.ReadAllLines(_filePath);
            _legacyProviderSectionText = CaptureSection(lines, ProviderSection);
            var values = Parse(lines);
            return new ControlPanelSettings
            {
                Config = BuildConfig(values),
                TextureImageEncryptedSecret = ReadString(values, TextureImageSection, "TextureImageSecret")
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new ControlPanelSettings();
        }
    }

    public void Save(ControlPanelSettings settings)
    {
        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(_filePath, BuildFile(settings, _legacyProviderSectionText), Encoding.UTF8);
    }

    private static UpdateConfigRequest BuildConfig(Dictionary<string, Dictionary<string, string>> values)
    {
        return new UpdateConfigRequest(
            TargetLanguage: ReadString(values, BasicSection, "TargetLanguage"),
            GameTitle: ReadString(values, BasicSection, "GameTitle"),
            MaxConcurrentRequests: ReadInt(values, ScanSection, "MaxConcurrentRequests"),
            RequestsPerMinute: ReadInt(values, ScanSection, "RequestsPerMinute"),
            Enabled: ReadBool(values, BasicSection, "Enabled"),
            AutoOpenControlPanel: ReadBool(values, BasicSection, "AutoOpenControlPanel"),
            HttpPort: ReadInt(values, BasicSection, "HttpPort"),
            OpenControlPanelHotkey: ReadString(values, HotkeySection, "OpenControlPanelHotkey"),
            ToggleTranslationHotkey: ReadString(values, HotkeySection, "ToggleTranslationHotkey"),
            ForceScanHotkey: ReadString(values, HotkeySection, "ForceScanHotkey"),
            ToggleFontHotkey: ReadString(values, HotkeySection, "ToggleFontHotkey"),
            ProviderKind: ReadEnum<ProviderKind>(values, BasicSection, "ProviderKind"),
            Style: ReadEnum<TranslationStyle>(values, BasicSection, "Style"),
            MaxBatchCharacters: ReadInt(values, ScanSection, "MaxBatchCharacters"),
            ScanIntervalMilliseconds: ReadInt(values, ScanSection, "ScanIntervalMilliseconds"),
            MaxScanTargetsPerTick: ReadInt(values, ScanSection, "MaxScanTargetsPerTick"),
            MaxWritebacksPerFrame: ReadInt(values, ScanSection, "MaxWritebacksPerFrame"),
            CustomPrompt: null,
            PromptTemplates: BuildPromptTemplates(values),
            MaxSourceTextLength: ReadInt(values, ScanSection, "MaxSourceTextLength"),
            IgnoreInvisibleText: ReadBool(values, ScanSection, "IgnoreInvisibleText"),
            SkipNumericSymbolText: ReadBool(values, ScanSection, "SkipNumericSymbolText"),
            EnableCacheLookup: ReadBool(values, CacheSection, "EnableCacheLookup"),
            EnableTranslationDebugLogs: ReadBool(values, CacheSection, "EnableTranslationDebugLogs"),
            EnableTranslationContext: ReadBool(values, CacheSection, "EnableTranslationContext"),
            TranslationContextMaxExamples: ReadInt(values, CacheSection, "TranslationContextMaxExamples"),
            TranslationContextMaxCharacters: ReadInt(values, CacheSection, "TranslationContextMaxCharacters"),
            EnableGlossary: ReadBool(values, GlossarySection, "EnableGlossary"),
            EnableAutoTermExtraction: ReadBool(values, GlossarySection, "EnableAutoTermExtraction"),
            GlossaryMaxTerms: ReadInt(values, GlossarySection, "GlossaryMaxTerms"),
            GlossaryMaxCharacters: ReadInt(values, GlossarySection, "GlossaryMaxCharacters"),
            ManualEditsOverrideAi: ReadBool(values, CacheSection, "ManualEditsOverrideAi"),
            ReapplyRememberedTranslations: ReadBool(values, CacheSection, "ReapplyRememberedTranslations"),
            EnableUgui: ReadBool(values, ScanSection, "EnableUgui"),
            EnableTmp: ReadBool(values, ScanSection, "EnableTmp"),
            EnableImgui: ReadBool(values, ScanSection, "EnableImgui"),
            EnableFontReplacement: ReadBool(values, FontSection, "EnableFontReplacement"),
            ReplaceUguiFonts: ReadBool(values, FontSection, "ReplaceUguiFonts"),
            ReplaceTmpFonts: ReadBool(values, FontSection, "ReplaceTmpFonts"),
            ReplaceImguiFonts: ReadBool(values, FontSection, "ReplaceImguiFonts"),
            AutoUseCjkFallbackFonts: ReadBool(values, FontSection, "AutoUseCjkFallbackFonts"),
            ReplacementFontName: ReadString(values, FontSection, "ReplacementFontName"),
            ReplacementFontFile: ReadString(values, FontSection, "ReplacementFontFile"),
            FontSamplingPointSize: ReadInt(values, FontSection, "FontSamplingPointSize"),
            FontSizeAdjustmentMode: ReadEnum<FontSizeAdjustmentMode>(values, FontSection, "FontSizeAdjustmentMode"),
            FontSizeAdjustmentValue: ReadDouble(values, FontSection, "FontSizeAdjustmentValue"),
            TextureImageTranslation: BuildTextureImageTranslationConfig(values),
            LlamaCpp: BuildLlamaCppConfig(values));
    }

    private static TextureImageTranslationConfig? BuildTextureImageTranslationConfig(Dictionary<string, Dictionary<string, string>> values)
    {
        var keys = new[]
        {
            "Enabled",
            "BaseUrl",
            "EditEndpoint",
            "VisionEndpoint",
            "ImageModel",
            "VisionModel",
            "Quality",
            "TimeoutSeconds",
            "MaxConcurrentRequests",
            "EnableVisionConfirmation"
        };
        if (!keys.Any(key => HasValue(values, TextureImageSection, key)))
        {
            return null;
        }

        var defaults = TextureImageTranslationConfig.Default();
        return new TextureImageTranslationConfig(
            ReadBool(values, TextureImageSection, "Enabled") ?? defaults.Enabled,
            ReadString(values, TextureImageSection, "BaseUrl") ?? defaults.BaseUrl,
            ReadString(values, TextureImageSection, "EditEndpoint") ?? defaults.EditEndpoint,
            ReadString(values, TextureImageSection, "VisionEndpoint") ?? defaults.VisionEndpoint,
            ReadString(values, TextureImageSection, "ImageModel") ?? defaults.ImageModel,
            ReadString(values, TextureImageSection, "VisionModel") ?? defaults.VisionModel,
            ReadString(values, TextureImageSection, "Quality") ?? defaults.Quality,
            ReadInt(values, TextureImageSection, "TimeoutSeconds") ?? defaults.TimeoutSeconds,
            ReadInt(values, TextureImageSection, "MaxConcurrentRequests") ?? defaults.MaxConcurrentRequests,
            ReadBool(values, TextureImageSection, "EnableVisionConfirmation") ?? defaults.EnableVisionConfirmation);
    }

    private static LlamaCppConfig? BuildLlamaCppConfig(Dictionary<string, Dictionary<string, string>> values)
    {
        var keys = new[] { "ModelPath", "ContextSize", "GpuLayers", "ParallelSlots", "BatchSize", "UBatchSize", "FlashAttentionMode", "AutoStartOnStartup" };
        if (!keys.Any(key => HasValue(values, LlamaCppSection, key)))
        {
            return null;
        }

        var defaults = LlamaCppConfig.Default();
        return new LlamaCppConfig(
            ReadString(values, LlamaCppSection, "ModelPath"),
            ReadInt(values, LlamaCppSection, "ContextSize") ?? defaults.ContextSize,
            ReadInt(values, LlamaCppSection, "GpuLayers") ?? defaults.GpuLayers,
            ReadInt(values, LlamaCppSection, "ParallelSlots") ?? defaults.ParallelSlots,
            ReadInt(values, LlamaCppSection, "BatchSize") ?? defaults.BatchSize,
            ReadInt(values, LlamaCppSection, "UBatchSize") ?? defaults.UBatchSize,
            ReadString(values, LlamaCppSection, "FlashAttentionMode") ?? defaults.FlashAttentionMode,
            ReadBool(values, LlamaCppSection, "AutoStartOnStartup") ?? defaults.AutoStartOnStartup);
    }

    private static PromptTemplateConfig? BuildPromptTemplates(Dictionary<string, Dictionary<string, string>> values)
    {
        var keys = new[]
        {
            "SystemPrompt",
            "GlossarySystemPolicy",
            "BatchUserPrompt",
            "GlossaryTermsSection",
            "CurrentItemContextSection",
            "ItemHintsSection",
            "ContextExamplesSection",
            "GlossaryRepairPrompt",
            "QualityRepairPrompt",
            "GlossaryExtractionSystemPrompt",
            "GlossaryExtractionUserPrompt"
        };
        if (!keys.Any(key => HasValue(values, PromptTemplateSection, key)))
        {
            return null;
        }

        return new PromptTemplateConfig(
            ReadPrompt(values, PromptTemplateSection, "SystemPrompt"),
            ReadPrompt(values, PromptTemplateSection, "GlossarySystemPolicy"),
            ReadPrompt(values, PromptTemplateSection, "BatchUserPrompt"),
            ReadPrompt(values, PromptTemplateSection, "GlossaryTermsSection"),
            ReadPrompt(values, PromptTemplateSection, "CurrentItemContextSection"),
            ReadPrompt(values, PromptTemplateSection, "ItemHintsSection"),
            ReadPrompt(values, PromptTemplateSection, "ContextExamplesSection"),
            ReadPrompt(values, PromptTemplateSection, "GlossaryRepairPrompt"),
            ReadPrompt(values, PromptTemplateSection, "QualityRepairPrompt"),
            ReadPrompt(values, PromptTemplateSection, "GlossaryExtractionSystemPrompt"),
            ReadPrompt(values, PromptTemplateSection, "GlossaryExtractionUserPrompt"));
    }

    private static string BuildFile(ControlPanelSettings settings, string? legacyProviderSectionText)
    {
        var defaults = RuntimeConfig.CreateDefault();
        var config = settings.Config ?? new UpdateConfigRequest();
        var llamaCpp = config.LlamaCpp ?? defaults.LlamaCpp;
        var promptTemplates = config.PromptTemplates ?? PromptTemplateConfig.Empty;
        var builder = new StringBuilder();

        builder.AppendLine("# HUnityAutoTranslator 配置文件");
        builder.AppendLine("# 可以在游戏关闭时手工编辑。保存后下次启动插件会读取这里的设置。");
        builder.AppendLine("# 控制面板只监听 127.0.0.1，本文件只允许修改端口，避免误暴露到局域网。");
        builder.AppendLine();

        Section(builder, BasicSection);
        Option(builder, "是否启用自动翻译。", "true", "true 或 false。", "Enabled", Bool(config.Enabled ?? defaults.Enabled));
        Option(builder, "目标语言。常用：zh-Hans 简体中文，zh-Hant 繁体中文，ja 日文，ko 韩文，en 英文。", "zh-Hans", null, "TargetLanguage", Text(config.TargetLanguage ?? defaults.TargetLanguage));
        Option(builder, "游戏名称。留空会使用当前 Unity 游戏名；填写后会手动覆盖自动检测值。", "留空", "示例：The Glitched Attraction。", "GameTitle", Text(config.GameTitle));
        Option(builder, "翻译风格。", "Localized", "可选：Faithful 忠实、Natural 自然、Localized 本地化、UiConcise UI短句。", "Style", EnumText(config.Style ?? defaults.Style));
        Option(builder, "翻译后端。在线服务商使用服务商配置；本地模型使用 LlamaCpp。", "OpenAI", "可选：OpenAI、LlamaCpp。OpenAI 表示按在线服务商配置优先级执行。", "ProviderKind", EnumText(config.ProviderKind ?? defaults.Provider.Kind));
        Option(builder, "启动游戏后是否自动打开控制面板。", "true", "true 或 false。", "AutoOpenControlPanel", Bool(config.AutoOpenControlPanel ?? defaults.AutoOpenControlPanel));
        Option(builder, "控制面板端口。监听地址固定为 127.0.0.1。", "48110", "范围：1 到 65535。", "HttpPort", Int(config.HttpPort ?? defaults.HttpPort));

        Section(builder, HotkeySection);
        Option(builder, "打开控制面板的快捷键。写 None 可禁用。", "Alt+H", "格式示例：Alt+H、Ctrl+Shift+P、None。", "OpenControlPanelHotkey", Text(config.OpenControlPanelHotkey ?? defaults.OpenControlPanelHotkey));
        Option(builder, "临时启用或暂停翻译的快捷键。写 None 可禁用。", "Alt+F", "格式示例：Alt+F、Ctrl+T、None。", "ToggleTranslationHotkey", Text(config.ToggleTranslationHotkey ?? defaults.ToggleTranslationHotkey));
        Option(builder, "强制重新扫描当前画面文字的快捷键。写 None 可禁用。", "Alt+G", "格式示例：Alt+G、Ctrl+G、None。", "ForceScanHotkey", Text(config.ForceScanHotkey ?? defaults.ForceScanHotkey));
        Option(builder, "临时启用或暂停字体辅助的快捷键。TMP 会优先保留原字体材质和效果。写 None 可禁用。", "Alt+D", "格式示例：Alt+D、Shift+F8、None。", "ToggleFontHotkey", Text(config.ToggleFontHotkey ?? defaults.ToggleFontHotkey));

        Section(builder, PromptTemplateSection);
        Option(builder, "系统提示词模板。留空使用内置默认；需要换行时写 \\n。", "内置默认", "可用：{TargetLanguage}、{StyleInstruction}、{GameTitle}、{GameContext}、{GlossarySystemPolicy}。", "SystemPrompt", Prompt(promptTemplates.SystemPrompt));
        Option(builder, "术语库系统约束模板。留空使用内置默认。", "内置默认", "通常由系统提示词中的 {GlossarySystemPolicy} 引入。", "GlossarySystemPolicy", Prompt(promptTemplates.GlossarySystemPolicy));
        Option(builder, "批量翻译用户提示词模板。留空使用内置默认，必须保留 {InputJson}。", "内置默认", "可用：{PromptSections}、{InputJson}。", "BatchUserPrompt", Prompt(promptTemplates.BatchUserPrompt));
        Option(builder, "术语库条目片段模板。留空使用内置默认，必须保留 {GlossaryTermsJson}。", "内置默认", null, "GlossaryTermsSection", Prompt(promptTemplates.GlossaryTermsSection));
        Option(builder, "当前文本上下文片段模板。留空使用内置默认，必须保留 {ItemContextsJson}。", "内置默认", null, "CurrentItemContextSection", Prompt(promptTemplates.CurrentItemContextSection));
        Option(builder, "短文本提示片段模板。留空使用内置默认，必须保留 {ItemHintsJson}。", "内置默认", null, "ItemHintsSection", Prompt(promptTemplates.ItemHintsSection));
        Option(builder, "历史上下文示例片段模板。留空使用内置默认，必须保留 {ContextExamplesJson}。", "内置默认", null, "ContextExamplesSection", Prompt(promptTemplates.ContextExamplesSection));
        Option(builder, "术语库修复提示词模板。留空使用内置默认，必须保留 {SourceText}、{InvalidTranslation}、{FailureReason}。", "内置默认", "可用：{RequiredGlossaryTermsJson}、{RequiredGlossaryTermsBlock}。", "GlossaryRepairPrompt", Prompt(promptTemplates.GlossaryRepairPrompt));
        Option(builder, "质量修复提示词模板。留空使用内置默认，必须保留 {SourceText}、{InvalidTranslation}、{FailureReason}、{RepairContextJson}。", "内置默认", null, "QualityRepairPrompt", Prompt(promptTemplates.QualityRepairPrompt));
        Option(builder, "自动术语抽取系统提示词模板。留空使用内置默认。", "内置默认", null, "GlossaryExtractionSystemPrompt", Prompt(promptTemplates.GlossaryExtractionSystemPrompt));
        Option(builder, "自动术语抽取用户提示词模板。留空使用内置默认，必须保留 {RowsJson}。", "内置默认", null, "GlossaryExtractionUserPrompt", Prompt(promptTemplates.GlossaryExtractionUserPrompt));

        Section(builder, ScanSection);
        Option(builder, "在线服务商同时进行的翻译请求数量。llama.cpp 使用 ParallelSlots。", "4", "范围：1 到 100。", "MaxConcurrentRequests", Int(config.MaxConcurrentRequests ?? defaults.MaxConcurrentRequests));
        Option(builder, "每分钟最多发送多少次请求。", "60", "范围：1 到 600。", "RequestsPerMinute", Int(config.RequestsPerMinute ?? defaults.RequestsPerMinute));
        Option(builder, "单批次最多包含多少字符。", "1800", "范围：256 到 8000。", "MaxBatchCharacters", Int(config.MaxBatchCharacters ?? defaults.MaxBatchCharacters));
        Option(builder, "扫描画面文字的间隔，单位毫秒。", "750", "范围：100 到 5000。", "ScanIntervalMilliseconds", Int(config.ScanIntervalMilliseconds ?? (int)defaults.ScanInterval.TotalMilliseconds));
        Option(builder, "每次扫描最多处理多少个文字目标。", "256", "范围：1 到 4096。", "MaxScanTargetsPerTick", Int(config.MaxScanTargetsPerTick ?? defaults.MaxScanTargetsPerTick));
        Option(builder, "每帧最多写回多少条翻译。", "32", "范围：1 到 512。", "MaxWritebacksPerFrame", Int(config.MaxWritebacksPerFrame ?? defaults.MaxWritebacksPerFrame));
        Option(builder, "单条源文本最大长度，超过会跳过。", "2000", "范围：20 到 10000。", "MaxSourceTextLength", Int(config.MaxSourceTextLength ?? defaults.MaxSourceTextLength));
        Option(builder, "是否忽略不可见文字。", "true", "true 或 false。", "IgnoreInvisibleText", Bool(config.IgnoreInvisibleText ?? defaults.IgnoreInvisibleText));
        Option(builder, "是否跳过纯数字或符号文本。", "true", "true 或 false。", "SkipNumericSymbolText", Bool(config.SkipNumericSymbolText ?? defaults.SkipNumericSymbolText));
        Option(builder, "是否扫描 Unity UGUI 文本。", "true", "true 或 false。", "EnableUgui", Bool(config.EnableUgui ?? defaults.EnableUgui));
        Option(builder, "是否扫描 TextMeshPro 文本。", "true", "true 或 false。", "EnableTmp", Bool(config.EnableTmp ?? defaults.EnableTmp));
        Option(builder, "是否处理 IMGUI 文本。", "true", "true 或 false。", "EnableImgui", Bool(config.EnableImgui ?? defaults.EnableImgui));

        Section(builder, CacheSection);
        Option(builder, "是否启用翻译缓存命中。", "true", "true 或 false。", "EnableCacheLookup", Bool(config.EnableCacheLookup ?? defaults.EnableCacheLookup));
        Option(builder, "是否输出 AI 翻译请求结构调试日志。只建议测试时开启，会记录源文、场景、组件层级和 item hints。", "false", "true 或 false。", "EnableTranslationDebugLogs", Bool(config.EnableTranslationDebugLogs ?? defaults.EnableTranslationDebugLogs));
        Option(builder, "是否把同组件或同场景附近文本作为翻译上下文。", "true", "true 或 false。", "EnableTranslationContext", Bool(config.EnableTranslationContext ?? defaults.EnableTranslationContext));
        Option(builder, "最多附带多少条上下文示例。", "4", "范围：0 到 20。", "TranslationContextMaxExamples", Int(config.TranslationContextMaxExamples ?? defaults.TranslationContextMaxExamples));
        Option(builder, "上下文示例最多占用多少字符。", "1200", "范围：0 到 8000。", "TranslationContextMaxCharacters", Int(config.TranslationContextMaxCharacters ?? defaults.TranslationContextMaxCharacters));
        Option(builder, "人工编辑过的翻译是否优先于 AI 结果。", "true", "true 或 false。", "ManualEditsOverrideAi", Bool(config.ManualEditsOverrideAi ?? defaults.ManualEditsOverrideAi));
        Option(builder, "切换场景或刷新时是否重新套用已记住的翻译。", "true", "true 或 false。", "ReapplyRememberedTranslations", Bool(config.ReapplyRememberedTranslations ?? defaults.ReapplyRememberedTranslations));

        Section(builder, GlossarySection);
        Option(builder, "是否启用术语库约束。", "true", "true 或 false。", "EnableGlossary", Bool(config.EnableGlossary ?? defaults.EnableGlossary));
        Option(builder, "是否允许 AI 自动抽取术语。默认关闭，避免误写术语库。", "false", "true 或 false。", "EnableAutoTermExtraction", Bool(config.EnableAutoTermExtraction ?? defaults.EnableAutoTermExtraction));
        Option(builder, "每次翻译最多使用多少条术语。", "16", "范围：0 到 100。", "GlossaryMaxTerms", Int(config.GlossaryMaxTerms ?? defaults.GlossaryMaxTerms));
        Option(builder, "术语提示最多占用多少字符。", "1200", "范围：0 到 8000。", "GlossaryMaxCharacters", Int(config.GlossaryMaxCharacters ?? defaults.GlossaryMaxCharacters));

        Section(builder, FontSection);
        Option(builder, "是否启用字体替换。", "true", "true 或 false。", "EnableFontReplacement", Bool(config.EnableFontReplacement ?? defaults.EnableFontReplacement));
        Option(builder, "是否替换 UGUI 字体。", "true", "true 或 false。", "ReplaceUguiFonts", Bool(config.ReplaceUguiFonts ?? defaults.ReplaceUguiFonts));
        Option(builder, "是否替换 TextMeshPro 字体。", "true", "true 或 false。", "ReplaceTmpFonts", Bool(config.ReplaceTmpFonts ?? defaults.ReplaceTmpFonts));
        Option(builder, "是否替换 IMGUI 字体。", "true", "true 或 false。", "ReplaceImguiFonts", Bool(config.ReplaceImguiFonts ?? defaults.ReplaceImguiFonts));
        Option(builder, "未填写自定义字体时，是否自动使用系统中的中日韩字体。", "true", "true 或 false。", "AutoUseCjkFallbackFonts", Bool(config.AutoUseCjkFallbackFonts ?? defaults.AutoUseCjkFallbackFonts));
        Option(builder, "自定义字体名称。留空表示自动选择。", "留空", "示例：Noto Sans SC、Microsoft YaHei。", "ReplacementFontName", Text(config.ReplacementFontName));
        Option(builder, "自定义字体文件完整路径。留空表示自动选择。", "留空", "示例：C:\\Windows\\Fonts\\msyh.ttc。", "ReplacementFontFile", Text(config.ReplacementFontFile));
        Option(builder, "构建 TMP 字体资产时使用的采样字号。", "90", "范围：16 到 180。", "FontSamplingPointSize", Int(config.FontSamplingPointSize ?? defaults.FontSamplingPointSize));
        Option(builder, "译文字号调整方式。", "Disabled", "可选：Disabled 不调整、Points 加减点数、Percent 按百分比。", "FontSizeAdjustmentMode", EnumText(config.FontSizeAdjustmentMode ?? defaults.FontSizeAdjustmentMode));
        Option(builder, "译文字号调整值。Points 表示点数，Percent 表示百分比。", "0", "范围：-99 到 300。", "FontSizeAdjustmentValue", Double(config.FontSizeAdjustmentValue ?? defaults.FontSizeAdjustmentValue));

        Section(builder, TextureImageSection);
        builder.AppendLine("# 贴图图片服务配置已迁移到控制面板中的加密配置列表。");
        builder.AppendLine("# 旧版这里的服务地址、模型和 Key 仍会被读取并迁移一次，但新版本不会再写回这些敏感配置。");
        builder.AppendLine();

        Section(builder, LlamaCppSection);
        Option(builder, "llama.cpp 模型文件完整路径。ProviderKind 为 LlamaCpp 时使用。", "留空", "示例：D:\\Models\\qwen.gguf。", "ModelPath", Text(llamaCpp.ModelPath));
        Option(builder, "llama.cpp 上下文长度。", "4096", "范围：512 到 131072。", "ContextSize", Int(llamaCpp.ContextSize));
        Option(builder, "llama.cpp GPU 层数。999 表示尽量全部放入 GPU。", "999", "范围：0 到 999。", "GpuLayers", Int(llamaCpp.GpuLayers));
        Option(builder, "llama.cpp 并行槽位数量。", "1", "范围：1 到 16。", "ParallelSlots", Int(llamaCpp.ParallelSlots));

        Option(builder, "llama.cpp prompt batch size。", "2048", "范围：128 到 8192。", "BatchSize", Int(llamaCpp.BatchSize));
        Option(builder, "llama.cpp physical ubatch size。", "512", "范围：64 到 4096，且不超过 BatchSize。", "UBatchSize", Int(llamaCpp.UBatchSize));
        Option(builder, "llama.cpp Flash Attention 模式。", "auto", "可选：auto、on、off。", "FlashAttentionMode", Text(llamaCpp.FlashAttentionMode));
        Option(builder, "上次手动启动成功后，下次启动游戏时是否自动启动 llama.cpp。本项会由启动/停止按钮自动维护。", "false", "true 或 false。", "AutoStartOnStartup", Bool(llamaCpp.AutoStartOnStartup));

        if (!string.IsNullOrWhiteSpace(legacyProviderSectionText))
        {
            builder.AppendLine();
            builder.AppendLine("# 以下为旧版服务商配置，当前版本已忽略，仅为避免覆盖你的原始文本而保留。");
            builder.AppendLine(legacyProviderSectionText.TrimEnd());
        }

        return builder.ToString();
    }

    private static string? CaptureSection(IReadOnlyList<string> lines, string targetSection)
    {
        var captured = new List<string>();
        var inSection = false;
        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (line.StartsWith("[", StringComparison.Ordinal) && line.EndsWith("]", StringComparison.Ordinal))
            {
                var section = line.Substring(1, line.Length - 2).Trim();
                if (inSection && !string.Equals(section, targetSection, StringComparison.OrdinalIgnoreCase))
                {
                    break;
                }

                inSection = string.Equals(section, targetSection, StringComparison.OrdinalIgnoreCase);
            }

            if (inSection)
            {
                captured.Add(rawLine);
            }
        }

        return captured.Count == 0 ? null : string.Join(Environment.NewLine, captured);
    }

    private static Dictionary<string, Dictionary<string, string>> Parse(IEnumerable<string> lines)
    {
        var values = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        var section = string.Empty;
        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal) || line.StartsWith(";", StringComparison.Ordinal))
            {
                continue;
            }

            if (line.StartsWith("[", StringComparison.Ordinal) && line.EndsWith("]", StringComparison.Ordinal))
            {
                section = line.Substring(1, line.Length - 2).Trim();
                if (!values.ContainsKey(section))
                {
                    values[section] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                }

                continue;
            }

            var separator = line.IndexOf('=');
            if (separator < 0)
            {
                continue;
            }

            var key = line.Substring(0, separator).Trim();
            var value = line.Substring(separator + 1).Trim();
            if (key.Length == 0)
            {
                continue;
            }

            if (!values.TryGetValue(section, out var sectionValues))
            {
                sectionValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                values[section] = sectionValues;
            }

            sectionValues[key] = value;
        }

        return values;
    }

    private static void Section(StringBuilder builder, string section)
    {
        builder.Append('[').Append(section).AppendLine("]");
        builder.AppendLine();
    }

    private static void Option(
        StringBuilder builder,
        string description,
        string defaultValue,
        string? note,
        string key,
        string value)
    {
        builder.Append("# ").AppendLine(description);
        builder.Append("# 默认值：").AppendLine(defaultValue);
        if (!string.IsNullOrWhiteSpace(note))
        {
            builder.Append("# ").AppendLine(note);
        }

        builder.Append(key).Append(" = ").AppendLine(value);
        builder.AppendLine();
    }

    private static string? ReadString(Dictionary<string, Dictionary<string, string>> values, string section, string key)
    {
        return values.TryGetValue(section, out var sectionValues) && sectionValues.TryGetValue(key, out var value)
            ? value
            : null;
    }

    private static bool HasValue(Dictionary<string, Dictionary<string, string>> values, string section, string key)
    {
        return values.TryGetValue(section, out var sectionValues) && sectionValues.ContainsKey(key);
    }

    private static bool? ReadBool(Dictionary<string, Dictionary<string, string>> values, string section, string key)
    {
        var value = ReadString(values, section, key);
        if (bool.TryParse(value, out var parsed))
        {
            return parsed;
        }

        if (string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, "on", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, "1", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (string.Equals(value, "no", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, "off", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, "0", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return null;
    }

    private static int? ReadInt(Dictionary<string, Dictionary<string, string>> values, string section, string key)
    {
        return int.TryParse(ReadString(values, section, key), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
    }

    private static double? ReadDouble(Dictionary<string, Dictionary<string, string>> values, string section, string key)
    {
        return double.TryParse(ReadString(values, section, key), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
    }

    private static T? ReadEnum<T>(Dictionary<string, Dictionary<string, string>> values, string section, string key)
        where T : struct, Enum
    {
        var value = ReadString(values, section, key);
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (Enum.TryParse<T>(value, ignoreCase: true, out var parsed) && Enum.IsDefined(typeof(T), parsed))
        {
            return parsed;
        }

        return null;
    }

    private static string? ReadPrompt(Dictionary<string, Dictionary<string, string>> values, string section, string key)
    {
        var value = ReadString(values, section, key);
        return value == null ? null : DecodePrompt(value);
    }

    private static string DecodePrompt(string value)
    {
        return value.Replace("\\r\\n", "\n", StringComparison.Ordinal)
            .Replace("\\n", "\n", StringComparison.Ordinal)
            .Replace("\\r", "\n", StringComparison.Ordinal);
    }

    private static string? NullIfWhiteSpace(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static string Text(string? value)
    {
        return value ?? string.Empty;
    }

    private static string Prompt(string? value)
    {
        return string.IsNullOrEmpty(value)
            ? string.Empty
            : value.Replace("\r\n", "\n", StringComparison.Ordinal)
                .Replace("\r", "\n", StringComparison.Ordinal)
                .Replace("\n", "\\n", StringComparison.Ordinal);
    }

    private static string Bool(bool value)
    {
        return value ? "true" : "false";
    }

    private static string Int(int value)
    {
        return value.ToString(CultureInfo.InvariantCulture);
    }

    private static string Double(double value)
    {
        return value.ToString("0.########", CultureInfo.InvariantCulture);
    }

    private static string NullableDouble(double? value)
    {
        return value.HasValue ? Double(value.Value) : string.Empty;
    }

    private static string EnumText<T>(T value)
        where T : struct, Enum
    {
        return value.ToString();
    }
}

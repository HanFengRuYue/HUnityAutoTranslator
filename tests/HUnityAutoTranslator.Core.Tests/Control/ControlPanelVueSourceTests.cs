using System.Text.RegularExpressions;
using FluentAssertions;

namespace HUnityAutoTranslator.Core.Tests.Control;

public sealed class ControlPanelVueSourceTests
{
    [Fact]
    public void Vue_status_refresh_failure_marks_plugin_status_as_disconnected()
    {
        var storeSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.ControlPanel", "src", "state", "controlPanelStore.ts"));
        var statusPageSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.ControlPanel", "src", "pages", "StatusPage.vue"));

        storeSource.Should().Contain("export function markPanelDisconnected(error: unknown)");
        storeSource.Should().Contain("controlPanelStore.connection = \"offline\";");
        storeSource.Should().Contain("markPanelDisconnected(error);");
        statusPageSource.Should().Contain("value-id=\"enabledText\"");
        statusPageSource.Should().Contain(":tone=\"enabledTone\"");
    }

    [Fact]
    public void Vue_app_removes_duplicate_runtime_topbar()
    {
        var appSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.ControlPanel", "src", "App.vue"));

        appSource.Should().NotContain("workspace-topbar");
        appSource.Should().NotContain("runtime-strip");
        appSource.Should().NotContain("queueText");
        appSource.Should().NotContain("providerText");
        appSource.Should().NotContain("lastRefreshText");
    }

    [Fact]
    public void Vue_status_page_uses_one_metric_layer_without_writeback_or_provider_result()
    {
        var statusPageSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.ControlPanel", "src", "pages", "StatusPage.vue"));

        statusPageSource.Should().Contain("state.value.QueueCount ?? state.value.QueuedTextCount ?? 0");
        statusPageSource.Should().Contain("activeProviderProfileLabel");
        statusPageSource.Should().Contain("activeProviderKindLabel");
        statusPageSource.Should().Contain("activeProviderModelLabel");
        statusPageSource.Should().Contain("MetricCard");
        statusPageSource.Should().Contain("ActiveTranslationProvider");
        statusPageSource.Should().Contain("value-id=\"cacheCount\"");
        statusPageSource.Should().Contain("value-id=\"enabledText\"");
        statusPageSource.Should().Contain("value-id=\"totalTokenCount\"");
        statusPageSource.Should().Contain("label=\"预计token用量\"");
        statusPageSource.Should().NotContain("API Key");
        statusPageSource.Should().NotContain("activeTranslationProviderLabel");
        statusPageSource.Should().NotContain("const rpmLabel");
        statusPageSource.Should().NotContain("formatMilliseconds");
        statusPageSource.Should().NotContain("state.value.QueueCount || state.value.QueuedTextCount || 0");
        statusPageSource.Should().NotContain("WritebackQueueCount");
        statusPageSource.Should().NotContain("status-command");
        statusPageSource.Should().NotContain("providerStatusText");
    }

    [Fact]
    public void Vue_status_page_does_not_fall_back_to_legacy_model_when_no_provider_profile_is_ready()
    {
        var statusPageSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.ControlPanel", "src", "pages", "StatusPage.vue"));
        var activeProviderModelLabel = Regex.Match(
            statusPageSource,
            @"const activeProviderModelLabel = computed\(\(\) => \s*\{(?<body>[\s\S]*?)\}\);");

        activeProviderModelLabel.Success.Should().BeTrue("the status page should explicitly decide when an AI service model is configured");
        activeProviderModelLabel.Groups["body"].Value.Should().Contain("providerProfiles.value.length");
        activeProviderModelLabel.Groups["body"].Value.Should().NotContain("state.value?.Model");
    }

    [Fact]
    public void Vue_status_page_keeps_ai_service_summary_to_requested_four_cards()
    {
        var statusPageSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.ControlPanel", "src", "pages", "StatusPage.vue"));
        var aiServiceBlock = System.Text.RegularExpressions.Regex.Match(
            statusPageSource,
            @"<SectionPanel title=""AI 服务""[\s\S]*?</SectionPanel>");

        aiServiceBlock.Success.Should().BeTrue("status page should render an AI 服务 panel");
        aiServiceBlock.Value.Should().Contain("<span>当前配置</span>");
        aiServiceBlock.Value.Should().Contain("<span>服务商</span>");
        aiServiceBlock.Value.Should().Contain("<span>模型</span>");
        aiServiceBlock.Value.Should().Contain("<span>处理速度</span>");
        System.Text.RegularExpressions.Regex.Matches(aiServiceBlock.Value, "<span>").Count.Should().Be(4);
        aiServiceBlock.Value.Should().NotContain("<span>当前请求/最近使用</span>");
        aiServiceBlock.Value.Should().NotContain("<span>并发</span>");
        aiServiceBlock.Value.Should().NotContain("<span>RPM</span>");
        aiServiceBlock.Value.Should().NotContain("<span>平均耗时</span>");
        aiServiceBlock.Value.Should().NotContain("<span>Token 用量</span>");
    }

    [Fact]
    public void Vue_status_page_shows_in_flight_capacity_without_completed_count_card()
    {
        var statusPageSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.ControlPanel", "src", "pages", "StatusPage.vue"));

        statusPageSource.Should().Contain("const activeTranslationCapacity");
        statusPageSource.Should().Contain("function formatInFlightCapacity");
        statusPageSource.Should().Contain("state.value.InFlightTranslationCount");
        statusPageSource.Should().Contain("state.value.EffectiveMaxConcurrentRequests");
        statusPageSource.Should().Contain("当前正在占用的 AI 请求/槽位数");
        statusPageSource.Should().Contain("同一请求可能批量包含多条文本");
        statusPageSource.Should().Contain("value-id=\"inFlightTranslationCount\"");
        statusPageSource.Should().Contain("LoaderPinwheel");
        statusPageSource.Should().Contain(":icon=\"LoaderPinwheel\"");
        statusPageSource.Should().NotContain("LoaderCircle");
        statusPageSource.Should().NotContain("value-id=\"completedTranslationCount\"");
        statusPageSource.Should().NotContain("state?.CompletedTranslationCount");
    }

    [Fact]
    public void Vue_ai_settings_documents_profile_concurrency_limit_and_effective_capacity()
    {
        var aiPageSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.ControlPanel", "src", "pages", "AiSettingsPage.vue"));
        var statusPageSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.ControlPanel", "src", "pages", "StatusPage.vue"));
        var apiTypesSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.ControlPanel", "src", "types", "api.ts"));

        apiTypesSource.Should().Contain("EffectiveMaxConcurrentRequests: number;");
        apiTypesSource.Should().Contain("ProviderProfiles: ProviderProfileState[] | null;");
        apiTypesSource.Should().Contain("ActiveProviderProfileKind: string | number | null;");
        apiTypesSource.Should().Contain("ActiveProviderProfileModel: string | null;");
        apiTypesSource.Should().Contain("ActiveTranslationProvider: ProviderActivityPreview | null;");
        apiTypesSource.Should().Contain("MaxConcurrentRequests: number;");
        statusPageSource.Should().Contain("state.value.EffectiveMaxConcurrentRequests");
        aiPageSource.Should().Contain("id=\"providerProfileMaxConcurrentRequests\"");
        aiPageSource.Should().Contain("max=\"100\"");
        aiPageSource.Should().Contain("type=\"range\"");
        aiPageSource.Should().Contain("id=\"providerProfileRequestsPerMinute\"");
        aiPageSource.Should().Contain("max=\"15000\"");
        aiPageSource.Should().Contain("llama.cpp 本地模型");
    }

    [Fact]
    public void Vue_ai_settings_exposes_texture_image_translation_settings()
    {
        var aiPageSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.ControlPanel", "src", "pages", "AiSettingsPage.vue"));
        var storeSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.ControlPanel", "src", "state", "controlPanelStore.ts"));
        var apiTypesSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.ControlPanel", "src", "types", "api.ts"));

        apiTypesSource.Should().Contain("export interface TextureImageProviderProfileState");
        apiTypesSource.Should().Contain("TextureImageProviderProfiles: TextureImageProviderProfileState[] | null;");
        apiTypesSource.Should().Contain("TextureImageProviderProfileImportResult");
        storeSource.Should().NotContain("/api/texture-image/key");
        aiPageSource.Should().Contain("textureImageProfileManager");
        aiPageSource.Should().Contain("/api/texture-image-profiles");
        aiPageSource.Should().Contain("openTextureImageProfileEditor");
        aiPageSource.Should().Contain("textureImageProfileEditorOpen");
        aiPageSource.Should().Contain("accept=\".huttextureimage\"");
        aiPageSource.Should().Contain("id=\"textureImageProfileApiKey\"");
        aiPageSource.Should().NotContain("defaultTextureImageConfig");
        aiPageSource.Should().NotContain("TextureImageTranslation: buildTextureImageConfig()");
        aiPageSource.Should().NotContain("id=\"textureImageBaseUrl\"");
        aiPageSource.Should().NotContain("id=\"textureImageApiKey\"");
    }

    [Fact]
    public void Vue_ai_settings_orders_provider_and_texture_configuration_cards()
    {
        var aiPageSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.ControlPanel", "src", "pages", "AiSettingsPage.vue"));

        aiPageSource.Should().Contain("SectionPanel title=\"AI翻译配置\"");
        aiPageSource.Should().Contain("SectionPanel title=\"贴图翻译配置\"");
        aiPageSource.Should().NotContain("SectionPanel title=\"服务商配置\"");
        aiPageSource.Should().NotContain("SectionPanel title=\"贴图文字翻译\"");
        aiPageSource.IndexOf("SectionPanel title=\"AI翻译配置\"", StringComparison.Ordinal)
            .Should().BeLessThan(aiPageSource.IndexOf("SectionPanel title=\"贴图翻译配置\"", StringComparison.Ordinal));
    }

    [Fact]
    public void Vue_translation_context_controls_belong_to_ai_settings()
    {
        var pluginPageSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.ControlPanel", "src", "pages", "PluginSettingsPage.vue"));
        var aiPageSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.ControlPanel", "src", "pages", "AiSettingsPage.vue"));
        var cssSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.ControlPanel", "src", "styles", "app.css"));

        aiPageSource.Should().Contain("id=\"enableTranslationContext\"");
        aiPageSource.Should().Contain("id=\"translationContextMaxExamples\"");
        aiPageSource.Should().Contain("id=\"translationContextMaxCharacters\"");
        aiPageSource.Should().Contain("启用翻译上下文");
        aiPageSource.Should().Contain("上下文示例数");
        aiPageSource.Should().Contain("上下文字符数");
        aiPageSource.Should().Contain("EnableTranslationContext: form.EnableTranslationContext");
        aiPageSource.Should().Contain("TranslationContextMaxExamples: numberValue(form.TranslationContextMaxExamples)");
        aiPageSource.Should().Contain("TranslationContextMaxCharacters: numberValue(form.TranslationContextMaxCharacters)");
        var contextRow = Regex.Match(aiPageSource, @"<div class=""translation-context-row"">(?<body>[\s\S]*?)</div>");
        contextRow.Success.Should().BeTrue("context limits should share the toggle row instead of creating a sparse second grid");
        contextRow.Groups["body"].Value.Should().Contain("id=\"enableTranslationContext\"");
        contextRow.Groups["body"].Value.Should().Contain("id=\"translationContextMaxExamples\"");
        contextRow.Groups["body"].Value.Should().Contain("id=\"translationContextMaxCharacters\"");
        CssBlock(cssSource, @"\.translation-context-row")
            .Should().Contain("display: flex;")
            .And.Contain("flex-wrap: wrap;")
            .And.Contain("gap: 10px;");

        pluginPageSource.Should().NotContain("id=\"enableTranslationContext\"");
        pluginPageSource.Should().NotContain("id=\"translationContextMaxExamples\"");
        pluginPageSource.Should().NotContain("id=\"translationContextMaxCharacters\"");
    }

    [Fact]
    public void Vue_glossary_controls_only_live_on_glossary_page()
    {
        var aiPageSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.ControlPanel", "src", "pages", "AiSettingsPage.vue"));
        var glossaryPageSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.ControlPanel", "src", "pages", "GlossaryPage.vue"));

        glossaryPageSource.Should().Contain("id=\"enableGlossary\"");
        glossaryPageSource.Should().Contain("id=\"enableAutoTermExtraction\"");
        glossaryPageSource.Should().Contain("EnableAutoTermExtraction: settings.EnableAutoTermExtraction");
        glossaryPageSource.Should().Contain("GlossaryMaxTerms: Number(settings.GlossaryMaxTerms)");
        glossaryPageSource.Should().Contain("GlossaryMaxCharacters: Number(settings.GlossaryMaxCharacters)");

        aiPageSource.Should().NotContain("id=\"enableGlossary\"");
        aiPageSource.Should().NotContain("id=\"enableAutoTermExtraction\"");
        aiPageSource.Should().NotContain("id=\"glossaryMaxTerms\"");
        aiPageSource.Should().NotContain("id=\"glossaryMaxCharacters\"");
        aiPageSource.Should().NotContain("EnableGlossary: form.EnableGlossary");
        aiPageSource.Should().NotContain("EnableAutoTermExtraction: form.EnableAutoTermExtraction");
        aiPageSource.Should().NotContain("GlossaryMaxTerms: numberValue(form.GlossaryMaxTerms)");
        aiPageSource.Should().NotContain("GlossaryMaxCharacters: numberValue(form.GlossaryMaxCharacters)");
    }

    [Fact]
    public void Vue_ai_settings_keeps_section_panels_visually_separated()
    {
        var aiPageSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.ControlPanel", "src", "pages", "AiSettingsPage.vue"));
        var cssSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.ControlPanel", "src", "styles", "app.css"));

        aiPageSource.Should().Contain("class=\"settings-stack ai-settings-stack\"");
        CssBlock(cssSource, @"\.settings-stack")
            .Should().Contain("display: grid;")
            .And.Contain("gap: 14px;");
    }

    [Fact]
    public void Vue_ai_settings_uses_language_name_dropdown_with_code_values()
    {
        var aiPageSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.ControlPanel", "src", "pages", "AiSettingsPage.vue"));
        var languageSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.ControlPanel", "src", "utils", "languages.ts"));

        aiPageSource.Should().Contain("import { targetLanguageOptions } from \"../utils/languages\"");
        languageSource.Should().Contain("export const targetLanguageOptions");
        languageSource.Should().Contain("{ value: \"zh-Hans\", label: \"简体中文\" }");
        languageSource.Should().Contain("{ value: \"zh-Hant\", label: \"繁体中文\" }");
        languageSource.Should().Contain("{ value: \"ja\", label: \"日语\" }");
        languageSource.Should().Contain("{ value: \"ko\", label: \"韩语\" }");
        languageSource.Should().Contain("{ value: \"pt-BR\", label: \"巴西葡萄牙语\" }");
        aiPageSource.Should().Contain("<select id=\"targetLanguage\" v-model=\"form.TargetLanguage\" @change=\"markDirty\">");
        aiPageSource.Should().Contain("v-for=\"option in targetLanguageOptions\"");
        aiPageSource.Should().NotContain("id=\"targetLanguage\" v-model=\"form.TargetLanguage\" autocomplete=\"off\"");
    }

    [Fact]
    public void Vue_ai_settings_hides_profile_dependent_options_when_provider_kind_changes()
    {
        var aiPageSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.ControlPanel", "src", "pages", "AiSettingsPage.vue"));

        aiPageSource.Should().Contain("const isProfileOpenAi");
        aiPageSource.Should().Contain("const isProfileDeepSeek");
        aiPageSource.Should().Contain("const isProfileOpenAiCompatible");
        aiPageSource.Should().Contain("v-if=\"form.EnableTranslationContext\"");
        aiPageSource.Should().Contain("v-if=\"isProfileOpenAi\"");
        aiPageSource.Should().Contain("v-if=\"isProfileDeepSeek\"");
        aiPageSource.Should().Contain("v-if=\"isProfileDeepSeek || isProfileOpenAiCompatible\"");
        aiPageSource.Should().NotContain("EnableOpenAiReasoning");
        aiPageSource.Should().Contain("providerProfileDeepSeekReasoningEffort");
    }

    [Fact]
    public void Vue_ai_settings_exposes_openai_compatible_advanced_options()
    {
        var aiPageSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.ControlPanel", "src", "pages", "AiSettingsPage.vue"));
        var cssSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.ControlPanel", "src", "styles", "app.css"));
        var apiTypesSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.ControlPanel", "src", "types", "api.ts"));

        apiTypesSource.Should().Contain("OpenAICompatibleCustomHeaders: string | null;");
        apiTypesSource.Should().Contain("OpenAICompatibleExtraBodyJson: string | null;");
        apiTypesSource.Should().Contain("OpenAICompatibleCustomHeaders?: string | null;");
        apiTypesSource.Should().Contain("OpenAICompatibleExtraBodyJson?: string | null;");
        aiPageSource.Should().Contain("OpenAICompatibleCustomHeaders: \"\"");
        aiPageSource.Should().Contain("OpenAICompatibleExtraBodyJson: \"\"");
        aiPageSource.Should().Contain("const isProfileOpenAiCompatible");
        aiPageSource.Should().Contain("v-if=\"isProfileOpenAiCompatible\"");
        aiPageSource.Should().Contain("id=\"providerProfileCustomHeaders\"");
        aiPageSource.Should().Contain("id=\"providerProfileExtraBodyJson\"");
        aiPageSource.Should().Contain("OpenAICompatibleCustomHeaders: profileForm.OpenAICompatibleCustomHeaders.trim() || null");
        aiPageSource.Should().Contain("OpenAICompatibleExtraBodyJson: profileForm.OpenAICompatibleExtraBodyJson.trim() || null");
        aiPageSource.Should().Contain("Authorization 和 Content-Type 由插件维护。");
        cssSource.Should().Contain(".ai-compatible-advanced");
        cssSource.Should().Contain(".textarea-field");
    }

    [Fact]
    public void Vue_plugin_settings_and_api_types_do_not_expose_cache_retention()
    {
        var pluginPageSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.ControlPanel", "src", "pages", "PluginSettingsPage.vue"));
        var apiTypesSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.ControlPanel", "src", "types", "api.ts"));

        pluginPageSource.Should().NotContain("CacheRetentionDays");
        pluginPageSource.Should().NotContain("cacheRetentionDays");
        apiTypesSource.Should().NotContain("CacheRetentionDays");
    }

    [Fact]
    public void Vue_status_metric_help_tooltips_stack_above_neighboring_cards()
    {
        var cssSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.ControlPanel", "src", "styles", "app.css"));
        var hoverBlock = System.Text.RegularExpressions.Regex.Match(cssSource, @"\.metric:hover,\s*\.metric:focus-visible\s*\{[^}]*\}", System.Text.RegularExpressions.RegexOptions.Singleline);

        hoverBlock.Success.Should().BeTrue();
        hoverBlock.Value.Should().Contain("z-index: 35;");
        cssSource.Should().Contain(".metric[data-help]::after");
    }

    [Fact]
    public void Vue_settings_pages_use_shared_help_tooltips_for_major_options()
    {
        var cssSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.ControlPanel", "src", "styles", "app.css"));
        var pluginPageSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.ControlPanel", "src", "pages", "PluginSettingsPage.vue"));
        var aiPageSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.ControlPanel", "src", "pages", "AiSettingsPage.vue"));
        var glossaryPageSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.ControlPanel", "src", "pages", "GlossaryPage.vue"));

        cssSource.Should().Contain(".help-target[data-help]::after");
        cssSource.Should().Contain(".help-target:hover::after");
        cssSource.Should().Contain(".help-target:focus-within::after");
        cssSource.Should().Contain(".help-target:hover");

        pluginPageSource.Should().Contain("class=\"check help-target\"");
        pluginPageSource.Should().Contain("暂停采集、翻译和写回，已保存的配置和缓存不会被删除。");
        pluginPageSource.Should().Contain("控制插件多久扫描一次 Unity 文本，数值越小越及时但更耗性能。");
        pluginPageSource.Should().Contain("点击后直接监听组合键，按 Backspace 或 Delete 可清空为 None。");

        aiPageSource.Should().Contain("class=\"field help-target\"");
        aiPageSource.Should().Contain("服务商配置按优先级执行");
        aiPageSource.Should().Contain("此配置可同时执行的在线请求数。");
        aiPageSource.Should().Contain("help: \"控制模型的总体翻译规则");
        aiPageSource.Should().Contain(":data-help=\"field.help\"");

        glossaryPageSource.Should().Contain("class=\"check help-target\"");
        glossaryPageSource.Should().Contain("把匹配到的术语作为强制译名写入提示词，手动术语优先。");
        glossaryPageSource.Should().Contain("限制每次请求最多注入多少条术语，设为 0 等于不注入术语。");
    }

    [Fact]
    public void Vue_section_help_tooltips_raise_panel_above_later_cards()
    {
        var cssSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.ControlPanel", "src", "styles", "app.css"));
        var panelBlock = System.Text.RegularExpressions.Regex.Match(cssSource, @"\.section-panel\s*\{[^}]*\}", System.Text.RegularExpressions.RegexOptions.Singleline);
        var hoverBlock = System.Text.RegularExpressions.Regex.Match(cssSource, @"\.section-panel:hover,\s*\.section-panel:focus-within\s*\{[^}]*\}", System.Text.RegularExpressions.RegexOptions.Singleline);

        panelBlock.Success.Should().BeTrue();
        panelBlock.Value.Should().Contain("position: relative;");
        hoverBlock.Success.Should().BeTrue();
        hoverBlock.Value.Should().Contain("z-index: 45;");
    }

    [Fact]
    public void Vue_status_and_settings_surfaces_keep_structural_icons_without_plugin_checkbox_icons()
    {
        var statusPageSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.ControlPanel", "src", "pages", "StatusPage.vue"));
        var pluginPageSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.ControlPanel", "src", "pages", "PluginSettingsPage.vue"));
        var aiPageSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.ControlPanel", "src", "pages", "AiSettingsPage.vue"));
        var glossaryPageSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.ControlPanel", "src", "pages", "GlossaryPage.vue"));
        var sectionPanelSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.ControlPanel", "src", "components", "SectionPanel.vue"));
        var metricCardSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.ControlPanel", "src", "components", "MetricCard.vue"));
        var cssSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.ControlPanel", "src", "styles", "app.css"));

        sectionPanelSource.Should().Contain("icon?: Component");
        sectionPanelSource.Should().Contain("class=\"section-icon\"");
        sectionPanelSource.Should().Contain("class=\"section-actions\"");
        metricCardSource.Should().Contain("icon?: Component");
        metricCardSource.Should().Contain("class=\"metric-icon\"");
        statusPageSource.Should().Contain(":icon=\"Activity\"");
        statusPageSource.Should().Contain(":icon=\"Bot\"");
        pluginPageSource.Should().Contain("from \"lucide-vue-next\"");
        pluginPageSource.Should().NotContain("class=\"option-icon\"");
        aiPageSource.Should().Contain("from \"lucide-vue-next\"");
        aiPageSource.Should().Contain("class=\"field-label-icon\"");
        aiPageSource.Should().Contain("class=\"provider-profile-card\"");
        aiPageSource.Should().Contain("class=\"secondary icon-button\"");
        glossaryPageSource.Should().Contain("class=\"option-icon\"");
        cssSource.Should().Contain(".option-icon");
        cssSource.Should().Contain(".field-label-icon");
        cssSource.Should().Contain(".section-actions");
    }

    [Fact]
    public void Vue_sidebar_uses_icons_titles_and_collapsed_mode_without_captions()
    {
        var sidebarSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.ControlPanel", "src", "components", "AppSidebar.vue"));
        var cssSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.ControlPanel", "src", "styles", "app.css"));

        sidebarSource.Should().Contain("lucide-vue-next");
        sidebarSource.Should().Contain("collapsedStorageKey");
        sidebarSource.Should().Contain("localStorage");
        sidebarSource.Should().Contain("nav-icon");
        sidebarSource.Should().Contain(":title=\"page.label\"");
        sidebarSource.Should().Contain("<strong>HUnity</strong>");
        sidebarSource.Should().Contain("class=\"brand-copy\"");
        sidebarSource.Should().NotContain("HUnityAutoTranslator</strong>");
        sidebarSource.Should().NotContain("本机控制面板");
        sidebarSource.Should().NotContain("caption:");
        sidebarSource.Should().NotContain("<small>{{ page.caption }}</small>");
        cssSource.Should().Contain(".app-shell.sidebar-collapsed");
        cssSource.Should().Contain("grid-template-columns: 220px minmax(0, 1fr);");
        cssSource.Should().Contain("grid-template-columns: 64px minmax(0, 1fr);");
        cssSource.Should().Contain(".sidebar-collapse");
        cssSource.Should().Contain(".nav-icon");
    }

    [Fact]
    public void Vue_shared_layout_constrains_pages_to_browser_width()
    {
        var cssSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.ControlPanel", "src", "styles", "app.css"));

        CssBlock(cssSource, @"\.app-shell").Should().Contain("width: 100%;").And.Contain("min-width: 0;");
        CssBlock(cssSource, @"\.workspace").Should().Contain("max-width: 100%;").And.Contain("overflow-x: clip;");
        CssBlock(cssSource, @"\.workspace-main").Should().Contain("max-width: 100%;");
        CssBlock(cssSource, @"\.page").Should().Contain("width: 100%;").And.Contain("max-width: min(1500px, 100%);");
        CssBlock(cssSource, @"\.page-head").Should().Contain("min-width: 0;").And.Contain("flex-wrap: wrap;");
        CssBlock(cssSource, @"\.section-panel").Should().Contain("min-width: 0;").And.Contain("max-width: 100%;");
        CssBlock(cssSource, @"\.section-head").Should().Contain("min-width: 0;").And.Contain("flex-wrap: wrap;");
        CssBlock(cssSource, @"\.section-head\s*>\s*div:first-child").Should().Contain("min-width: 0;");
        CssBlock(cssSource, @"\.actions").Should().Contain("min-width: 0;");

        CssBlock(cssSource, @"\.metric-grid,\s*\.about-grid").Should().Contain("repeat(auto-fit, minmax(min(180px, 100%), 1fr))");
        CssBlock(cssSource, @"\.provider-summary").Should().Contain("repeat(auto-fit, minmax(min(180px, 100%), 1fr))");
        CssBlock(cssSource, @"\.form-grid\.four").Should().Contain("repeat(auto-fit, minmax(min(180px, 100%), 1fr))");
        CssBlock(cssSource, @"\.ai-provider-grid,\s*\.ai-endpoint-grid").Should().Contain("repeat(auto-fit, minmax(min(190px, 100%), 1fr))");
        CssBlock(cssSource, @"\.ai-model-row").Should().Contain("repeat(auto-fit, minmax(min(240px, 100%), 1fr))");
        CssBlock(cssSource, @"\.llama-status-strip").Should().Contain("repeat(auto-fit, minmax(min(180px, 100%), 1fr))");
        CssBlock(cssSource, @"\.llama-run-row").Should().Contain("repeat(auto-fit, minmax(min(150px, 100%), 1fr))");
        CssBlock(cssSource, @"\.editor-tools").Should().Contain("repeat(auto-fit, minmax(min(260px, 100%), 1fr))");

        CssBlock(cssSource, @"\.metric\[data-help\]::after,\s*\.help-target\[data-help\]::after")
            .Should().Contain("width: min(360px, 100%);").And.NotContain("width: max-content;");
    }

    [Fact]
    public void Vue_sidebar_uses_blue_white_brand_icon_and_keeps_white_blue_asset()
    {
        var sidebarSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.ControlPanel", "src", "components", "AppSidebar.vue"));
        var cssSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.ControlPanel", "src", "styles", "app.css"));
        var indexSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.ControlPanel", "index.html"));
        var root = FindRepositoryRoot();
        var brandingRoot = Path.Combine(root, "src", "HUnityAutoTranslator.ControlPanel", "public", "branding");

        sidebarSource.Should().Contain("hunityIconBlueWhite");
        sidebarSource.Should().Contain("const brandIcon = hunityIconBlueWhite;");
        sidebarSource.Should().NotContain("hunityIconWhiteBlue");
        sidebarSource.Should().NotContain("effectiveTheme");
        sidebarSource.Should().Contain("class=\"brand-logo\"");
        cssSource.Should().Contain(".brand-logo");
        cssSource.Should().Contain(".sidebar.collapsed .brand");
        indexSource.Should().Contain("rel=\"icon\"");
        indexSource.Should().Contain("./branding/hunity-icon-blue-white.ico");

        File.Exists(Path.Combine(root, "src", "HUnityAutoTranslator.ControlPanel", "scripts", "branding-preview-source.png")).Should().BeTrue();
        File.Exists(Path.Combine(root, "src", "HUnityAutoTranslator.ControlPanel", "scripts", "generate_brand_icons.py")).Should().BeTrue();
        File.Exists(Path.Combine(brandingRoot, "hunity-icon-blue-white.ico")).Should().BeTrue();
        File.Exists(Path.Combine(brandingRoot, "hunity-icon-white-blue.ico")).Should().BeTrue();
        File.Exists(Path.Combine(brandingRoot, "hunity-icon-blue-white.png")).Should().BeTrue();
        File.Exists(Path.Combine(brandingRoot, "hunity-icon-white-blue.png")).Should().BeTrue();
        Directory.GetFiles(brandingRoot, "hunity-icon-blue-white-*.png").Should().HaveCount(8);
        Directory.GetFiles(brandingRoot, "hunity-icon-white-blue-*.png").Should().HaveCount(8);
    }

    [Fact]
    public void Vue_sidebar_aligns_collapsed_controls_and_switches_theme_icon_by_state()
    {
        var sidebarSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.ControlPanel", "src", "components", "AppSidebar.vue"));
        var cssSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.ControlPanel", "src", "styles", "app.css"));

        sidebarSource.Should().Contain("const collapsedControlSize = 40;");
        sidebarSource.Should().Contain("const themeIcon");
        sidebarSource.Should().Contain("MonitorCog");
        sidebarSource.Should().Contain("Sun");
        sidebarSource.Should().Contain("Moon");
        sidebarSource.Should().Contain("<component v-if=\"collapsed\" :is=\"themeIcon\" class=\"nav-icon\" />");
        cssSource.Should().Contain("--collapsed-control-size");
        cssSource.Should().Contain(".sidebar.collapsed .connection");
        cssSource.Should().Contain("justify-content: center;");
        cssSource.Should().Contain("text-align: center;");
        cssSource.Should().Contain(".sidebar.collapsed .sidebar-collapse");
        cssSource.Should().Contain(".sidebar.collapsed .theme-cycle");
    }

    [Fact]
    public void Vue_plugin_settings_expose_runtime_hotkey_and_font_controls()
    {
        var pluginPageSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.ControlPanel", "src", "pages", "PluginSettingsPage.vue"));

        pluginPageSource.Should().Contain("id=\"openControlPanelHotkey\"");
        pluginPageSource.Should().Contain("id=\"toggleTranslationHotkey\"");
        pluginPageSource.Should().Contain("id=\"forceScanHotkey\"");
        pluginPageSource.Should().Contain("id=\"toggleFontHotkey\"");
        pluginPageSource.Should().Contain("readonly");
        pluginPageSource.Should().Contain("function beginHotkeyCapture");
        pluginPageSource.Should().Contain("function handleHotkeyKeydown(event: KeyboardEvent, field: HotkeyField)");
        pluginPageSource.Should().Contain("function normalizeCapturedHotkey");
        pluginPageSource.Should().Contain("markDirty();");
        pluginPageSource.Should().Contain("id=\"enableFontReplacement\"");
        pluginPageSource.Should().Contain("id=\"replacementFontName\"");
        pluginPageSource.Should().Contain("id=\"replacementFontFile\"");
        pluginPageSource.Should().Contain("ReplacementFontName: form.ReplacementFontName");
        pluginPageSource.Should().Contain("ReplacementFontFile: form.ReplacementFontFile");
    }

    [Fact]
    public void Vue_plugin_settings_groups_font_size_adjustment_as_flat_conditional_controls()
    {
        var pluginPageSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.ControlPanel", "src", "pages", "PluginSettingsPage.vue"));
        var cssSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.ControlPanel", "src", "styles", "app.css"));

        pluginPageSource.Should().Contain("const fontSizeAdjustmentEnabled = computed");
        pluginPageSource.Should().Contain("id=\"fontSizeAdjustmentEnabled\"");
        pluginPageSource.Should().Contain("v-if=\"fontSizeAdjustmentEnabled\"");
        pluginPageSource.Should().Contain("id=\"fontSizeAdjustmentValue\"");
        pluginPageSource.Should().Contain("id=\"fontSizeAdjustmentModePercent\"");
        pluginPageSource.Should().Contain("id=\"fontSizeAdjustmentModePoints\"");
        pluginPageSource.Should().NotContain("<select id=\"fontSizeAdjustmentMode\"");
        pluginPageSource.Should().NotContain("class=\"font-size-adjustment-row\"");
        cssSource.Should().NotContain(".font-size-adjustment-row");

        Regex.Match(
            pluginPageSource,
            @"class=""font-size-settings"">[\s\S]*id=""fontSamplingPointSize""[\s\S]*id=""fontSizeAdjustmentEnabled""[\s\S]*v-if=""fontSizeAdjustmentEnabled""[\s\S]*id=""fontSizeAdjustmentValue""[\s\S]*v-if=""fontSizeAdjustmentEnabled""[\s\S]*class=""segmented-control font-size-mode-control""[\s\S]*id=""fontSizeAdjustmentModePercent""[\s\S]*id=""fontSizeAdjustmentModePoints""")
            .Success.Should().BeTrue("font size controls should stay in one flat grid without a full-row spacer");

        CssBlock(cssSource, @"\.font-size-settings").Should()
            .Contain("display: grid;")
            .And.Contain("repeat(auto-fit, minmax(min(220px, 100%), 1fr))")
            .And.Contain("align-items: end;");
        CssBlock(cssSource, @"\.segmented-control").Should()
            .Contain("display: inline-grid;")
            .And.Contain("grid-template-columns: repeat(2, minmax(0, 1fr));");
        CssBlock(cssSource, @"\.segmented-control input").Should().Contain("position: absolute;");
    }

    [Fact]
    public void Vue_plugin_settings_can_pick_font_file_and_fill_name_before_save()
    {
        var storeSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.ControlPanel", "src", "state", "controlPanelStore.ts"));
        var apiTypesSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.ControlPanel", "src", "types", "api.ts"));
        var pluginPageSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.ControlPanel", "src", "pages", "PluginSettingsPage.vue"));

        apiTypesSource.Should().Contain("export type FontPickStatus = \"selected\" | \"cancelled\" | \"unsupported\" | \"error\";");
        apiTypesSource.Should().Contain("export interface FontPickResult");
        apiTypesSource.Should().Contain("export interface FontPickOptions");
        storeSource.Should().Contain("export async function pickFontFile(options: FontPickOptions = {})");
        storeSource.Should().Contain("api<FontPickResult>(\"/api/fonts/pick\", { method: \"POST\", body: options })");
        pluginPageSource.Should().Contain("async function pickReplacementFontFile()");
        pluginPageSource.Should().Contain("form.ReplacementFontFile = result.FilePath;");
        pluginPageSource.Should().Contain("form.ReplacementFontName = result.FontName ?? \"\";");
        pluginPageSource.Should().Contain("id=\"pickReplacementFontFile\"");
        pluginPageSource.Should().Contain("id=\"automaticReplacementFontSummary\"");
        pluginPageSource.Should().Contain("automatic-font-summary");
        pluginPageSource.Should().Contain("automaticFontSummary");
    }

    [Fact]
    public void Vue_ai_settings_manage_provider_profile_crud_and_utility_actions()
    {
        var aiPageSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.ControlPanel", "src", "pages", "AiSettingsPage.vue"));

        aiPageSource.Should().Contain("async function saveGlobalConfig(options: SaveBehavior = {})");
        aiPageSource.Should().Contain("async function createProviderProfile(options: ProviderProfileSaveBehavior = {}): Promise<void>");
        aiPageSource.Should().Contain("async function saveProviderProfile(options: ProviderProfileSaveBehavior = {}): Promise<void>");
        aiPageSource.Should().Contain("async function deleteProviderProfile(profile: ProviderProfileState): Promise<void>");
        aiPageSource.Should().Contain("async function moveProviderProfile(profile: ProviderProfileState, direction: -1 | 1): Promise<void>");
        aiPageSource.Should().Contain("async function exportProviderProfile(profile: ProviderProfileState): Promise<void>");
        aiPageSource.Should().Contain("async function importProviderProfile(event: Event): Promise<void>");
        aiPageSource.Should().Contain("async function runProfileUtility<T extends { Succeeded?: boolean }>");
        aiPageSource.Should().Contain("const profileModelOptions = ref<ProviderModelInfo[]>([]);");
        aiPageSource.Should().Contain("profileModelOptions.value = result.Models;");
        aiPageSource.Should().Contain("v-if=\"profileModelOptions.length\"");
        aiPageSource.Should().Contain("const providerEditorOpen = ref(false);");
        aiPageSource.Should().Contain("function openProviderProfileEditor(profile: ProviderProfileState): void");
        aiPageSource.Should().Contain("function openNewProviderProfile(): void");
        aiPageSource.Should().Contain("function closeProviderProfileEditor(): void");
        aiPageSource.Should().Contain("/api/provider-profiles");
        aiPageSource.Should().Contain("/api/provider-profiles/draft/");
        aiPageSource.Should().Contain("/test");
        aiPageSource.Should().Contain("/models");
        aiPageSource.Should().Contain("/balance");
        aiPageSource.Should().NotContain("saveUseOnlineProfiles");
        aiPageSource.Should().NotContain("createLlamaCppProfile");
        aiPageSource.Should().NotContain("openNewProviderProfile(0)");
        aiPageSource.Should().NotContain("openNewProviderProfile(3)");
        aiPageSource.Should().NotContain("savePendingApiKey");
    }

    [Fact]
    public void Vue_ai_provider_profile_utilities_use_unsaved_draft_form()
    {
        var aiPageSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.ControlPanel", "src", "pages", "AiSettingsPage.vue"));

        aiPageSource.Should().Contain("function shouldReplaceProfileDefaultName");
        aiPageSource.Should().Contain("profileForm.Name = shouldReplaceProfileDefaultName(currentName) ? defaults.name : currentName;");
        aiPageSource.Should().Contain("function buildProviderProfileUtilityPath(action: \"test\" | \"models\" | \"balance\"): string");
        aiPageSource.Should().Contain("return `/api/provider-profiles/draft/${action}`;");
        aiPageSource.Should().Contain("body: buildProviderProfileRequest()");
        aiPageSource.Should().Contain("function canApplyProviderSelectionFromState(): boolean");
        aiPageSource.Should().Contain("return !providerEditorOpen.value || !profileDirty.value;");
        aiPageSource.Should().Contain("if (canApplyProviderSelectionFromState())");
        aiPageSource.Should().NotContain(":disabled=\"utilityBusy || !profileForm.Id\" @click=\"testProfile\"");
        aiPageSource.Should().NotContain(":disabled=\"utilityBusy || !profileForm.Id\" @click=\"fetchProfileModels\"");
        aiPageSource.Should().NotContain(":disabled=\"utilityBusy || !profileForm.Id\" @click=\"fetchProfileBalance\"");
    }

    [Fact]
    public void Vue_ai_settings_exposes_game_title_override_and_profile_temperature()
    {
        var aiPageSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.ControlPanel", "src", "pages", "AiSettingsPage.vue"));
        var apiTypesSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.ControlPanel", "src", "types", "api.ts"));

        apiTypesSource.Should().Contain("GameTitle: string | null;");
        apiTypesSource.Should().Contain("AutomaticGameTitle: string | null;");
        apiTypesSource.Should().Contain("GameTitle?: string | null;");
        aiPageSource.Should().Contain("Gamepad2");
        aiPageSource.Should().Contain("GameTitle: \"\"");
        aiPageSource.Should().Contain("const automaticGameTitle");
        aiPageSource.Should().Contain("form.GameTitle = state.GameTitle ?? \"\";");
        aiPageSource.Should().Contain("GameTitle: form.GameTitle.trim()");
        aiPageSource.Should().Contain("id=\"gameTitle\"");
        aiPageSource.Should().Contain(":placeholder=\"automaticGameTitle ||");
        aiPageSource.Should().Contain("id=\"providerProfileTemperature\"");
        aiPageSource.Should().Contain("setProfileTemperatureFromRange");
        aiPageSource.Should().Contain("formatTemperatureValue");
        aiPageSource.Should().Contain("v-if=\"isProfileDeepSeek || isProfileOpenAiCompatible\"");
        aiPageSource.Should().Contain("Temperature: profileForm.Temperature === \"\" ? null : Number(profileForm.Temperature)");
    }

    [Fact]
    public void Vue_ai_prompt_editor_edits_templates_without_duplicate_default_prompt_preview()
    {
        var aiPageSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.ControlPanel", "src", "pages", "AiSettingsPage.vue"));
        var apiTypesSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.ControlPanel", "src", "types", "api.ts"));

        apiTypesSource.Should().Contain("DefaultSystemPrompt: string;");
        apiTypesSource.Should().Contain("PromptTemplates: PromptTemplateConfig;");
        apiTypesSource.Should().Contain("DefaultPromptTemplates: PromptTemplateConfig;");
        apiTypesSource.Should().NotContain("CustomInstruction");
        aiPageSource.Should().Contain("applyPromptTemplates(state.PromptTemplates, state.DefaultPromptTemplates);");
        aiPageSource.Should().Contain("promptTemplateFields");
        aiPageSource.Should().Contain("activePromptTemplateKey");
        aiPageSource.Should().Contain("id=\"customPrompt\"");
        aiPageSource.Should().Contain("v-model=\"activePromptTemplateText\"");
        aiPageSource.Should().Contain("id=\"restorePromptTemplate\"");
        aiPageSource.Should().Contain("id=\"restoreAllPromptTemplates\"");
        aiPageSource.Should().Contain("function restoreDefaultPrompt");
        aiPageSource.Should().Contain("function validatePromptTemplates");
        aiPageSource.Should().Contain("function normalizePrompt(value: string | null | undefined): string");
        aiPageSource.Should().Contain("promptModeText");
        aiPageSource.Should().Contain("class=\"prompt-editor-head\"");
        aiPageSource.Should().Contain("class=\"prompt-template-tabs\"");
        aiPageSource.Should().Contain("class=\"prompt-editor-field\"");
        aiPageSource.Should().Contain("正在使用内置提示词");
        aiPageSource.Should().Contain("恢复全部内置");
        aiPageSource.Should().NotContain("<p class=\"hint\">{{ defaultSystemPrompt }}</p>");
        aiPageSource.Should().NotContain("defaultSystemPrompt");
        aiPageSource.Should().NotContain("完整提示词");
        aiPageSource.Should().NotContain("customInstruction");
        aiPageSource.Should().NotContain("CustomInstruction");
    }

    [Fact]
    public void Vue_ai_provider_feedback_uses_top_toasts_without_legacy_inline_output()
    {
        var aiPageSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.ControlPanel", "src", "pages", "AiSettingsPage.vue"));
        var toastSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.ControlPanel", "src", "components", "ToastHost.vue"));

        aiPageSource.Should().Contain("showToast(");
        aiPageSource.Should().Contain("async function fetchProfileModels()");
        aiPageSource.Should().Contain("async function fetchProfileBalance()");
        aiPageSource.Should().Contain("async function testProfile()");
        toastSource.Should().Contain("class=\"toast-host\"");
        aiPageSource.Should().NotContain("providerStatusText");
        aiPageSource.Should().NotContain("providerUtilityMessage");
        aiPageSource.Should().NotContain("providerUtilityOutput");
        aiPageSource.Should().NotContain("utility-results");
        aiPageSource.Should().NotContain("providerModels.length");
    }

    [Fact]
    public void Vue_ai_balance_toast_includes_returned_balance_details()
    {
        var aiPageSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.ControlPanel", "src", "pages", "AiSettingsPage.vue"));

        aiPageSource.Should().Contain("function formatBalanceToast(result: ProviderBalanceResult): string");
        aiPageSource.Should().Contain("formatBalanceToast);");
        aiPageSource.Should().Contain("balance.TotalBalance");
        aiPageSource.Should().Contain("balance.GrantedBalance");
        aiPageSource.Should().Contain("balance.ToppedUpBalance");
        aiPageSource.Should().Contain("查询余额");
        aiPageSource.Should().NotContain("查询余额/成本</button>");
    }

    [Fact]
    public void Vue_deepseek_profile_defaults_use_current_v4_model_id()
    {
        var aiPageSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.ControlPanel", "src", "pages", "AiSettingsPage.vue"));

        aiPageSource.Should().Contain("model: \"deepseek-v4-flash\"");
        aiPageSource.Should().Contain("1: { name: \"DeepSeek\"");
        aiPageSource.Should().Contain("applyProfileKindDefaults");
    }

    [Fact]
    public void Vue_ai_settings_expose_llamacpp_install_status_without_manual_port()
    {
        var aiPageSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.ControlPanel", "src", "pages", "AiSettingsPage.vue"));
        var apiTypesSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.ControlPanel", "src", "types", "api.ts"));

        apiTypesSource.Should().Contain("export interface LlamaCppConfig");
        apiTypesSource.Should().Contain("export interface LlamaCppServerStatus");
        apiTypesSource.Should().Contain("BatchSize: number;");
        apiTypesSource.Should().Contain("UBatchSize: number;");
        apiTypesSource.Should().Contain("FlashAttentionMode: string;");
        apiTypesSource.Should().Contain("AutoStartOnStartup: boolean;");
        apiTypesSource.Should().Contain("export interface LlamaCppBenchmarkResult");
        apiTypesSource.Should().Contain("Installed: boolean");
        apiTypesSource.Should().Contain("Release: string | null");
        apiTypesSource.Should().Contain("Variant: string | null");
        apiTypesSource.Should().Contain("ServerPath: string | null");
        aiPageSource.Should().Contain("id=\"llamaCppModelPath\"");
        aiPageSource.Should().Contain("id=\"pickLlamaCppModel\"");
        aiPageSource.Should().Contain("id=\"runLlamaCppBenchmark\"");
        aiPageSource.Should().Contain("id=\"llamaCppBatchSize\"");
        aiPageSource.Should().Contain("id=\"llamaCppUBatchSize\"");
        aiPageSource.Should().Contain("id=\"llamaCppFlashAttentionMode\"");
        aiPageSource.Should().Contain("id=\"startLlamaCpp\"");
        aiPageSource.Should().Contain("id=\"stopLlamaCpp\"");
        aiPageSource.Should().Contain("llamaCppIsActive");
        aiPageSource.Should().Contain("llamaCppStatusText");
        aiPageSource.Should().Contain("stopped: \"已停止\"");
        aiPageSource.Should().Contain("running: \"运行中\"");
        aiPageSource.Should().Contain("v-if=\"!llamaCppIsActive\"");
        aiPageSource.Should().Contain("v-else id=\"stopLlamaCpp\"");
        aiPageSource.Should().Contain("llamaCppInstallText");
        aiPageSource.Should().Contain("LlamaCppAutoStartOnStartup");
        aiPageSource.Should().Contain("AutoStartOnStartup: profileForm.LlamaCppAutoStartOnStartup");
        aiPageSource.Should().Contain("profileForm.LlamaCppAutoStartOnStartup = true;");
        aiPageSource.Should().Contain("profileForm.LlamaCppAutoStartOnStartup = false;");
        aiPageSource.Should().Contain("async function ensureSavedLlamaCppProfile(): Promise<string | null>");
        aiPageSource.Should().Contain("const profileId = await ensureSavedLlamaCppProfile();");
        aiPageSource.Should().Contain("llamaCppBenchmarkButtonText");
        aiPageSource.Should().Contain("停止并运行 CUDA 基准");
        aiPageSource.Should().Contain("await stopLlamaCpp({ quiet: true });");
        aiPageSource.Should().Contain("if (llamaCppIsActive.value && !window.confirm");
        aiPageSource.Should().NotContain(":disabled=\"llamaCppBenchmarkBusy || llamaCppIsActive || !profileForm.Id\"");
        aiPageSource.Should().Contain("/api/provider-profiles/${encodeURIComponent(profileId)}/start");
        aiPageSource.Should().Contain("/api/provider-profiles/${encodeURIComponent(profileForm.Id)}/stop");
        aiPageSource.Should().Contain("/api/provider-profiles/${encodeURIComponent(profileId)}/benchmark");
        aiPageSource.Should().Contain("/api/llamacpp/model/pick");
        aiPageSource.Should().NotContain("id=\"llamaCppPort\"");
        aiPageSource.Should().NotContain("<div><span>端口</span>");
    }

    [Fact]
    public void Vue_ai_settings_exposes_llamacpp_model_preset_downloads()
    {
        var aiPageSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.ControlPanel", "src", "pages", "AiSettingsPage.vue"));
        var apiTypesSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.ControlPanel", "src", "types", "api.ts"));
        var cssSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.ControlPanel", "src", "styles", "app.css"));

        apiTypesSource.Should().Contain("export interface LlamaCppModelDownloadPreset");
        apiTypesSource.Should().Contain("export interface LlamaCppModelDownloadStatus");
        apiTypesSource.Should().Contain("PresetId: string;");
        apiTypesSource.Should().Contain("ProgressPercent: number;");
        aiPageSource.Should().Contain("llamaCppModelPresets");
        aiPageSource.Should().Contain("llamaCppSelectedPresetId");
        aiPageSource.Should().Contain("const llamaCppDownloadDialogOpen = ref(false);");
        aiPageSource.Should().Contain("function openLlamaCppDownloadDialog()");
        aiPageSource.Should().Contain("async function loadLlamaCppModelPresets()");
        aiPageSource.Should().Contain("async function downloadLlamaCppPreset()");
        aiPageSource.Should().Contain("async function cancelLlamaCppDownload()");
        aiPageSource.Should().Contain("id=\"openLlamaCppModelDownload\"");
        aiPageSource.Should().Contain("id=\"llamaCppPresetList\"");
        aiPageSource.Should().Contain("role=\"radiogroup\"");
        aiPageSource.Should().Contain("class=\"model-preset-card\"");
        aiPageSource.Should().Contain("model-preset-card-active");
        aiPageSource.Should().NotContain("<select id=\"llamaCppPreset\"");
        aiPageSource.Should().Contain("id=\"downloadLlamaCppPreset\"");
        aiPageSource.Should().Contain("id=\"cancelLlamaCppDownload\"");
        aiPageSource.Should().Contain("class=\"model-download-backdrop\"");
        aiPageSource.Should().Contain("class=\"model-download-dialog\"");
        aiPageSource.Should().Contain("/api/llamacpp/model/presets");
        aiPageSource.Should().Contain("/api/llamacpp/model/download");
        aiPageSource.Should().Contain("/api/llamacpp/model/download/cancel");
        aiPageSource.Should().Contain("profileForm.LlamaCppModelPath = status.LocalPath;");
        aiPageSource.Should().Contain("已下载并填入模型路径");
        aiPageSource.Should().Contain("模型下载");
        aiPageSource.Should().Contain("<div v-if=\"isLlamaCppDownloading\" class=\"llama-download-progress\">");
        aiPageSource.Should().Contain("function formatLlamaCppDownloadSpeed");
        aiPageSource.Should().Contain("function formatLlamaCppRemainingTime");
        aiPageSource.Should().Contain("速度 ${formatLlamaCppDownloadSpeed(status)}");
        aiPageSource.Should().Contain("剩余 ${formatLlamaCppRemainingTime(status)}");
        aiPageSource.Should().NotContain("return `${status.Message} ${size}`;");
        aiPageSource.Should().Contain("{{ preset.UseCase }}");
        aiPageSource.Should().Contain("{{ preset.Notes }}");
        aiPageSource.Should().Contain("{{ preset.License }}");
        aiPageSource.Should().Contain("model-preset-license");
        aiPageSource.Should().NotContain("兼容备选");
        aiPageSource.Should().NotContain("魔塔");
        aiPageSource.Should().NotContain("魔搭");
        aiPageSource.Should().NotContain("selectedLlamaCppPreset?.ModelScopeModelId");
        cssSource.Should().Contain(".model-preset-list");
        Regex.IsMatch(cssSource, @"(?s)\.model-preset-list\s*\{[^}]*grid-template-columns:\s*1fr;")
            .Should().BeTrue("the preset list should stay single-column at every desktop width");
        Regex.IsMatch(cssSource, @"(?s)@media \(max-width:\s*780px\)[^{]*\{[^}]*\.model-preset-list,")
            .Should().BeFalse("single-column preset layout should not depend on the mobile breakpoint");
        cssSource.Should().Contain(".model-preset-card");
        var presetCardBlock = CssBlock(cssSource, @"\.model-preset-card");
        presetCardBlock.Should().Contain("grid-template-columns: minmax(0, 1fr);");
        presetCardBlock.Should().Contain("justify-content: stretch;");
        presetCardBlock.Should().Contain("justify-items: start;");
        presetCardBlock.Should().Contain("align-content: start;");
        presetCardBlock.Should().Contain("width: 100%;");
        presetCardBlock.Should().Contain("text-align: left;");
        var presetCardContentBlock = CssBlock(cssSource, @"\.model-preset-card\s*>\s*span");
        presetCardContentBlock.Should().Contain("width: 100%;");
        presetCardContentBlock.Should().Contain("min-width: 0;");
        presetCardContentBlock.Should().Contain("max-width: 100%;");
        cssSource.Should().Contain(".model-preset-card-active");
        cssSource.Should().Contain(".model-download-backdrop");
        cssSource.Should().Contain(".model-download-dialog");
        cssSource.Should().Contain(".llama-download-progress");
    }

    [Fact]
    public void Vue_ai_settings_separates_online_profiles_from_llamacpp_runtime()
    {
        var aiPageSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.ControlPanel", "src", "pages", "AiSettingsPage.vue"));
        var apiTypesSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.ControlPanel", "src", "types", "api.ts"));

        apiTypesSource.Should().Contain("LlamaCpp: LlamaCppConfig | null;");
        aiPageSource.Should().Contain("const providerKindOptions = [");
        aiPageSource.Should().Contain("{ value: 2, label: \"OpenAI 兼容\" }");
        aiPageSource.Should().Contain("{ value: 3, label: \"llama.cpp 本地模型\" }");
        aiPageSource.Should().Contain("const hasLlamaCppProfile");
        aiPageSource.Should().Contain("const isProfileLlamaCpp");
        var profileStatusBlock = Regex.Match(
            aiPageSource,
            @"function formatProfileStatus\(profile: ProviderProfileState\): string \{(?<body>[\s\S]*?)\n\}");
        profileStatusBlock.Success.Should().BeTrue("profile cards should format local-model state separately from online cooldowns");
        profileStatusBlock.Groups["body"].Value.IndexOf("providerKindToNumber(profile.Kind) === 3", StringComparison.Ordinal)
            .Should().BeLessThan(profileStatusBlock.Groups["body"].Value.IndexOf("profile.CooldownRemainingSeconds > 0", StringComparison.Ordinal));
        aiPageSource.Should().Contain("function buildProfileLlamaCppConfig(): LlamaCppConfig");
        aiPageSource.Should().Contain("function openNewProviderProfile(): void");
        aiPageSource.Should().NotContain("async function createLlamaCppProfile(): Promise<void>");
        aiPageSource.Should().NotContain("saveUseOnlineProfiles");
        aiPageSource.Should().NotContain(":disabled=\"hasLlamaCppProfile\"");
        aiPageSource.Should().NotContain("await saveConfig(buildConfigRequest(0), formKey, { quiet: true })");
        aiPageSource.Should().NotContain("await saveConfig(buildConfigRequest(3), formKey, { quiet: true })");
        aiPageSource.Should().Contain("SectionPanel title=\"AI翻译配置\"");
        aiPageSource.Should().NotContain("SectionPanel title=\"在线服务商配置\"");
        aiPageSource.Should().NotContain("SectionPanel title=\"llama.cpp 本地模型\"");
    }

    [Fact]
    public void Vue_ai_settings_uses_compact_llamacpp_layout()
    {
        var aiPageSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.ControlPanel", "src", "pages", "AiSettingsPage.vue"));
        var cssSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.ControlPanel", "src", "styles", "app.css"));

        aiPageSource.Should().Contain("class=\"provider-profile-manager\"");
        aiPageSource.Should().Contain("class=\"provider-profile-list\"");
        aiPageSource.Should().Contain("class=\"provider-profile-editor\"");
        aiPageSource.Should().Contain("class=\"provider-card-actions\"");
        aiPageSource.Should().Contain("title=\"编辑\"");
        aiPageSource.Should().Contain("@click.stop=\"openProviderProfileEditor(profile)\"");
        aiPageSource.Should().Contain("class=\"provider-editor-backdrop\"");
        aiPageSource.Should().Contain("class=\"provider-editor-dialog\"");
        aiPageSource.Should().Contain("role=\"dialog\"");
        aiPageSource.Should().Contain("aria-labelledby=\"providerEditorTitle\"");
        aiPageSource.Should().Contain("@click.self=\"closeProviderProfileEditor\"");
        aiPageSource.Should().Contain("class=\"llama-local-panel\"");
        aiPageSource.Should().Contain("llama-model-row");
        aiPageSource.Should().Contain("class=\"llama-status-strip\"");
        aiPageSource.Should().Contain("class=\"llama-run-row\"");
        aiPageSource.Should().Contain("class=\"llama-result-card\"");
        aiPageSource.Should().Contain("id=\"llamaCppModelPath\"");
        aiPageSource.Should().Contain("id=\"pickLlamaCppModel\"");
        aiPageSource.Should().Contain("id=\"startLlamaCpp\"");
        aiPageSource.Should().Contain("id=\"stopLlamaCpp\"");
        aiPageSource.Should().NotContain("mini-summary");
        cssSource.Should().NotContain(".mini-summary");
        cssSource.Should().Contain(".provider-profile-manager");
        cssSource.Should().Contain(".provider-profile-list");
        cssSource.Should().Contain(".provider-profile-editor");
        cssSource.Should().Contain(".provider-card-actions");
        cssSource.Should().Contain(".provider-editor-backdrop");
        cssSource.Should().Contain(".provider-editor-dialog");
        CssBlock(cssSource, @"\.provider-profile-manager").Should().Contain("grid-template-columns: 1fr;");
        cssSource.Should().Contain(".llama-local-panel");
        cssSource.Should().Contain(".llama-model-row");
        cssSource.Should().Contain(".llama-status-strip");
        CssBlock(cssSource, @"\.llama-status-strip").Should().Contain("grid-template-columns: repeat(auto-fit, minmax(min(180px, 100%), 1fr));");
        cssSource.Should().NotContain("grid-template-columns: minmax(min(280px, 100%), 0.82fr) minmax(0, 1.6fr);");
        cssSource.Should().NotContain("grid-template-columns: minmax(180px, 1fr) minmax(120px, 0.7fr) minmax(260px, 1.6fr);");
        cssSource.Should().NotContain("grid-template-columns: minmax(150px, 1fr) minmax(110px, 0.7fr) minmax(100px, 0.6fr) minmax(260px, 1.55fr);");
        cssSource.Should().Contain(".llama-run-row");
        cssSource.Should().Contain(".llama-result-card");
    }

    [Fact]
    public void Vue_provider_profile_editor_uses_configuration_copy_and_adaptive_dialog_layout()
    {
        var aiPageSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.ControlPanel", "src", "pages", "AiSettingsPage.vue"));
        var cssSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.ControlPanel", "src", "styles", "app.css"));

        aiPageSource.Should().Contain("服务商配置");
        aiPageSource.Should().Contain("配置编辑器");
        aiPageSource.Should().Contain("新建配置");
        aiPageSource.Should().Contain("保存配置");
        aiPageSource.Should().NotContain("档案");

        CssBlock(cssSource, @"\.provider-editor-backdrop")
            .Should().Contain("overflow-x: clip;").And.Contain("overflow-y: auto;");
        CssBlock(cssSource, @"\.provider-editor-dialog")
            .Should().Contain("width: min(1120px, 100%);")
            .And.Contain("overflow-x: clip;")
            .And.Contain("overflow-y: auto;");
        CssBlock(cssSource, @"\.provider-editor-dialog\s+\.field\.help-target\[data-help\]::after,\s*\.provider-editor-dialog\s+\.llama-model-row\.help-target\[data-help\]::after")
            .Should().Contain("position: absolute;")
            .And.Contain("z-index: 90;");
        CssBlock(cssSource, @"\.provider-editor-dialog\s+\.field\.help-target\[data-help\]::after,\s*\.provider-editor-dialog\s+\.llama-model-row\.help-target\[data-help\]::after")
            .Should().NotContain("position: static;")
            .And.NotContain("max-height: 0;");
        cssSource.Should().Contain(".provider-editor-dialog .field.help-target[data-help]:hover::after");
        cssSource.Should().Contain(".provider-editor-dialog .llama-model-row.help-target[data-help]:focus-within::after");
    }

    [Fact]
    public void Vue_ai_defaults_disable_reasoning_and_thinking()
    {
        var aiPageSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.ControlPanel", "src", "pages", "AiSettingsPage.vue"));

        aiPageSource.Should().Contain("ReasoningEffort: \"none\"");
        aiPageSource.Should().Contain("DeepSeekThinkingMode: \"disabled\"");
        aiPageSource.Should().Contain("OutputVerbosity: \"low\"");
        aiPageSource.Should().Contain("applyProfileKindDefaults");
    }

    [Fact]
    public void Vue_settings_refresh_preserves_unsaved_form_edits()
    {
        var storeSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.ControlPanel", "src", "state", "controlPanelStore.ts"));
        var pluginPageSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.ControlPanel", "src", "pages", "PluginSettingsPage.vue"));
        var aiPageSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.ControlPanel", "src", "pages", "AiSettingsPage.vue"));

        storeSource.Should().Contain("dirtyForms: new Set<string>()");
        storeSource.Should().Contain("export function setDirtyForm(key: string, dirty: boolean)");
        pluginPageSource.Should().Contain("if (!state || (!force && formDirty.value))");
        aiPageSource.Should().Contain("if (!state || (!force && formDirty.value))");
        pluginPageSource.Should().Contain("watch(() => controlPanelStore.state");
        aiPageSource.Should().Contain("watch(() => controlPanelStore.state");
    }

    [Fact]
    public void Vue_glossary_page_uses_inline_table_new_row_for_manual_terms()
    {
        var glossaryPageSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.ControlPanel", "src", "pages", "GlossaryPage.vue"));

        glossaryPageSource.Should().Contain("data-page=\"glossary\"");
        glossaryPageSource.Should().Contain("id=\"page-glossary\"");
        glossaryPageSource.Should().Contain("id=\"enableGlossary\"");
        glossaryPageSource.Should().Contain("id=\"enableAutoTermExtraction\"");
        glossaryPageSource.Should().Contain("async function loadGlossaryTerms()");
        glossaryPageSource.Should().Contain("async function saveGlossaryTerm(");
        glossaryPageSource.Should().Contain("async function deleteGlossaryTerm(");
        glossaryPageSource.Should().Contain("const showInlineTermEditor");
        glossaryPageSource.Should().Contain("const glossarySourceTermInput");
        glossaryPageSource.Should().Contain("function beginAddGlossaryTerm()");
        glossaryPageSource.Should().Contain("id=\"addGlossaryTerm\"");
        glossaryPageSource.Should().Contain("id=\"glossaryNewRow\"");
        glossaryPageSource.Should().Contain("v-if=\"showInlineTermEditor\"");
        glossaryPageSource.Should().Contain("class=\"compact-check cell-check\"");
        glossaryPageSource.Should().Contain("id=\"saveGlossaryInlineRow\"");
        glossaryPageSource.Should().NotContain("clearGlossaryForm");
        glossaryPageSource.Should().NotContain("title=\"新增或更新术语\"");
        glossaryPageSource.Should().Contain("/api/glossary");
    }

    [Fact]
    public void Vue_glossary_inline_new_row_is_a_real_table_row()
    {
        var glossaryPageSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.ControlPanel", "src", "pages", "GlossaryPage.vue"));
        var cssSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.ControlPanel", "src", "styles", "app.css"));

        var newRowBlock = Regex.Match(
            glossaryPageSource,
            @"<tr v-if=""showInlineTermEditor"" id=""glossaryNewRow""[\s\S]*?</tr>");

        newRowBlock.Success.Should().BeTrue("the glossary add editor should stay inside the table body");
        newRowBlock.Value.Should().Contain("td v-for=\"column in visibleColumns\"");
        newRowBlock.Value.Should().Contain("inlineTermCellClass(column)");
        newRowBlock.Value.Should().Contain("id=\"saveGlossaryInlineRow\"");
        newRowBlock.Value.Should().Contain("class=\"glossary-action-cell\"");
        newRowBlock.Value.Should().NotContain(":colspan=");
        glossaryPageSource.Should().Contain("id=\"glossaryActionHead\"");
        glossaryPageSource.Should().Contain("class=\"glossary-action-col\"");
        glossaryPageSource.Should().NotContain("glossary-inline-editor");
        glossaryPageSource.Should().NotContain("visibleColumnSpan");
        CssBlock(cssSource, @"\.inline-new-row \.cell-editor").Should()
            .Contain("min-height: 34px;")
            .And.Contain("resize: none;");
        CssBlock(cssSource, @"\.glossary-action-cell").Should()
            .Contain("text-align: right;")
            .And.Contain("vertical-align: middle;")
            .And.Contain("position: sticky;")
            .And.Contain("right: 0;");
    }

    [Fact]
    public void Vue_glossary_page_exposes_complete_table_controls()
    {
        var glossaryPageSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.ControlPanel", "src", "pages", "GlossaryPage.vue"));
        var glossaryTableSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.ControlPanel", "src", "utils", "glossaryTable.ts"));
        var languageSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.ControlPanel", "src", "utils", "languages.ts"));
        var cssSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.ControlPanel", "src", "styles", "app.css"));

        languageSource.Should().Contain("export const targetLanguageOptions");
        languageSource.Should().Contain("export function languageLabel(");
        languageSource.Should().Contain("export function normalizeLanguageInput(");

        glossaryTableSource.Should().Contain("hunity.glossary.visibleColumns");
        glossaryTableSource.Should().Contain("hunity.glossary.columnOrder");
        glossaryTableSource.Should().Contain("hunity.glossary.columnFilters");
        glossaryTableSource.Should().Contain("hunity.glossary.columnWidths");
        glossaryTableSource.Should().Contain("export const defaultGlossaryColumns");
        glossaryTableSource.Should().Contain("export function glossaryRowKey(");

        glossaryPageSource.Should().Contain("id=\"glossaryColumnMenuButton\"");
        glossaryPageSource.Should().Contain("id=\"glossaryColumnChooser\"");
        glossaryPageSource.Should().Contain("id=\"clearGlossaryFilters\"");
        glossaryPageSource.Should().Contain("id=\"saveGlossaryRows\"");
        glossaryPageSource.Should().Contain("id=\"deleteSelectedGlossaryTerms\"");
        glossaryPageSource.Should().Contain("id=\"glossaryContextMenu\"");
        glossaryPageSource.Should().Contain("id=\"glossaryColumnFilterMenu\"");
        glossaryPageSource.Should().Contain("data-glossary-action=\"delete\"");
        glossaryPageSource.Should().Contain("data-glossary-action=\"enable\"");
        glossaryPageSource.Should().Contain("data-glossary-action=\"disable\"");
        glossaryPageSource.Should().Contain("/api/glossary/filter-options");
        glossaryPageSource.Should().Contain("function startColumnResize(");
        glossaryPageSource.Should().Contain("@contextmenu=\"showContextMenu\"");
        glossaryPageSource.Should().Contain("class=\"col-resizer\"");
        glossaryPageSource.Should().Contain("class=\"cell-editor\"");
        glossaryPageSource.Should().Contain("class=\"cell-text\"");
        glossaryPageSource.Should().Contain("languageLabel(row.TargetLanguage)");
        glossaryPageSource.Should().Contain("normalizeLanguageInput(value)");
        glossaryPageSource.Should().Contain("v-for=\"option in targetLanguageOptions\"");
        glossaryPageSource.Should().Contain("OriginalSourceTerm");
        glossaryPageSource.Should().Contain("OriginalTargetLanguage");

        CssBlock(cssSource, @"\.glossary-cell-center").Should()
            .Contain("text-align: center;")
            .And.Contain("vertical-align: middle;");
    }

    [Fact]
    public void Vue_glossary_table_default_widths_fit_header_controls()
    {
        var glossaryTableSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.ControlPanel", "src", "utils", "glossaryTable.ts"));

        var compactHeaderMinimums = new Dictionary<string, int>
        {
            ["Enabled"] = 112,
            ["Source"] = 112,
            ["UsageCount"] = 112,
            ["CreatedUtc"] = 120,
            ["UpdatedUtc"] = 120
        };

        foreach (var (key, minimumWidth) in compactHeaderMinimums)
        {
            var match = Regex.Match(glossaryTableSource, $@"key:\s*""{key}""[^}}]*width:\s*(?<width>\d+)");
            match.Success.Should().BeTrue($"glossary column {key} should define a default width");
            int.Parse(match.Groups["width"].Value).Should().BeGreaterThanOrEqualTo(minimumWidth, $"glossary column {key} should fit its header title and controls");
        }
    }

    [Fact]
    public void Vue_editor_exposes_persistent_column_visibility_and_order_controls()
    {
        var editorPageSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.ControlPanel", "src", "pages", "TextEditorPage.vue"));
        var tableSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.ControlPanel", "src", "utils", "table.ts"));

        editorPageSource.Should().Contain("id=\"columnMenuButton\"");
        editorPageSource.Should().Contain("id=\"columnChooser\"");
        tableSource.Should().Contain("hunity.editor.visibleColumns");
        tableSource.Should().Contain("hunity.editor.columnOrder");
        tableSource.Should().Contain("export function loadColumnOrder()");
        editorPageSource.Should().Contain("function moveColumn(");
        editorPageSource.Should().Contain("data-column-key");
        editorPageSource.Should().Contain("data-column-move");
    }

    [Fact]
    public void Vue_editor_persists_resized_column_widths()
    {
        var editorPageSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.ControlPanel", "src", "pages", "TextEditorPage.vue"));
        var tableSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.ControlPanel", "src", "utils", "table.ts"));

        tableSource.Should().Contain("hunity.editor.columnWidths");
        tableSource.Should().Contain("export function loadColumnWidths()");
        tableSource.Should().Contain("export function saveColumnWidths(");
        editorPageSource.Should().Contain("loadColumnWidths");
        editorPageSource.Should().Contain("const columnWidths = reactive<Record<string, number>>(loadColumnWidths());");
        editorPageSource.Should().Contain("function clampColumnWidth(");
        editorPageSource.Should().Contain("saveColumnWidths(columnWidths);");
    }

    [Fact]
    public void Vue_editor_formats_table_timestamps_with_full_datetime()
    {
        var editorPageSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.ControlPanel", "src", "pages", "TextEditorPage.vue"));
        var formatSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.ControlPanel", "src", "utils", "format.ts"));
        var tableSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.ControlPanel", "src", "utils", "table.ts"));

        formatSource.Should().Contain("export function formatFullDateTime(value: string | null | undefined): string");
        formatSource.Should().Contain("date.getFullYear()");
        formatSource.Should().Contain("date.getSeconds()");
        formatSource.Should().Contain("`${year}-${month}-${day} ${hour}:${minute}:${second}`");
        editorPageSource.Should().Contain("import { formatFullDateTime } from \"../utils/format\";");
        editorPageSource.Should().Contain("column.key.endsWith(\"Utc\") ? formatFullDateTime(value) : value");
        tableSource.Should().Contain("{ key: \"CreatedUtc\", label: \"创建时间\", sort: \"created_utc\", editable: false, width: 190 }");
        tableSource.Should().Contain("{ key: \"UpdatedUtc\", label: \"更新时间\", sort: \"updated_utc\", editable: false, width: 190 }");
    }

    [Fact]
    public void Vue_editor_exposes_import_export_filter_and_row_actions()
    {
        var editorPageSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.ControlPanel", "src", "pages", "TextEditorPage.vue"));
        var tableSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.ControlPanel", "src", "utils", "table.ts"));

        editorPageSource.Should().Contain("Filter");
        editorPageSource.Should().Contain("aria-label=\"筛选\"");
        editorPageSource.Should().Contain("id=\"importRows\"");
        editorPageSource.Should().Contain("id=\"exportRows\"");
        editorPageSource.Should().Contain("id=\"importFile\"");
        editorPageSource.Should().Contain("id=\"exportMenu\"");
        editorPageSource.Should().NotContain("id=\"exportJson\"");
        editorPageSource.Should().NotContain("id=\"exportCsv\"");
        editorPageSource.Should().Contain("data-table-action=\"retranslate\"");
        editorPageSource.Should().Contain("data-table-action=\"highlight\"");
        editorPageSource.Should().Contain("data-table-action=\"copy\"");
        editorPageSource.Should().Contain("data-table-action=\"paste\"");
        editorPageSource.Should().Contain("id=\"tableContextMenu\"");
        editorPageSource.Should().Contain("async function retranslateSelectedRows()");
        editorPageSource.Should().Contain("async function highlightSelectedRow()");
        editorPageSource.Should().Contain("/api/translations/retranslate");
        editorPageSource.Should().Contain("/api/translations/highlight");
        editorPageSource.Should().Contain("id=\"columnFilterMenu\"");
        editorPageSource.Should().Contain("async function loadColumnFilterOptions(");
        editorPageSource.Should().Contain("function positionFilterMenu(");
        editorPageSource.Should().Contain("function hideColumnFilterMenu()");
        editorPageSource.Should().Contain("function appendColumnFilters(");
        editorPageSource.Should().Contain("/api/translations/filter-options");
        editorPageSource.Should().Contain("data-filter-column");
        editorPageSource.Should().Contain("filterMenu.x");
        editorPageSource.Should().Contain("filterMenu.y");
        editorPageSource.Should().Contain("@click.stop=\"openColumnFilterMenu(column, $event)\"");
        editorPageSource.Should().Contain("closest(\"#columnFilterMenu\")");
        editorPageSource.Should().Contain("closest(\".header-filter\")");
        tableSource.Should().Contain("hunity.editor.columnFilters");
    }

    [Fact]
    public void Vue_editor_export_filename_includes_game_name_and_local_timestamp()
    {
        var editorPageSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.ControlPanel", "src", "pages", "TextEditorPage.vue"));

        editorPageSource.Should().Contain("controlPanelStore.state?.GameTitle");
        editorPageSource.Should().Contain("controlPanelStore.state?.AutomaticGameTitle");
        editorPageSource.Should().Contain("function sanitizeExportFileNamePart(");
        editorPageSource.Should().Contain("function formatExportTimestamp(");
        editorPageSource.Should().Contain(@"replace(/[<>:""\/\\|?*\x00-\x1f]+/g");
        editorPageSource.Should().Contain("anchor.download = `hunity-translations-${gameName}-${timestamp}.${format}`;");
        editorPageSource.Should().NotContain("anchor.download = `hunity-translations.${format}`;");
    }

    [Fact]
    public void Vue_editor_aligns_search_box_and_toolbar_actions()
    {
        var cssSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.ControlPanel", "src", "styles", "app.css"));
        var editorToolsBlock = System.Text.RegularExpressions.Regex.Match(cssSource, @"\.editor-tools\s*\{[^}]*\}", System.Text.RegularExpressions.RegexOptions.Singleline);
        var editorActionsBlock = System.Text.RegularExpressions.Regex.Match(cssSource, @"\.editor-actions\s*\{[^}]*\}", System.Text.RegularExpressions.RegexOptions.Singleline);

        editorToolsBlock.Success.Should().BeTrue();
        editorToolsBlock.Value.Should().Contain("grid-template-columns: repeat(auto-fit, minmax(min(260px, 100%), 1fr));");
        editorToolsBlock.Value.Should().Contain("align-items: end;");
        editorActionsBlock.Success.Should().BeTrue();
        editorActionsBlock.Value.Should().Contain("align-self: end;");
    }

    [Fact]
    public void Vue_editor_restores_sort_icons_and_polishes_table_focus_and_scrollbars()
    {
        var editorPageSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.ControlPanel", "src", "pages", "TextEditorPage.vue"));
        var cssSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.ControlPanel", "src", "styles", "app.css"));

        editorPageSource.Should().Contain("SortAsc");
        editorPageSource.Should().Contain("SortDesc");
        editorPageSource.Should().Contain("ChevronsUpDown");
        editorPageSource.Should().Contain("function sortIcon(");
        editorPageSource.Should().Contain("function ariaSort(");
        editorPageSource.Should().Contain(":aria-sort=\"ariaSort(column)\"");
        editorPageSource.Should().Contain(":data-sort-state=\"sortState(column)\"");
        editorPageSource.Should().Contain("class=\"sort-icon\"");
        cssSource.Should().Contain(".header-title:focus-visible");
        cssSource.Should().Contain(".sort-icon");
        cssSource.Should().Contain(".table-wrap::-webkit-scrollbar");
        cssSource.Should().Contain(".cell-text::-webkit-scrollbar");
        cssSource.Should().Contain("scrollbar-width: thin");
    }

    [Fact]
    public void Vue_editor_rejects_non_page_translation_responses_before_assigning_rows()
    {
        var editorPageSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.ControlPanel", "src", "pages", "TextEditorPage.vue"));

        editorPageSource.Should().Contain("function isTranslationCachePage(value: unknown): value is TranslationCachePage");
        editorPageSource.Should().Contain("const page = await getJson<unknown>");
        editorPageSource.Should().Contain("if (!isTranslationCachePage(page))");
        editorPageSource.Should().Contain("throw new Error(\"翻译表返回格式无效\");");
        editorPageSource.Should().Contain("rows.value = page.Items;");
    }

    [Fact]
    public void Vue_editor_recovers_from_stale_stored_column_filters_on_first_load()
    {
        var editorPageSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.ControlPanel", "src", "pages", "TextEditorPage.vue"));

        editorPageSource.Should().Contain("function buildTranslationQuery(includeColumnFilters = true): URLSearchParams");
        editorPageSource.Should().Contain("async function loadTranslations(recoverStaleStoredFilters = false): Promise<void>");
        editorPageSource.Should().Contain("if (recoverStaleStoredFilters && !search.value.trim() && hasActiveColumnFilters() && page.TotalCount === 0)");
        editorPageSource.Should().Contain("const unfilteredPage = await getJson<unknown>(`/api/translations?${buildTranslationQuery(false).toString()}`);");
        editorPageSource.Should().Contain("clearColumnFiltersState();");
        editorPageSource.Should().Contain("showToast(\"已清除过期筛选，重新显示翻译表。\", \"info\");");
        editorPageSource.Should().Contain("void loadTranslations(true);");
    }

    [Fact]
    public void Vue_styles_include_restrained_motion_with_reduced_motion_fallback()
    {
        var appSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.ControlPanel", "src", "App.vue"));
        var cssSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.ControlPanel", "src", "styles", "app.css"));

        appSource.Should().Contain("<Transition name=\"page-fade\" mode=\"out-in\">");
        cssSource.Should().Contain(".page-fade-enter-active");
        cssSource.Should().Contain(".page-fade-leave-active");
        cssSource.Should().Contain("@media (prefers-reduced-motion: reduce)");
        cssSource.Should().Contain("animation: none !important");
    }

    [Fact]
    public void Vue_editor_restores_spreadsheet_cell_selection_context_menu_and_column_resize()
    {
        var editorPageSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.ControlPanel", "src", "pages", "TextEditorPage.vue"));
        var cssSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.ControlPanel", "src", "styles", "app.css"));

        editorPageSource.Should().Contain("const selectedCells = ref(new Set<string>());");
        editorPageSource.Should().Contain("const selectionAnchor = ref<CellAddress | null>(null);");
        editorPageSource.Should().Contain("function selectCell(");
        editorPageSource.Should().Contain("function selectRange(");
        editorPageSource.Should().Contain("function selectAllCells()");
        editorPageSource.Should().Contain("async function copyCells()");
        editorPageSource.Should().Contain("async function pasteCells()");
        editorPageSource.Should().Contain("function startColumnResize(");
        editorPageSource.Should().Contain("@contextmenu=\"showContextMenu\"");
        editorPageSource.Should().Contain("data-cell");
        editorPageSource.Should().Contain("data-row-index");
        editorPageSource.Should().Contain("data-column-key");
        editorPageSource.Should().Contain("class=\"col-resizer\"");
        editorPageSource.Should().Contain("@mousedown.stop");
        editorPageSource.Should().NotContain("<col style=\"width:42px\">");
        editorPageSource.Should().NotContain("<th></th>");
        editorPageSource.Should().NotContain("const selectedKeys");
        editorPageSource.Should().NotContain("function toggleRow(");
        editorPageSource.Should().NotContain("selectedKeys.has(rowKey(row))");
        cssSource.Should().Contain(".context-menu");
        cssSource.Should().Contain(".context-menu.open");
        cssSource.Should().Contain("td.selected");
        cssSource.Should().Contain(".col-resizer");
    }

    [Fact]
    public void Vue_editor_uses_textarea_cell_editor_to_preserve_text_input_order()
    {
        var editorPageSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.ControlPanel", "src", "pages", "TextEditorPage.vue"));
        var cssSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.ControlPanel", "src", "styles", "app.css"));

        editorPageSource.Should().Contain("<textarea");
        editorPageSource.Should().Contain("class=\"cell-editor\"");
        editorPageSource.Should().Contain(":value=\"displayCellValue(row, column)\"");
        editorPageSource.Should().Contain("@input=\"updateCell(rowIndex, column, $event)\"");
        editorPageSource.Should().Contain("@keydown.stop");
        editorPageSource.Should().Contain("@click.stop");
        editorPageSource.Should().Contain("@mousedown.stop");
        editorPageSource.Should().NotContain(":contenteditable=\"column.editable ? 'true' : undefined\"");
        editorPageSource.Should().NotContain("closest(\"[contenteditable='true']\")");
        cssSource.Should().Contain(".cell-editor");
        cssSource.Should().Contain("resize: vertical;");
    }

    [Fact]
    public void Vue_editor_delete_key_clears_editable_cells_instead_of_deleting_rows()
    {
        var editorPageSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.ControlPanel", "src", "pages", "TextEditorPage.vue"));

        editorPageSource.Should().Contain("function clearSelectedEditableCells()");
        editorPageSource.Should().Contain("updateCellValue(cell.row, column, \"\");");
        editorPageSource.Should().Contain("tableMessage.value = `已清空 ${clearedCells} 个单元格，等待保存。`;");
        editorPageSource.Should().Contain("void clearSelectedEditableCells();");
        editorPageSource.Should().NotContain("event.key === \"Delete\" && selectedCells.value.size) {\r\n    event.preventDefault();\r\n    void deleteSelectedRows();");
    }

    [Fact]
    public void Vue_editor_exposes_component_font_override_column()
    {
        var tableSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.ControlPanel", "src", "utils", "table.ts"));
        var editorPageSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.ControlPanel", "src", "pages", "TextEditorPage.vue"));

        tableSource.Should().Contain("key: \"ReplacementFont\"");
        tableSource.Should().Contain("sort: \"replacement_font\"");
        editorPageSource.Should().Contain("const fontEditor = reactive");
        editorPageSource.Should().Contain("function replacementFontCellLabel");
        editorPageSource.Should().Contain("function openFontEditor");
        editorPageSource.Should().Contain("async function pickComponentFontFile()");
        editorPageSource.Should().Contain("pickFontFile({ CopyToConfig: true })");
        editorPageSource.Should().Contain("id=\"componentFontDialog\"");
        editorPageSource.Should().Contain("class=\"cell-text font-override-text\"");
        editorPageSource.Should().Contain("@dblclick=\"openFontEditor(row, rowIndex)\"");
        editorPageSource.Should().NotContain("class=\"font-override-button\"");
    }

    [Fact]
    public void Vue_control_panel_exposes_texture_replacement_page()
    {
        var appSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.ControlPanel", "src", "App.vue"));
        var sidebarSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.ControlPanel", "src", "components", "AppSidebar.vue"));
        var apiTypesSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.ControlPanel", "src", "types", "api.ts"));
        var texturePageSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.ControlPanel", "src", "pages", "TexturePage.vue"));
        var cssSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.ControlPanel", "src", "styles", "app.css"));

        apiTypesSource.Should().Contain("| \"textures\"");
        apiTypesSource.Should().Contain("export interface TextureCatalogItem");
        apiTypesSource.Should().Contain("export interface TextureImportResult");
        apiTypesSource.Should().Contain("export interface TextureCatalogScanStatus");
        apiTypesSource.Should().Contain("TotalCount: number;");
        apiTypesSource.Should().Contain("FilteredCount: number;");
        apiTypesSource.Should().Contain("Scenes: string[];");
        sidebarSource.Should().Contain("label: \"贴图替换\"");
        appSource.Should().Contain("TexturePage");
        texturePageSource.Should().Contain("/api/textures/scan");
        texturePageSource.Should().Contain("/api/textures/export");
        texturePageSource.Should().Contain("/api/textures/import");
        texturePageSource.Should().Contain("/api/textures/overrides");
        texturePageSource.Should().Contain("const selectedScene");
        texturePageSource.Should().Contain("const viewMode");
        texturePageSource.Should().Contain("const currentPage");
        texturePageSource.Should().Contain("textureImageUrl(item)");
        texturePageSource.Should().Contain("loading=\"lazy\"");
        texturePageSource.Should().Contain("decoding=\"async\"");
        texturePageSource.Should().Contain("id=\"textureSceneFilter\"");
        texturePageSource.Should().Contain("class=\"texture-view-toggle\"");
        texturePageSource.Should().Contain("class=\"texture-gallery\"");
        texturePageSource.Should().Contain("class=\"texture-pager\"");
        texturePageSource.Should().Contain("id=\"scanTextures\"");
        texturePageSource.Should().Contain("id=\"exportTextures\"");
        texturePageSource.Should().Contain("id=\"importTextures\"");
        texturePageSource.Should().Contain("id=\"clearTextureOverrides\"");
        cssSource.Should().Contain(".texture-summary");
        cssSource.Should().Contain(".texture-list");
        cssSource.Should().Contain(".texture-gallery");
        cssSource.Should().Contain(".texture-pager");
    }

    [Fact]
    public void Vue_texture_page_exposes_text_detection_confirmation_and_generation_flow()
    {
        var texturePageSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.ControlPanel", "src", "pages", "TexturePage.vue"));
        var apiTypesSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.ControlPanel", "src", "types", "api.ts"));
        var cssSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.ControlPanel", "src", "styles", "app.css"));

        apiTypesSource.Should().Contain("export interface TextureTextAnalysis");
        apiTypesSource.Should().Contain("export interface TextureTextDetectionResult");
        apiTypesSource.Should().Contain("export interface TextureImageTranslateResult");
        texturePageSource.Should().Contain("const textStatusOptions");
        texturePageSource.Should().Contain("const textStatusFilter");
        texturePageSource.Should().Contain("textStatus: textStatusFilter.value");
        texturePageSource.Should().Contain("/api/textures/analyze-text");
        texturePageSource.Should().Contain("/api/textures/text-status");
        texturePageSource.Should().Contain("/api/textures/translate-text");
        texturePageSource.Should().Contain("id=\"textureTextStatusFilter\"");
        texturePageSource.Should().Contain("id=\"detectTextureText\"");
        texturePageSource.Should().Contain("id=\"confirmTextureText\"");
        texturePageSource.Should().Contain("id=\"markTextureNoText\"");
        texturePageSource.Should().Contain("id=\"translateTextureText\"");
        texturePageSource.Should().Contain("textureTextStatusLabel(item)");
        texturePageSource.Should().Contain("toggleTextureSelectionFromEvent");
        cssSource.Should().Contain(".texture-text-badge");
        cssSource.Should().Contain(".texture-selection-tools");
    }

    [Fact]
    public void Vue_texture_page_exposes_deferred_large_texture_scan()
    {
        var texturePageSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.ControlPanel", "src", "pages", "TexturePage.vue"));
        var apiTypesSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.ControlPanel", "src", "types", "api.ts"));

        apiTypesSource.Should().Contain("DeferredTargetCount: number;");
        apiTypesSource.Should().Contain("DeferredTextureCount: number;");
        apiTypesSource.Should().Contain("export interface TextureScanRequest");
        apiTypesSource.Should().Contain("IncludeDeferredLargeTextures");
        texturePageSource.Should().Contain("scanLargeTextures");
        texturePageSource.Should().Contain("IncludeDeferredLargeTextures: true");
        texturePageSource.Should().Contain("id=\"scanLargeTextures\"");
        texturePageSource.Should().Contain("延迟超大贴图");
        texturePageSource.Should().Contain("DeferredTargetCount");
        texturePageSource.Should().Contain("DeferredTextureCount");
    }

    [Fact]
    public void Vue_texture_page_uses_override_thumbnails_and_comparison_dialog()
    {
        var texturePageSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.ControlPanel", "src", "pages", "TexturePage.vue"));
        var cssSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.ControlPanel", "src", "styles", "app.css"));

        texturePageSource.Should().Contain("textureImageUrl(item, \"override\")");
        texturePageSource.Should().Contain("textureDisplayImageUrl(item)");
        texturePageSource.Should().Contain("openTextureCompare");
        texturePageSource.Should().Contain("textureCompareDialogOpen");
        texturePageSource.Should().Contain("原图");
        texturePageSource.Should().Contain("译图");
        texturePageSource.Should().Contain("controlPanelStore.textureViewMode");
        texturePageSource.Should().NotContain("localStorage");
        cssSource.Should().Contain(".texture-compare-dialog");
        cssSource.Should().Contain(".texture-compare-grid");
    }

    [Fact]
    public void Vue_texture_page_keeps_panels_visually_separated()
    {
        var texturePageSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.ControlPanel", "src", "pages", "TexturePage.vue"));
        var cssSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.ControlPanel", "src", "styles", "app.css"));

        texturePageSource.Should().Contain("class=\"page active texture-page\"");
        CssBlock(cssSource, @"\.texture-page").Should().Contain("display: grid;").And.Contain("gap: 18px;");
        CssBlock(cssSource, @"\.texture-page \.page-head").Should().Contain("margin-bottom: 0;");
        CssBlock(cssSource, @"\.texture-summary").Should().Contain("margin-bottom: 0;");
    }

    [Fact]
    public void Vue_and_cfg_describe_font_controls_as_on_demand_assistance()
    {
        var pluginPageSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.ControlPanel", "src", "pages", "PluginSettingsPage.vue"));
        var cfgSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.Core", "Control", "CfgControlPanelSettingsStore.cs"));

        pluginPageSource.Should().Contain("按需字体辅助");
        pluginPageSource.Should().Contain("优先保留原字体，只有缺字时才启用替换或 fallback。");
        pluginPageSource.Should().NotContain("开启后插件会尝试把文本组件换成能显示中文的字体。");
        pluginPageSource.Should().NotContain(">启用字体替换</label>");
        cfgSource.Should().Contain("是否启用按需字体辅助。");
        cfgSource.Should().Contain("优先保留原字体，只有缺字时才使用替换字体或 TMP fallback。");
    }

    private static string FindRepositoryFile(params string[] relativeSegments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "HUnityAutoTranslator.sln")))
        {
            directory = directory.Parent;
        }

        directory.Should().NotBeNull("tests should run from inside the repository checkout");
        return Path.Combine(new[] { directory!.FullName }.Concat(relativeSegments).ToArray());
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "HUnityAutoTranslator.sln")))
        {
            directory = directory.Parent;
        }

        directory.Should().NotBeNull("tests should run from inside the repository checkout");
        return directory!.FullName;
    }

    private static string CssBlock(string cssSource, string selectorPattern)
    {
        var block = System.Text.RegularExpressions.Regex.Match(
            cssSource,
            $@"{selectorPattern}\s*\{{[^}}]*\}}",
            System.Text.RegularExpressions.RegexOptions.Singleline);
        block.Success.Should().BeTrue($"CSS should contain a {selectorPattern} block");
        return block.Value;
    }
}

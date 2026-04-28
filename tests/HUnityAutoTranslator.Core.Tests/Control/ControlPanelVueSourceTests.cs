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
        statusPageSource.Should().Contain("providerLabel");
        statusPageSource.Should().Contain("MetricCard");
        statusPageSource.Should().Contain("value-id=\"cacheCount\"");
        statusPageSource.Should().Contain("value-id=\"enabledText\"");
        statusPageSource.Should().NotContain("state.value.QueueCount || state.value.QueuedTextCount || 0");
        statusPageSource.Should().NotContain("WritebackQueueCount");
        statusPageSource.Should().NotContain("status-command");
        statusPageSource.Should().NotContain("providerStatusText");
    }

    [Fact]
    public void Vue_status_page_shows_in_flight_capacity_without_completed_count_card()
    {
        var statusPageSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.ControlPanel", "src", "pages", "StatusPage.vue"));

        statusPageSource.Should().Contain("const activeTranslationCapacity");
        statusPageSource.Should().Contain("function formatInFlightCapacity");
        statusPageSource.Should().Contain("state.value.InFlightTranslationCount");
        statusPageSource.Should().Contain("state.value.EffectiveMaxConcurrentRequests");
        statusPageSource.Should().Contain("value-id=\"inFlightTranslationCount\"");
        statusPageSource.Should().NotContain("value-id=\"completedTranslationCount\"");
        statusPageSource.Should().NotContain("state?.CompletedTranslationCount");
    }

    [Fact]
    public void Vue_ai_settings_documents_online_concurrency_limit_and_effective_capacity()
    {
        var aiPageSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.ControlPanel", "src", "pages", "AiSettingsPage.vue"));
        var statusPageSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.ControlPanel", "src", "pages", "StatusPage.vue"));
        var apiTypesSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.ControlPanel", "src", "types", "api.ts"));

        apiTypesSource.Should().Contain("EffectiveMaxConcurrentRequests: number;");
        statusPageSource.Should().Contain("state.value.EffectiveMaxConcurrentRequests");
        aiPageSource.Should().Contain("在线服务并发请求");
        aiPageSource.Should().Contain("max=\"100\"");
        aiPageSource.Should().Contain("llama.cpp 使用并行槽位");
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
        statusPageSource.Should().Contain(":icon=\"Gauge\"");
        pluginPageSource.Should().Contain("from \"lucide-vue-next\"");
        pluginPageSource.Should().NotContain("class=\"option-icon\"");
        aiPageSource.Should().Contain("from \"lucide-vue-next\"");
        aiPageSource.Should().Contain("class=\"option-icon\"");
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
        sidebarSource.Should().Contain("<span v-if=\"!collapsed\">控制面板</span>");
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
    public void Vue_plugin_settings_can_pick_font_file_and_fill_name_before_save()
    {
        var storeSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.ControlPanel", "src", "state", "controlPanelStore.ts"));
        var apiTypesSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.ControlPanel", "src", "types", "api.ts"));
        var pluginPageSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.ControlPanel", "src", "pages", "PluginSettingsPage.vue"));

        apiTypesSource.Should().Contain("export type FontPickStatus = \"selected\" | \"cancelled\" | \"unsupported\" | \"error\";");
        apiTypesSource.Should().Contain("export interface FontPickResult");
        storeSource.Should().Contain("export async function pickFontFile()");
        storeSource.Should().Contain("api<FontPickResult>(\"/api/fonts/pick\", { method: \"POST\" })");
        pluginPageSource.Should().Contain("async function pickReplacementFontFile()");
        pluginPageSource.Should().Contain("form.ReplacementFontFile = result.FilePath;");
        pluginPageSource.Should().Contain("form.ReplacementFontName = result.FontName ?? \"\";");
        pluginPageSource.Should().Contain("id=\"pickReplacementFontFile\"");
    }

    [Fact]
    public void Vue_ai_settings_save_and_provider_utility_persist_pending_api_key()
    {
        var aiPageSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.ControlPanel", "src", "pages", "AiSettingsPage.vue"));

        aiPageSource.Should().Contain("async function saveConfigOnly()");
        aiPageSource.Should().Contain("async function savePendingApiKey()");
        aiPageSource.Should().Contain("const apiKey = form.ApiKey.trim();");
        aiPageSource.Should().Contain("await saveConfigOnly();");
        aiPageSource.Should().Contain("await savePendingApiKey();");
    }

    [Fact]
    public void Vue_ai_settings_exposes_game_title_override_and_llamacpp_temperature()
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
        aiPageSource.Should().Contain("form.ProviderKind === 1 || form.ProviderKind === 2 || form.ProviderKind === 3");
        aiPageSource.Should().Contain("data-providers=\"1,2,3\"");
    }

    [Fact]
    public void Vue_ai_prompt_editor_shows_default_prompt_and_removes_custom_instruction()
    {
        var aiPageSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.ControlPanel", "src", "pages", "AiSettingsPage.vue"));
        var apiTypesSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.ControlPanel", "src", "types", "api.ts"));

        apiTypesSource.Should().Contain("DefaultSystemPrompt: string;");
        apiTypesSource.Should().Contain("PromptTemplates: PromptTemplateConfig;");
        apiTypesSource.Should().Contain("DefaultPromptTemplates: PromptTemplateConfig;");
        apiTypesSource.Should().NotContain("CustomInstruction");
        aiPageSource.Should().Contain("DefaultSystemPrompt");
        aiPageSource.Should().Contain("promptTemplateFields");
        aiPageSource.Should().Contain("activePromptTemplateKey");
        aiPageSource.Should().Contain("id=\"customPrompt\"");
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
        aiPageSource.Should().Contain("恢复内置提示词");
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
        aiPageSource.Should().Contain("async function fetchModels()");
        aiPageSource.Should().Contain("async function fetchBalance()");
        aiPageSource.Should().Contain("async function testProvider()");
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
        aiPageSource.Should().Contain("formatBalanceToast(result)");
        aiPageSource.Should().Contain("balance.TotalBalance");
        aiPageSource.Should().Contain("balance.GrantedBalance");
        aiPageSource.Should().Contain("balance.ToppedUpBalance");
    }

    [Fact]
    public void Vue_deepseek_presets_use_current_v4_model_ids()
    {
        var aiPageSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.ControlPanel", "src", "pages", "AiSettingsPage.vue"));

        aiPageSource.Should().Contain("model: \"deepseek-v4-flash\"");
        aiPageSource.Should().Contain("value: \"deepseek-v4-flash\", label: \"DeepSeek V4 Flash\"");
        aiPageSource.Should().Contain("value: \"deepseek-v4-pro\", label: \"DeepSeek V4 Pro\"");
    }

    [Fact]
    public void Vue_ai_settings_expose_llamacpp_install_status_without_manual_port()
    {
        var aiPageSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.ControlPanel", "src", "pages", "AiSettingsPage.vue"));
        var apiTypesSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.ControlPanel", "src", "types", "api.ts"));

        apiTypesSource.Should().Contain("export interface LlamaCppConfig");
        apiTypesSource.Should().Contain("export interface LlamaCppServerStatus");
        apiTypesSource.Should().Contain("Installed: boolean");
        apiTypesSource.Should().Contain("Release: string | null");
        apiTypesSource.Should().Contain("Variant: string | null");
        apiTypesSource.Should().Contain("ServerPath: string | null");
        aiPageSource.Should().Contain("id=\"llamaCppModelPath\"");
        aiPageSource.Should().Contain("id=\"pickLlamaCppModel\"");
        aiPageSource.Should().Contain("id=\"startLlamaCpp\"");
        aiPageSource.Should().Contain("id=\"stopLlamaCpp\"");
        aiPageSource.Should().Contain("llamaCppIsActive");
        aiPageSource.Should().Contain("v-if=\"!llamaCppIsActive\"");
        aiPageSource.Should().Contain("v-else id=\"stopLlamaCpp\"");
        aiPageSource.Should().Contain("llamaCppInstallText");
        aiPageSource.Should().Contain("/api/llamacpp/start");
        aiPageSource.Should().Contain("/api/llamacpp/stop");
        aiPageSource.Should().Contain("/api/llamacpp/model/pick");
        aiPageSource.Should().NotContain("id=\"llamaCppPort\"");
        aiPageSource.Should().NotContain("<div><span>端口</span>");
    }

    [Fact]
    public void Vue_ai_settings_hides_online_model_controls_for_llamacpp()
    {
        var aiPageSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.ControlPanel", "src", "pages", "AiSettingsPage.vue"));

        aiPageSource.Should().Contain("Model: isLlamaCpp.value ? \"local-model\" : form.Model");
        aiPageSource.Should().Contain("<div v-if=\"!isLlamaCpp\" class=\"ai-model-row\"");
        aiPageSource.Should().Contain("<button v-if=\"!isLlamaCpp\" id=\"fetchModels\"");
        aiPageSource.Should().Contain("<label v-if=\"!isLlamaCpp\" class=\"field\"><span class=\"field-label\"><Gauge class=\"field-label-icon\" />在线服务并发请求</span>");
        aiPageSource.Should().Contain("llama.cpp 使用并行槽位控制本地模型压力。");
    }

    [Fact]
    public void Vue_ai_settings_uses_compact_llamacpp_layout()
    {
        var aiPageSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.ControlPanel", "src", "pages", "AiSettingsPage.vue"));
        var cssSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.ControlPanel", "src", "styles", "app.css"));

        aiPageSource.Should().Contain("class=\"ai-provider-grid\"");
        aiPageSource.Should().Contain("class=\"ai-model-row\"");
        aiPageSource.Should().Contain("class=\"llama-local-panel\"");
        aiPageSource.Should().Contain("class=\"llama-status-strip\"");
        aiPageSource.Should().Contain("class=\"llama-run-row\"");
        aiPageSource.Should().Contain("class=\"llama-result-card\"");
        aiPageSource.Should().Contain("id=\"llamaCppModelPath\"");
        aiPageSource.Should().Contain("id=\"pickLlamaCppModel\"");
        aiPageSource.Should().Contain("id=\"startLlamaCpp\"");
        aiPageSource.Should().Contain("id=\"stopLlamaCpp\"");
        aiPageSource.Should().NotContain("mini-summary");
        cssSource.Should().NotContain(".mini-summary");
        cssSource.Should().Contain(".ai-provider-grid");
        cssSource.Should().Contain(".ai-model-row");
        cssSource.Should().Contain(".llama-local-panel");
        cssSource.Should().Contain(".llama-status-strip");
        cssSource.Should().Contain(".llama-run-row");
        cssSource.Should().Contain(".llama-result-card");
    }

    [Fact]
    public void Vue_ai_defaults_disable_reasoning_and_thinking()
    {
        var aiPageSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.ControlPanel", "src", "pages", "AiSettingsPage.vue"));

        aiPageSource.Should().Contain("ReasoningEffort: \"none\"");
        aiPageSource.Should().Contain("DeepSeekReasoningEffort: \"none\"");
        aiPageSource.Should().Contain("DeepSeekThinkingMode: \"disabled\"");
        aiPageSource.Should().Contain("applyProviderDefaults");
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
        glossaryPageSource.Should().Contain("class=\"compact-check\"");
        glossaryPageSource.Should().Contain("id=\"saveGlossaryInlineRow\"");
        glossaryPageSource.Should().NotContain("clearGlossaryForm");
        glossaryPageSource.Should().NotContain("title=\"新增或更新术语\"");
        glossaryPageSource.Should().Contain("/api/glossary");
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
    public void Vue_editor_aligns_search_box_and_toolbar_actions()
    {
        var cssSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.ControlPanel", "src", "styles", "app.css"));
        var editorToolsBlock = System.Text.RegularExpressions.Regex.Match(cssSource, @"\.editor-tools\s*\{[^}]*\}", System.Text.RegularExpressions.RegexOptions.Singleline);
        var editorActionsBlock = System.Text.RegularExpressions.Regex.Match(cssSource, @"\.editor-actions\s*\{[^}]*\}", System.Text.RegularExpressions.RegexOptions.Singleline);

        editorToolsBlock.Success.Should().BeTrue();
        editorToolsBlock.Value.Should().Contain("grid-template-columns: minmax(260px, 1fr) auto;");
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

        tableSource.Should().Contain("key: \"ReplacementFont\"");
        tableSource.Should().Contain("sort: \"replacement_font\"");
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
}

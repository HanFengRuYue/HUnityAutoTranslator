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
        statusPageSource.Should().Contain("return \"连接中断\";");
        statusPageSource.Should().Contain("value-id=\"enabledText\"");
        statusPageSource.Should().Contain(":tone=\"enabledTone\"");
    }

    [Fact]
    public void Vue_status_metrics_explain_their_meaning_on_hover()
    {
        var metricSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.ControlPanel", "src", "components", "MetricCard.vue"));
        var statusPageSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.ControlPanel", "src", "pages", "StatusPage.vue"));
        var cssSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.ControlPanel", "src", "styles", "app.css"));

        cssSource.Should().Contain(".metric[data-help]::after");
        metricSource.Should().Contain("tabindex=\"0\"");
        metricSource.Should().Contain(":data-help=\"help\"");
        statusPageSource.Should().Contain("help=\"当前插件是否启用");
        statusPageSource.Should().Contain("help=\"已经排队、尚未被 AI 服务处理");
        statusPageSource.Should().Contain("help=\"译文已准备好，等待写回 Unity 文本组件的数量。\"");
        statusPageSource.Should().Contain("help=\"已经保存到本地 SQLite，后续可直接复用或编辑的译文数量。\"");
        statusPageSource.Should().Contain("label=\"已翻译文本\"");
        statusPageSource.Should().Contain("value-id=\"cacheCount\"");
        statusPageSource.Should().NotContain("缓存条目");
        statusPageSource.Should().NotContain("文本条目");
    }

    [Fact]
    public void Vue_status_metric_help_tooltips_shrink_to_their_content()
    {
        var cssSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.ControlPanel", "src", "styles", "app.css"));

        cssSource.Should().Contain("width: max-content;");
        cssSource.Should().Contain("max-width: min(360px, calc(100vw - 44px));");
        cssSource.Should().Contain("white-space: normal;");
        cssSource.Should().Contain("overflow-wrap: anywhere;");
        cssSource.Should().Contain("max-width: calc(100vw - 52px);");
        cssSource.Should().NotContain("width: min(280px, calc(100vw - 44px));");
    }

    [Fact]
    public void Vue_status_page_keeps_zero_waiting_queue_count()
    {
        var statusPageSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.ControlPanel", "src", "pages", "StatusPage.vue"));

        statusPageSource.Should().Contain("state.value.QueueCount ?? state.value.QueuedTextCount ?? 0");
        statusPageSource.Should().NotContain("state.value.QueueCount || state.value.QueuedTextCount || 0");
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
        pluginPageSource.Should().Contain("listeningHotkeyField.value = field;");
        pluginPageSource.Should().Contain("event.key === \"Escape\"");
        pluginPageSource.Should().Contain("form[field] = \"None\";");
        pluginPageSource.Should().Contain("showToast(\"需要使用 Ctrl、Shift 或 Alt 组合键。\", \"warn\")");
        pluginPageSource.Should().Contain("markDirty();");
        pluginPageSource.Should().Contain("OpenControlPanelHotkey: form.OpenControlPanelHotkey");
        pluginPageSource.Should().Contain("ToggleTranslationHotkey: form.ToggleTranslationHotkey");
        pluginPageSource.Should().Contain("ForceScanHotkey: form.ForceScanHotkey");
        pluginPageSource.Should().Contain("ToggleFontHotkey: form.ToggleFontHotkey");
        pluginPageSource.Should().Contain("id=\"enableFontReplacement\"");
        pluginPageSource.Should().Contain("id=\"replaceUguiFonts\"");
        pluginPageSource.Should().Contain("id=\"replaceTmpFonts\"");
        pluginPageSource.Should().Contain("id=\"replaceImguiFonts\"");
        pluginPageSource.Should().Contain("id=\"autoUseCjkFallbackFonts\"");
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
        pluginPageSource.Should().Contain("markDirty();");
        pluginPageSource.Should().Contain("id=\"pickReplacementFontFile\"");
        pluginPageSource.Should().Contain(":disabled=\"isPickingFontFile\"");
        pluginPageSource.Should().Contain("选择字体文件");
    }

    [Fact]
    public void Vue_ai_settings_save_and_provider_utility_persist_pending_api_key()
    {
        var aiPageSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.ControlPanel", "src", "pages", "AiSettingsPage.vue"));

        aiPageSource.Should().Contain("async function saveConfigOnly()");
        aiPageSource.Should().Contain("async function savePendingApiKey()");
        aiPageSource.Should().Contain("const apiKey = form.ApiKey.trim();");
        aiPageSource.Should().Contain("if (!apiKey)");
        aiPageSource.Should().Contain("await saveConfigOnly();");
        aiPageSource.Should().Contain("await savePendingApiKey();");
        aiPageSource.Should().Contain("未填写新密钥，已保留当前密钥。");
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
        aiPageSource.Should().Contain("余额：");
        aiPageSource.Should().Contain("最近 7 天成本：");
        aiPageSource.Should().Contain("未返回余额/成本记录。");
        aiPageSource.Should().NotContain("showToast(result.Message || `已获取 ${result.Balances.length} 条余额记录。`");
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
    public void Vue_glossary_page_exposes_settings_and_crud_endpoints()
    {
        var glossaryPageSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.ControlPanel", "src", "pages", "GlossaryPage.vue"));

        glossaryPageSource.Should().Contain("data-page=\"glossary\"");
        glossaryPageSource.Should().Contain("id=\"page-glossary\"");
        glossaryPageSource.Should().Contain("id=\"enableGlossary\"");
        glossaryPageSource.Should().Contain("id=\"enableAutoTermExtraction\"");
        glossaryPageSource.Should().Contain("AI 自动提取默认关闭");
        glossaryPageSource.Should().Contain("async function loadGlossaryTerms()");
        glossaryPageSource.Should().Contain("async function saveGlossaryTerm(");
        glossaryPageSource.Should().Contain("async function deleteGlossaryTerm(");
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
    public void Vue_editor_exposes_import_export_filter_and_row_actions()
    {
        var editorPageSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.ControlPanel", "src", "pages", "TextEditorPage.vue"));
        var tableSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.ControlPanel", "src", "utils", "table.ts"));

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
        editorPageSource.Should().NotContain("class=\"secondary\" type=\"button\" data-table-action=\"retranslate\"");
        editorPageSource.Should().NotContain("class=\"secondary\" type=\"button\" data-table-action=\"highlight\"");
        editorPageSource.Should().NotContain("class=\"danger\" type=\"button\" data-table-action=\"delete\"");
        editorPageSource.Should().Contain("async function retranslateSelectedRows()");
        editorPageSource.Should().Contain("async function highlightSelectedRow()");
        editorPageSource.Should().Contain("/api/translations/retranslate");
        editorPageSource.Should().Contain("/api/translations/highlight");
        editorPageSource.Should().Contain("id=\"columnFilterMenu\"");
        editorPageSource.Should().Contain("async function loadColumnFilterOptions(");
        editorPageSource.Should().Contain("function appendColumnFilters(");
        editorPageSource.Should().Contain("/api/translations/filter-options");
        editorPageSource.Should().Contain("data-filter-column");
        tableSource.Should().Contain("hunity.editor.columnFilters");
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
    public void Vue_editor_exposes_component_font_override_column()
    {
        var tableSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.ControlPanel", "src", "utils", "table.ts"));

        tableSource.Should().Contain("key: \"ReplacementFont\"");
        tableSource.Should().Contain("label: \"替换字体\"");
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

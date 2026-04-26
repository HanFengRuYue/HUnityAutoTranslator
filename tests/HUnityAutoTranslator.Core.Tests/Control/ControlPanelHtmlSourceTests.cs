using FluentAssertions;

namespace HUnityAutoTranslator.Core.Tests.Control;

public sealed class ControlPanelHtmlSourceTests
{
    [Fact]
    public void Control_panel_editor_exposes_persistent_column_visibility_controls()
    {
        var htmlSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.Plugin", "Web", "ControlPanelHtml.cs"));

        htmlSource.Should().Contain("id=\"columnMenuButton\"");
        htmlSource.Should().Contain("id=\"columnChooser\"");
        htmlSource.Should().Contain("hunity.editor.visibleColumns");
        htmlSource.Should().Contain("function visibleTableColumns()");
        htmlSource.Should().Contain("function renderColumnChooser()");
        htmlSource.Should().Contain("data-column-key");
        htmlSource.Should().NotContain("data-col=\"${columnIndex}\"");
    }

    [Fact]
    public void Control_panel_editor_uses_requested_column_order_and_allows_manual_reordering()
    {
        var htmlSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.Plugin", "Web", "ControlPanelHtml.cs"));

        var expectedOrder = new[]
        {
            "SourceText",
            "TranslatedText",
            "TargetLanguage",
            "SceneName",
            "ComponentHierarchy",
            "ComponentType",
            "ProviderKind",
            "ProviderModel",
            "CreatedUtc",
            "UpdatedUtc"
        };

        var positions = expectedOrder
            .Select(key => htmlSource.IndexOf($"key: \"{key}\"", StringComparison.Ordinal))
            .ToArray();

        positions.Should().OnlyContain(position => position >= 0);
        positions.Should().BeInAscendingOrder();
        htmlSource.Should().Contain("hunity.editor.columnOrder");
        htmlSource.Should().Contain("function loadColumnLayout()");
        htmlSource.Should().Contain("function moveColumn(");
        htmlSource.Should().Contain("data-column-move");
    }

    [Fact]
    public void Control_panel_editor_consolidates_import_export_controls()
    {
        var htmlSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.Plugin", "Web", "ControlPanelHtml.cs"));

        htmlSource.Should().Contain("id=\"importRows\"");
        htmlSource.Should().Contain("id=\"exportRows\"");
        htmlSource.Should().Contain("id=\"importFile\" class=\"hidden-file-input\"");
        htmlSource.Should().Contain("window.showSaveFilePicker");
        htmlSource.Should().Contain("id=\"exportMenu\"");
        htmlSource.Should().NotContain("id=\"exportJson\"");
        htmlSource.Should().NotContain("id=\"exportCsv\"");
        htmlSource.Should().NotContain("style=\"max-width:220px\"");
    }

    [Fact]
    public void Provider_utility_feedback_uses_top_toasts_instead_of_inline_card_output()
    {
        var htmlSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.Plugin", "Web", "ControlPanelHtml.cs"));

        htmlSource.Should().Contain("id=\"toastHost\"");
        htmlSource.Should().Contain("function showToast(");
        htmlSource.Should().Contain("function renderModels(");
        htmlSource.Should().Contain("showToast(`已获取 ${models.length} 个模型。${details}`");
        htmlSource.Should().NotContain("id=\"providerStatusText\"");
        htmlSource.Should().NotContain("setText(\"providerStatusText\"");
        htmlSource.Should().NotContain("id=\"providerUtilityMessage\"");
        htmlSource.Should().NotContain("id=\"providerUtilityOutput\"");
    }

    [Fact]
    public void Ai_settings_save_and_provider_utility_persist_pending_api_key()
    {
        var htmlSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.Plugin", "Web", "ControlPanelHtml.cs"));

        htmlSource.Should().Contain("async function saveConfigOnly()");
        htmlSource.Should().Contain("async function savePendingApiKey()");
        htmlSource.Should().Contain("const apiKey = $(\"apiKey\").value.trim();");
        htmlSource.Should().Contain("if (!apiKey) return false;");
        htmlSource.Should().Contain("await saveConfigOnly();");
        htmlSource.Should().Contain("await savePendingApiKey();");
        htmlSource.Should().Contain("showToast(\"未填写新密钥，已保留当前密钥。\", \"info\")");
    }

    [Fact]
    public void DeepSeek_presets_use_current_official_v4_model_ids()
    {
        var htmlSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.Plugin", "Web", "ControlPanelHtml.cs"));

        htmlSource.Should().Contain("1: { baseUrl: \"https://api.deepseek.com\", endpoint: \"/chat/completions\", model: \"deepseek-v4-flash\" }");
        htmlSource.Should().Contain("[\"deepseek-v4-flash\", \"DeepSeek V4 Flash\"");
        htmlSource.Should().Contain("[\"deepseek-v4-pro\", \"DeepSeek V4 Pro\"");
    }

    [Fact]
    public void Ai_settings_keep_deepseek_thinking_selection_when_state_omits_field()
    {
        var htmlSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.Plugin", "Web", "ControlPanelHtml.cs"));

        htmlSource.Should().Contain("function hasStateField(");
        htmlSource.Should().Contain("function setKnownSelectValue(");
        htmlSource.Should().Contain("setKnownSelectValue(\"deepSeekThinkingMode\", state.DeepSeekThinkingMode);");
        htmlSource.Should().NotContain("$(\"deepSeekThinkingMode\").value = state.DeepSeekThinkingMode || \"enabled\";");
    }

    [Fact]
    public void Control_panel_refresh_does_not_default_missing_config_fields()
    {
        var htmlSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.Plugin", "Web", "ControlPanelHtml.cs"));

        htmlSource.Should().Contain("setStateTextValue(state, \"baseUrl\", \"BaseUrl\")");
        htmlSource.Should().Contain("setStateNumberValues(state);");
        htmlSource.Should().Contain("setStateCheckboxValues(state);");
        htmlSource.Should().Contain("setKnownSelectValue(\"providerKind\", state.ProviderKind);");
        htmlSource.Should().Contain("setKnownSelectValue(\"outputVerbosity\", state.OutputVerbosity);");
        htmlSource.Should().NotContain("$(\"providerKind\").value = String(state.ProviderKind);");
        htmlSource.Should().NotContain("$(id).checked = Boolean(state[id[0].toUpperCase() + id.slice(1)]);");
    }

    [Fact]
    public void Refresh_failure_marks_plugin_status_as_disconnected()
    {
        var htmlSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.Plugin", "Web", "ControlPanelHtml.cs"));

        htmlSource.Should().Contain("function markPanelDisconnected(error)");
        htmlSource.Should().Contain("setText(\"enabledText\", \"连接中断\")");
        htmlSource.Should().Contain("$(\"enabledText\").className = \"danger-text\"");
        htmlSource.Should().Contain("markPanelDisconnected(error);");
    }

    [Fact]
    public void Status_metrics_explain_their_meaning_on_hover()
    {
        var htmlSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.Plugin", "Web", "ControlPanelHtml.cs"));

        htmlSource.Should().Contain(".metric[data-help]::after");
        htmlSource.Should().Contain("tabindex=\"0\" data-help=\"当前插件是否启用");
        htmlSource.Should().Contain("data-help=\"已经排队、尚未被 AI 服务处理");
        htmlSource.Should().Contain("data-help=\"译文已准备好，等待写回 Unity 文本组件的数量。");
        htmlSource.Should().Contain("data-help=\"已经保存到本地 SQLite，后续可直接复用或编辑的译文数量。");
        htmlSource.Should().Contain("<span>已翻译文本</span><strong id=\"cacheCount\">0</strong>");
        htmlSource.Should().NotContain("<span>缓存条目</span>");
        htmlSource.Should().NotContain("文本条目");
    }

    [Fact]
    public void Status_metric_help_tooltips_shrink_to_their_content()
    {
        var htmlSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.Plugin", "Web", "ControlPanelHtml.cs"));

        htmlSource.Should().Contain("width: max-content;");
        htmlSource.Should().Contain("max-width: min(360px, calc(100vw - 44px));");
        htmlSource.Should().Contain("white-space: normal;");
        htmlSource.Should().Contain("overflow-wrap: anywhere;");
        htmlSource.Should().Contain(".metric[data-help]::after { left: 12px; right: auto; max-width: calc(100vw - 52px); }");
        htmlSource.Should().NotContain("width: min(280px, calc(100vw - 44px));");
    }

    [Fact]
    public void Status_page_keeps_zero_waiting_queue_count()
    {
        var htmlSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.Plugin", "Web", "ControlPanelHtml.cs"));

        htmlSource.Should().Contain("state.QueueCount ?? state.QueuedTextCount ?? 0");
        htmlSource.Should().NotContain("state.QueueCount || state.QueuedTextCount || 0");
    }

    [Fact]
    public void Plugin_settings_expose_font_replacement_controls()
    {
        var htmlSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.Plugin", "Web", "ControlPanelHtml.cs"));

        htmlSource.Should().Contain("id=\"enableFontReplacement\"");
        htmlSource.Should().Contain("id=\"replaceUguiFonts\"");
        htmlSource.Should().Contain("id=\"replaceTmpFonts\"");
        htmlSource.Should().Contain("id=\"replaceImguiFonts\"");
        htmlSource.Should().Contain("id=\"autoUseCjkFallbackFonts\"");
        htmlSource.Should().Contain("id=\"replacementFontName\"");
        htmlSource.Should().Contain("id=\"replacementFontFile\"");
        htmlSource.Should().Contain("id=\"fontSamplingPointSize\"");
    }

    [Fact]
    public void Plugin_settings_explain_empty_font_fields_use_automatic_selection()
    {
        var htmlSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.Plugin", "Web", "ControlPanelHtml.cs"));

        htmlSource.Should().Contain("placeholder=\"留空自动选择，如 Microsoft YaHei / Noto Sans SC\"");
        htmlSource.Should().Contain("placeholder=\"留空自动选择，如 C:\\Windows\\Fonts\\msyh.ttc\"");
    }

    [Fact]
    public void Plugin_settings_show_automatic_font_fallbacks_as_placeholders_only()
    {
        var htmlSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.Plugin", "Web", "ControlPanelHtml.cs"));

        htmlSource.Should().Contain("const defaultFontNamePlaceholder");
        htmlSource.Should().Contain("const defaultFontFilePlaceholder");
        htmlSource.Should().Contain("function applyAutomaticFontPlaceholders(state)");
        htmlSource.Should().Contain("state.AutomaticReplacementFontName");
        htmlSource.Should().Contain("state.AutomaticReplacementFontFile");
        htmlSource.Should().Contain("applyAutomaticFontPlaceholders(state);");
        htmlSource.Should().Contain("ReplacementFontName: $(\"replacementFontName\").value");
        htmlSource.Should().Contain("ReplacementFontFile: $(\"replacementFontFile\").value");
        htmlSource.Should().NotContain("ReplacementFontName: state.AutomaticReplacementFontName");
        htmlSource.Should().NotContain("ReplacementFontFile: state.AutomaticReplacementFontFile");
    }

    [Fact]
    public void Font_replacement_service_retries_tmp_candidates_and_caches_failures()
    {
        var serviceSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.Plugin", "Unity", "UnityTextFontReplacementService.cs"));

        serviceSource.Should().Contain("_failedTmpFontAssetKeys");
        serviceSource.Should().Contain("ResolveTmpFontAsset");
        serviceSource.Should().Contain("EnumerateFontCandidates");
        serviceSource.Should().Contain("foreach (var candidate in EnumerateFontCandidates(config, key, context))");
        serviceSource.Should().Contain("TMP fallback font asset could not be created from any candidate");
        serviceSource.Should().Contain("Action<string?, string?> automaticFontFallbackReporter");
        serviceSource.Should().Contain("ReportAutomaticFontFallbacks(config);");
        serviceSource.Should().Contain("ResolveFirstUsableAutomaticFontName");
        serviceSource.Should().Contain("ResolveFirstUsableAutomaticFontFile");
    }

    [Fact]
    public void Translation_editor_exposes_component_font_override_column()
    {
        var htmlSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.Plugin", "Web", "ControlPanelHtml.cs"));

        htmlSource.Should().Contain("key: \"ReplacementFont\"");
        htmlSource.Should().Contain("title: \"替换字体\"");
        htmlSource.Should().Contain("sort: \"replacement_font\"");
    }

    [Fact]
    public void Local_http_server_exposes_translation_column_filter_options_endpoint()
    {
        var serverSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.Plugin", "Web", "LocalHttpServer.cs"));

        serverSource.Should().Contain("path == \"/api/translations/filter-options\"");
        serverSource.Should().Contain("ParseTranslationFilterOptionsQuery");
        serverSource.Should().Contain("ParseColumnFilters");
        serverSource.Should().Contain("filter.");
        serverSource.Should().Contain("TranslationCacheColumns.EmptyValueMarker");
    }

    [Fact]
    public void Translation_editor_exposes_excel_style_column_filters()
    {
        var htmlSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.Plugin", "Web", "ControlPanelHtml.cs"));

        htmlSource.Should().Contain("id=\"clearTableFilters\"");
        htmlSource.Should().Contain("id=\"columnFilterMenu\"");
        htmlSource.Should().Contain("hunity.editor.columnFilters");
        htmlSource.Should().Contain("function openColumnFilterMenu(");
        htmlSource.Should().Contain("function loadColumnFilterOptions(");
        htmlSource.Should().Contain("function appendColumnFilters(");
        htmlSource.Should().Contain("/api/translations/filter-options");
        htmlSource.Should().Contain("filter-active");
        htmlSource.Should().Contain("data-filter-column");
    }

    [Fact]
    public void Control_panel_exposes_translation_context_settings()
    {
        var htmlSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.Plugin", "Web", "ControlPanelHtml.cs"));

        htmlSource.Should().Contain("id=\"enableTranslationContext\"");
        htmlSource.Should().Contain("id=\"translationContextMaxExamples\"");
        htmlSource.Should().Contain("id=\"translationContextMaxCharacters\"");
        htmlSource.Should().Contain("EnableTranslationContext");
        htmlSource.Should().Contain("TranslationContextMaxExamples");
        htmlSource.Should().Contain("TranslationContextMaxCharacters");
    }

    [Fact]
    public void Control_panel_exposes_glossary_page_settings_and_crud_endpoints()
    {
        var htmlSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.Plugin", "Web", "ControlPanelHtml.cs"));
        var serverSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.Plugin", "Web", "LocalHttpServer.cs"));
        var pluginSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.Plugin", "Plugin.cs"));

        htmlSource.Should().Contain("data-page=\"glossary\"");
        htmlSource.Should().Contain("id=\"page-glossary\"");
        htmlSource.Should().Contain("id=\"enableGlossary\"");
        htmlSource.Should().Contain("id=\"enableAutoTermExtraction\"");
        htmlSource.Should().Contain("AI 自动提取默认关闭");
        htmlSource.Should().Contain("function loadGlossaryTerms()");
        htmlSource.Should().Contain("function saveGlossaryTerm(");
        htmlSource.Should().Contain("function deleteGlossaryTerm(");
        htmlSource.Should().Contain("/api/glossary");

        serverSource.Should().Contain("path == \"/api/glossary\"");
        serverSource.Should().Contain("IGlossaryStore");
        serverSource.Should().Contain("GlossaryQuery");

        pluginSource.Should().Contain("translation-glossary.sqlite");
        pluginSource.Should().Contain("SqliteGlossaryStore");
    }

    [Fact]
    public void Translation_editor_exposes_selected_row_retranslate_action()
    {
        var htmlSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.Plugin", "Web", "ControlPanelHtml.cs"));
        var serverSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.Plugin", "Web", "LocalHttpServer.cs"));

        htmlSource.Should().Contain("data-table-action=\"retranslate\"");
        htmlSource.Should().Contain("function retranslateSelectedRows()");
        htmlSource.Should().Contain("/api/translations/retranslate");
        serverSource.Should().Contain("path == \"/api/translations/retranslate\"");
        serverSource.Should().Contain("TranslationJob.Create");
    }

    [Fact]
    public void Translation_editor_exposes_selected_row_highlight_action()
    {
        var htmlSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.Plugin", "Web", "ControlPanelHtml.cs"));
        var serverSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.Plugin", "Web", "LocalHttpServer.cs"));

        htmlSource.Should().Contain("data-table-action=\"highlight\"");
        htmlSource.Should().Contain("function highlightSelectedRow()");
        htmlSource.Should().Contain("/api/translations/highlight");
        serverSource.Should().Contain("path == \"/api/translations/highlight\"");
        serverSource.Should().Contain("TranslationHighlightRequest.FromEntry");
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

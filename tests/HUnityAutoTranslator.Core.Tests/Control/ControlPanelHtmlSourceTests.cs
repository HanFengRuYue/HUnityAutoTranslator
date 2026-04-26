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
        htmlSource.Should().Contain("已获取 ${models.length} 个模型");
        htmlSource.Should().NotContain("id=\"providerUtilityMessage\"");
        htmlSource.Should().NotContain("id=\"providerUtilityOutput\"");
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

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

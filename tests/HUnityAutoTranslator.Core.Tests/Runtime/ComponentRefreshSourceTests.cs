using FluentAssertions;

namespace HUnityAutoTranslator.Core.Tests.Runtime;

public sealed class ComponentRefreshSourceTests
{
    [Fact]
    public void Worker_pool_publishes_component_context_with_completed_translation_results()
    {
        var source = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.Core", "Queueing", "TranslationWorkerPool.cs"));

        source.Should().Contain("sceneName: jobs[i].Context.SceneName");
        source.Should().Contain("componentHierarchy: jobs[i].Context.ComponentHierarchy");
        source.Should().Contain("componentType: jobs[i].Context.ComponentType");
        source.Should().Contain("updatedUtc: resultUpdatedUtc");
    }

    [Fact]
    public void Manual_translation_save_publishes_context_refresh_even_when_target_id_is_not_resolved()
    {
        var source = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.Plugin", "Web", "LocalHttpServer.cs"));

        source.Should().Contain("var targetId = string.Empty;");
        source.Should().Contain("_highlighter?.TryResolveTargetId(TranslationHighlightRequest.FromEntry(entry), out targetId);");
        source.Should().Contain("sceneName: entry.SceneName");
        source.Should().Contain("componentHierarchy: entry.ComponentHierarchy");
        source.Should().Contain("componentType: entry.ComponentType");
        source.Should().Contain("updatedUtc: entry.UpdatedUtc");
        source.Should().NotContain("_highlighter == null ||");
        source.Should().NotContain("!_highlighter.TryResolveTargetId(TranslationHighlightRequest.FromEntry(entry), out var targetId)");
    }

    [Fact]
    public void Unity_applier_can_resolve_refresh_results_by_component_context()
    {
        var source = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.Plugin", "Unity", "UnityMainThreadResultApplier.cs"));

        source.Should().Contain("_pendingComponentRefreshes");
        source.Should().Contain("TryFindTarget(result, out var target)");
        source.Should().Contain("TryFindTargetByComponentContext");
        source.Should().Contain("ComponentContextMatches(result, target)");
        source.Should().Contain("RememberPendingComponentRefresh(result)");
    }

    [Fact]
    public void Unity_applier_adjusts_font_size_from_original_target_size()
    {
        var source = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.Plugin", "Unity", "UnityMainThreadResultApplier.cs"));
        var targetSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.Plugin", "Unity", "IUnityTextTarget.cs"));

        source.Should().Contain("_originalFontSizes");
        source.Should().Contain("RememberOriginalFontSize(target)");
        source.Should().Contain("FontSizeAdjustment.Calculate(");
        source.Should().Contain("originalSize,");
        source.Should().Contain("RestoreOriginalFontSize(target)");
        source.Should().Contain("已调整 {target.ComponentType} 的字号");
        targetSource.Should().Contain("bool TryGetFontSize(out float fontSize)");
        targetSource.Should().Contain("bool TrySetFontSize(float fontSize)");
    }

    [Fact]
    public void Translation_import_publishes_refreshes_for_rows_modified_since_import_start()
    {
        var source = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.Plugin", "Web", "LocalHttpServer.cs"));

        source.Should().Contain("var refreshSince = DateTimeOffset.UtcNow;");
        source.Should().Contain("var refreshQueued = PublishUpdatedWritebacks(refreshSince);");
        source.Should().Contain("RefreshQueuedCount = refreshQueued");
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

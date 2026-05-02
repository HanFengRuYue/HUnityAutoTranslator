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
    public void Manual_translation_clear_and_delete_publish_source_restore_writeback()
    {
        var source = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.Plugin", "Web", "LocalHttpServer.cs"));

        source.Should().Contain("NormalizeManualEntry(entry)");
        source.Should().Contain("PublishRestoreSourceWriteback(entry, previousTranslatedText);");
        source.Should().Contain("PublishRestoreSourceWriteback(entry, previousTranslatedText)");
        source.Should().Contain("previousTranslatedText: removedTranslatedText");
        source.Should().Contain("restoreSourceText: true");
        source.Should().Contain("entry.SourceText,");
        source.Should().Contain("_cache.Delete(entry);");
    }

    [Fact]
    public void Unity_applier_restores_source_text_without_remembering_source_as_translation()
    {
        var source = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.Plugin", "Unity", "UnityMainThreadResultApplier.cs"));

        source.Should().Contain("result.RestoreSourceText");
        source.Should().Contain("ApplyRestoreSourceToTarget(result, target)");
        source.Should().Contain("_writebacks.TryRestoreSourceText(");
        source.Should().Contain("RestoreOriginalFontSize(target)");
    }

    [Fact]
    public void Text_scanners_reread_text_after_registration_before_skip_and_pipeline()
    {
        AssertScannerRereadsAfterRegister(File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.Plugin", "Capture", "TmpTextScanner.cs")));
        AssertScannerRereadsAfterRegister(File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.Plugin", "Capture", "UguiTextScanner.cs")));
    }

    [Fact]
    public void Text_scanners_apply_component_fonts_only_after_translated_text_is_available()
    {
        AssertComponentFontReplacementWaitsForTranslatedText(
            File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.Plugin", "Capture", "TmpTextScanner.cs")),
            "_fontReplacement?.ApplyToTmp(component,",
            "_fontReplacement?.ApplyToTmp(component, rememberedKey, context, text);",
            "_fontReplacement?.ApplyToTmp(component, key, context, decision.TranslatedText);");
        AssertComponentFontReplacementWaitsForTranslatedText(
            File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.Plugin", "Capture", "UguiTextScanner.cs")),
            "_fontReplacement?.ApplyToUgui(component,",
            "_fontReplacement?.ApplyToUgui(component, rememberedKey, context, text);",
            "_fontReplacement?.ApplyToUgui(component, key, context, decision.TranslatedText);");

        var imguiSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.Plugin", "Capture", "ImguiHookInstaller.cs"));
        imguiSource.Should().Contain("ResolveForDraw(sourceText)");
        imguiSource.Should().Contain("BeginImguiDrawFontScope(__originalMethod, __args, key, context, resolution.DisplayText)");
        imguiSource.Should().Contain("PostfixStringText(UnityTextFontReplacementService.ImguiFontScope? __state)");
        imguiSource.Should().Contain("__state?.Dispose();");
        imguiSource.Should().NotContain("RequestImguiFont(");
        imguiSource.Should().NotContain("ApplyPendingImguiFontForDraw");
        imguiSource.Should().NotContain("_fontReplacement.ApplyToImgui(");
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

    private static void AssertScannerRereadsAfterRegister(string source)
    {
        var registerIndex = source.IndexOf("_applier.Register(target);", StringComparison.Ordinal);
        registerIndex.Should().BeGreaterThanOrEqualTo(0);

        var rereadIndex = source.IndexOf("text = target.GetText();", registerIndex, StringComparison.Ordinal);
        rereadIndex.Should().BeGreaterThan(registerIndex);

        var rememberedCheckIndex = source.IndexOf("if (_applier.IsRememberedTranslation(target.Id, text))", StringComparison.Ordinal);
        rememberedCheckIndex.Should().BeGreaterThan(rereadIndex);

        var pipelineIndex = source.IndexOf("new CapturedText(target.Id, text, target.IsVisible, context)", StringComparison.Ordinal);
        pipelineIndex.Should().BeGreaterThan(rereadIndex);
    }

    private static void AssertComponentFontReplacementWaitsForTranslatedText(
        string source,
        string applyCall,
        string rememberedTranslatedSampleCall,
        string cachedTranslatedSampleCall)
    {
        var firstApplyIndex = source.IndexOf(applyCall, StringComparison.Ordinal);
        firstApplyIndex.Should().BeGreaterThanOrEqualTo(0);

        var rememberedCheckIndex = source.IndexOf("if (_applier.IsRememberedTranslation(target.Id, text))", StringComparison.Ordinal);
        rememberedCheckIndex.Should().BeGreaterThanOrEqualTo(0);

        var cachedTranslationIndex = source.IndexOf("if (decision.Kind == PipelineDecisionKind.UseCachedTranslation && decision.TranslatedText != null)", StringComparison.Ordinal);
        cachedTranslationIndex.Should().BeGreaterThanOrEqualTo(0);

        firstApplyIndex.Should().BeGreaterThan(rememberedCheckIndex);
        source.IndexOf(applyCall, cachedTranslationIndex, StringComparison.Ordinal)
            .Should().BeGreaterThan(cachedTranslationIndex);
        source.Should().Contain(rememberedTranslatedSampleCall);
        source.Should().Contain(cachedTranslatedSampleCall);
    }
}

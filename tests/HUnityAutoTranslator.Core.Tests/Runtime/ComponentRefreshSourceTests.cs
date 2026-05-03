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
    public void Manual_translation_save_publishes_context_refresh_even_when_only_component_font_changed()
    {
        var source = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.Plugin", "Web", "LocalHttpServer.cs"));
        var publishBlock = source[
            source.IndexOf("private bool PublishManualWriteback", StringComparison.Ordinal)..
            source.IndexOf("private bool PublishRestoreSourceWriteback", StringComparison.Ordinal)];

        publishBlock.Should().NotContain("string.Equals(entry.TranslatedText, previousTranslatedText");
        publishBlock.Should().Contain("targetLanguage: entry.TargetLanguage");
        publishBlock.Should().Contain("componentType: entry.ComponentType");
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
    public void Text_scanners_refresh_cached_translations_after_stable_release_without_queueing()
    {
        AssertScannerUsesCacheOnlyRefresh(File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.Plugin", "Capture", "TmpTextScanner.cs")));
        AssertScannerUsesCacheOnlyRefresh(File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.Plugin", "Capture", "UguiTextScanner.cs")));
        AssertScannerUsesCacheOnlyRefresh(File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.Plugin", "Capture", "UnityTextTargetProcessor.cs")));
    }

    [Fact]
    public void Text_change_processor_passes_translated_text_to_component_font_replacement()
    {
        var source = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.Plugin", "Capture", "UnityTextTargetProcessor.cs"));
        var applyBlock = source[
            source.IndexOf("private void ApplyFont(", StringComparison.Ordinal)..
            source.IndexOf("private void RestoreFont", StringComparison.Ordinal)];

        source.Should().Contain("ApplyFont(component, targetKind, rememberedKey, context, text);");
        source.Should().Contain("ApplyFont(component, targetKind, key, context, decision.TranslatedText);");
        applyBlock.Should().Contain("string translatedText");
        applyBlock.Should().Contain("_fontReplacement?.ApplyToUgui(component, key, context, translatedText);");
        applyBlock.Should().Contain("_fontReplacement?.ApplyToTmp(component, key, context, translatedText);");
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
    public void Unity_applier_keeps_context_refresh_pending_when_current_text_temporarily_mismatches()
    {
        var source = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.Plugin", "Unity", "UnityMainThreadResultApplier.cs"));
        var normalized = source.Replace("\r\n", "\n", StringComparison.Ordinal);

        source.Should().Contain("if (!ApplyResultToTarget(result, target))");
        source.Should().Contain("RememberPendingComponentRefresh(result);");
        normalized.Should().NotContain("_pendingComponentRefreshes.Remove(key);\n            if (ApplyResultToTarget(result, target))");
        normalized.Should().Contain("if (ApplyResultToTarget(result, target))\n            {\n                break;\n            }");
    }

    [Fact]
    public void Unity_applier_reapplies_component_font_after_context_refresh()
    {
        var applierSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.Plugin", "Unity", "UnityMainThreadResultApplier.cs"));
        var runtimeSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.Plugin", "PluginRuntime.cs"));
        var targetSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.Plugin", "Unity", "IUnityTextTarget.cs"));
        var reflectionTargetSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.Plugin", "Capture", "ReflectionTextTarget.cs"));

        applierSource.Should().Contain("public void SetFontReplacementService(UnityTextFontReplacementService? fontReplacement)");
        applierSource.Should().Contain("ApplyFontForResult(result, target)");
        applierSource.Should().Contain("_fontReplacement?.ApplyToTmp(target.Component, key, context, result.TranslatedText)");
        applierSource.Should().Contain("_fontReplacement?.ApplyToUgui(target.Component, key, context, result.TranslatedText)");
        runtimeSource.Should().Contain("_resultApplier.SetFontReplacementService(_fontReplacement);");
        targetSource.Should().Contain("UnityEngine.Object Component { get; }");
        reflectionTargetSource.Should().Contain("public UnityEngine.Object Component => _component;");
    }

    [Fact]
    public void Unity_applier_captures_tmp_visible_color_before_translated_writeback()
    {
        var applierSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.Plugin", "Unity", "UnityMainThreadResultApplier.cs"));
        var serviceSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.Plugin", "Unity", "UnityTextFontReplacementService.cs"));
        var tryApplyBlock = applierSource[
            applierSource.IndexOf("private bool TryApplyRemembered", StringComparison.Ordinal)..
            applierSource.IndexOf("private void CaptureTmpVisibleColorBeforeWriteback", StringComparison.Ordinal)];
        var toggleBlock = applierSource[
            applierSource.IndexOf("public int SetTranslatedTextMode", StringComparison.Ordinal)..
            applierSource.IndexOf("private bool TryFindTarget", StringComparison.Ordinal)];

        serviceSource.Should().Contain("public void CaptureTmpVisibleColor(UnityEngine.Object component)");
        applierSource.Should().Contain("private void CaptureTmpVisibleColorBeforeWriteback(IUnityTextTarget target)");
        applierSource.Should().Contain("_fontReplacement.CaptureTmpVisibleColor(target.Component);");
        applierSource.Should().Contain("_writebacks.IsRememberedSourceText(target.Id, currentText)");
        tryApplyBlock.IndexOf("CaptureTmpVisibleColorBeforeWriteback(target);", StringComparison.Ordinal)
            .Should().BeGreaterThan(tryApplyBlock.IndexOf("_writebacks.TryGetDisplayText(", StringComparison.Ordinal));
        tryApplyBlock.IndexOf("_writebacks.IsRememberedSourceText(target.Id, currentText)", StringComparison.Ordinal)
            .Should().BeLessThan(tryApplyBlock.IndexOf("CaptureTmpVisibleColorBeforeWriteback(target);", StringComparison.Ordinal));
        tryApplyBlock.IndexOf("CaptureTmpVisibleColorBeforeWriteback(target);", StringComparison.Ordinal)
            .Should().BeLessThan(tryApplyBlock.IndexOf("target.SetText(replacement);", StringComparison.Ordinal));
        toggleBlock.IndexOf("_writebacks.IsRememberedSourceText(target.Id, currentText)", StringComparison.Ordinal)
            .Should().BeLessThan(toggleBlock.IndexOf("CaptureTmpVisibleColorBeforeWriteback(target);", StringComparison.Ordinal));
        toggleBlock.IndexOf("CaptureTmpVisibleColorBeforeWriteback(target);", StringComparison.Ordinal)
            .Should().BeLessThan(toggleBlock.IndexOf("target.SetText(replacement);", StringComparison.Ordinal));
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

    private static void AssertScannerUsesCacheOnlyRefresh(string source)
    {
        source.Should().Contain("StableTextDecisionKind.RefreshCachedTranslation");
        source.Should().Contain("_pipeline.ResolveCachedTranslationOnly(");
        source.Should().Contain("PipelineDecisionKind.UseCachedTranslation");
    }
}

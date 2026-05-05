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
    public void Ugui_tmp_paths_try_exact_cache_hits_before_stability_wait()
    {
        AssertExactCacheHitPrecedesStabilityWait(File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.Plugin", "Capture", "TmpTextScanner.cs")));
        AssertExactCacheHitPrecedesStabilityWait(File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.Plugin", "Capture", "UguiTextScanner.cs")));
        AssertExactCacheHitPrecedesStabilityWait(File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.Plugin", "Capture", "UnityTextTargetProcessor.cs")));
    }

    [Fact]
    public void Unity_text_change_hook_keeps_global_reflection_scans_throttled()
    {
        var hookSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.Plugin", "Capture", "UnityTextChangeHookInstaller.cs"));
        var runtimeSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.Plugin", "PluginRuntime.cs"));
        var queueSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.Plugin", "Capture", "UnityTextChangeQueue.cs"));
        var textChangedBlock = hookSource[
            hookSource.IndexOf("private static void PostfixTextChanged", StringComparison.Ordinal)..
            hookSource.IndexOf("private static void PostfixGameObjectSetActive", StringComparison.Ordinal)];

        hookSource.Should().Contain("Action _requestGlobalTextScan");
        textChangedBlock.Should().NotContain("_requestGlobalTextScan();");
        textChangedBlock.Should().Contain("TryGetChangedText(__args, out var changedText)");
        textChangedBlock.Should().Contain("EnqueueChangedText(component, changedText)");
        hookSource.Should().Contain("_changeQueue.Enqueue(");
        textChangedBlock.Should().NotContain("ProcessChangedText(component)");
        queueSource.Should().Contain("public int Drain(");
        queueSource.Should().Contain("Stopwatch.StartNew()");
        hookSource.Should().Contain("PatchGameObjectSetActive");
        hookSource.Should().Contain("PostfixGameObjectSetActive");
        hookSource.Should().Contain("PostfixGameObjectSetActive(bool value)");
        hookSource.Should().NotContain("PostfixGameObjectSetActive(bool active)");
        runtimeSource.Should().Contain("RequestGlobalTextScan");
        runtimeSource.Should().Contain("GlobalTextScanDebounceSeconds");
        runtimeSource.Should().Contain("TextHookIdleGlobalScanSeconds");
        runtimeSource.Should().Contain("hookIdleFallbackReady");
        runtimeSource.Should().NotContain("Time.unscaledTime < _fastReflectionScanUntil");
        runtimeSource.Should().Contain("textHooksEnabled && !runReflectionScan");
        runtimeSource.Should().NotContain("ReflectionScanIntervalWhenTextHooksEnabledSeconds = 2f");
    }

    [Fact]
    public void Text_change_hook_prefilters_raw_text_before_reflection_target_creation()
    {
        var hookSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.Plugin", "Capture", "UnityTextChangeHookInstaller.cs"));
        var processorSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.Plugin", "Capture", "UnityTextTargetProcessor.cs"));
        var processorBlock = processorSource[
            processorSource.IndexOf("public UnityTextProcessResult Process(", StringComparison.Ordinal)..
            processorSource.IndexOf("private void RunSuppressed", StringComparison.Ordinal)];

        hookSource.Should().Contain("TryGetChangedText(__args, out var changedText)");
        hookSource.Should().Contain("UnityTextTargetProcessor.ShouldSkipRawText(changedText, config)");
        processorSource.Should().Contain("public static bool ShouldSkipRawText(string? text, RuntimeConfig config)");
        processorBlock.Should().Contain("if (observedText != null && ShouldSkipRawText(observedText, config))");
        processorBlock.IndexOf("ShouldSkipRawText(observedText, config)", StringComparison.Ordinal)
            .Should().BeLessThan(processorBlock.IndexOf("GetOrCreateTarget", StringComparison.Ordinal));
        processorBlock.Should().NotContain("new ReflectionTextTarget(component, textProperty)");
    }

    [Fact]
    public void Queued_text_changes_retry_after_stability_wait_without_global_scan()
    {
        var runtimeSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.Plugin", "PluginRuntime.cs"));
        var queueSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.Plugin", "Capture", "UnityTextChangeQueue.cs"));
        var processorSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.Plugin", "Capture", "UnityTextTargetProcessor.cs"));

        processorSource.Should().Contain("UnityTextProcessResult.WaitForStability");
        processorSource.Should().Contain("return UnityTextProcessResult.WaitForStability;");
        queueSource.Should().Contain("RequeueForStability");
        queueSource.Should().Contain("ReadyTime");
        queueSource.Should().Contain("item.ReadyTime > now");
        runtimeSource.Should().Contain("FastStaticTextRetrySeconds");
        runtimeSource.Should().Contain("_textTargetProcessor?.Process(");
        runtimeSource.Should().Contain("if (result == UnityTextProcessResult.WaitForStability)");
        runtimeSource.Should().Contain("RequeueForStability(item, Time.unscaledTime + FastStaticTextRetrySeconds)");
        runtimeSource.Should().NotContain("RequestGlobalTextScan();\r\n        _textChangeQueue.RequeueForStability");
    }

    [Fact]
    public void Global_scanners_requeue_static_text_after_stability_wait()
    {
        var runtimeSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.Plugin", "PluginRuntime.cs"));
        var tmpSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.Plugin", "Capture", "TmpTextScanner.cs"));
        var uguiSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.Plugin", "Capture", "UguiTextScanner.cs"));

        AssertScannerRequeuesStabilityWait(tmpSource, "UnityTextTargetKind.Tmp");
        AssertScannerRequeuesStabilityWait(uguiSource, "UnityTextTargetKind.Ugui");
        runtimeSource.Should().Contain("new UguiTextScanner(pipeline, _resultApplier, _logger, _controlPanel.GetConfig, _fontReplacement, textStabilityGate, textTargetRegistry, _textChangeQueue)");
        runtimeSource.Should().Contain("new TmpTextScanner(pipeline, _resultApplier, _logger, _controlPanel.GetConfig, _fontReplacement, textStabilityGate, textTargetRegistry, _textChangeQueue)");
    }

    [Fact]
    public void Text_targets_cache_metadata_and_reflection_members_for_repeated_hook_processing()
    {
        var registrySource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.Plugin", "Capture", "UnityTextTargetRegistry.cs"));
        var targetSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.Plugin", "Capture", "ReflectionTextTarget.cs"));
        var processorSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.Plugin", "Capture", "UnityTextTargetProcessor.cs"));

        registrySource.Should().Contain("_targets");
        registrySource.Should().Contain("GetOrCreateTarget");
        registrySource.Should().Contain("InvalidateMetadata");
        registrySource.Should().Contain("RecordTextTargetMetadataBuild");
        processorSource.Should().Contain("_targetRegistry.GetOrCreateTarget(component, textProperty)");
        targetSource.Should().Contain("_cachedSceneName");
        targetSource.Should().Contain("_cachedHierarchyPath");
        targetSource.Should().Contain("InvalidateMetadata()");
        targetSource.Should().Contain("ReflectionTextMemberCache");
        targetSource.Should().Contain("_memberCache");
    }

    [Fact]
    public void Repeated_font_and_layout_state_is_skipped_for_same_target_text_and_config()
    {
        var applierSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.Plugin", "Unity", "UnityMainThreadResultApplier.cs"));
        var fontSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.Plugin", "Unity", "UnityTextFontReplacementService.cs"));

        applierSource.Should().Contain("AppliedUnityTextStateCache");
        applierSource.Should().Contain("_layoutStateCache.TrySkip");
        applierSource.Should().Contain("_metrics?.RecordLayoutApplicationSkipped()");
        applierSource.Should().Contain("_metrics?.RecordLayoutApplication()");
        fontSource.Should().Contain("_uguiAppliedFontStates");
        fontSource.Should().Contain("_tmpAppliedFontStates");
        fontSource.Should().Contain("RecordFontApplicationSkipped");
        fontSource.Should().Contain("RecordTmpMeshForceUpdate");
        fontSource.Should().Contain("BuildAppliedFontStateKey");
    }

    [Fact]
    public void Unity_applier_skips_repeated_layout_work_for_unchanged_registered_targets()
    {
        var source = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.Plugin", "Unity", "UnityMainThreadResultApplier.cs"));
        var registerBlock = source[
            source.IndexOf("public void Register(IUnityTextTarget target)", StringComparison.Ordinal)..
            source.IndexOf("public IReadOnlyList<TranslationHighlightTarget> SnapshotTargets()", StringComparison.Ordinal)];

        source.Should().Contain("_registeredTextSnapshots");
        registerBlock.Should().Contain("IsSameRegisteredText(target.Id, currentText)");
        registerBlock.Should().Contain("return;");
        registerBlock.Should().Contain("RememberRegisteredText(target.Id, currentText)");
        registerBlock.Should().NotContain("ApplyCurrentFontSizeState(target);\r\n        TryApplyPendingComponentRefresh(target);");
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
    public void Unity_applier_uses_tmp_native_auto_size_before_manual_overflow_fallback()
    {
        var source = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.Plugin", "Unity", "UnityMainThreadResultApplier.cs"));
        var applyResultBlock = source[
            source.IndexOf("private bool ApplyResultToTarget", StringComparison.Ordinal)..
            source.IndexOf("private bool ApplyRestoreSourceToTarget", StringComparison.Ordinal)];
        var fontStateBlock = source[
            source.IndexOf("private void ApplyFontSizeState", StringComparison.Ordinal)..
            source.IndexOf("private void RestoreOriginalFontSize", StringComparison.Ordinal)];

        applyResultBlock.Should().Contain("if (appliedFont)");
        applyResultBlock.Should().Contain("ApplyFontSizeState(target, translatedTextIsActive: _useTranslatedText);");
        fontStateBlock.Should().Contain("if (config.EnableTmpNativeAutoSize)");
        fontStateBlock.Should().Contain("TryApplyTmpNativeAutoSize(target, desiredSize)");
        fontStateBlock.Should().Contain("TryAutoShrinkTmpOverflowingTranslatedText(target, originalSize);");
        fontStateBlock.IndexOf("TryApplyTmpNativeAutoSize(target, desiredSize)", StringComparison.Ordinal)
            .Should().BeLessThan(fontStateBlock.IndexOf("TryAutoShrinkTmpOverflowingTranslatedText(target, originalSize);", StringComparison.Ordinal));
        source.Should().Contain("_originalTmpAutoSizeStates");
        source.Should().Contain("fontSizeMax");
        source.Should().Contain("fontSizeMin");
        source.Should().Contain("enableAutoSizing");
        source.Should().Contain("ForceMeshUpdate");
        source.Should().Contain("private bool TryAutoShrinkTmpOverflowingTranslatedText");
        source.Should().Contain("IsTmpTarget(target.ComponentType)");
        source.Should().Contain("IsTmpTruncateOverflowMode(target.Component)");
        source.Should().Contain("ReadTmpBool(target.Component, \"isTextOverflowing\")");
        source.Should().Contain("ReadTmpBool(target.Component, \"isTextTruncated\")");
        source.Should().Contain("ReadTmpFloat(target.Component, \"preferredHeight\", out var preferredHeight)");
        source.Should().Contain("TryGetTmpRectHeight(target.Component, out var rectHeight)");
        source.Should().Contain("target.TrySetFontSize(shrunkSize)");
        source.Should().Contain("RefreshTmpLayout(target.Component);");
        source.Should().Contain("RestoreOriginalFontSize(target)");
    }

    [Fact]
    public void Unity_applier_preserves_and_restores_translated_text_layout_spacing()
    {
        var source = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.Plugin", "Unity", "UnityMainThreadResultApplier.cs"));
        var targetSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.Plugin", "Unity", "IUnityTextTarget.cs"));
        var reflectionTargetSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.Plugin", "Capture", "ReflectionTextTarget.cs"));
        var hotkeySource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.Plugin", "Hotkeys", "RuntimeHotkeyController.cs"));
        var uguiScannerSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.Plugin", "Capture", "UguiTextScanner.cs"));
        var tmpScannerSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.Plugin", "Capture", "TmpTextScanner.cs"));
        var processorSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.Plugin", "Capture", "UnityTextTargetProcessor.cs"));

        source.Should().Contain("_originalTextLayoutBaselines");
        source.Should().Contain("RememberOriginalTextLayoutBaseline(target)");
        source.Should().Contain("public void ApplyCurrentTextLayoutState(IUnityTextTarget target)");
        source.Should().Contain("ApplyTranslatedTextLayoutState(target, desiredSize)");
        source.Should().Contain("RestoreOriginalTextLayoutState(target)");
        source.Should().Contain("TryApplyUguiLineSpacingCompensation(target, baseline)");
        source.Should().Contain("TryApplyTmpLineSpacingCompensation(target, baseline)");
        source.Should().Contain("TryAutoShrinkUguiOverflowingTranslatedText(target, originalSize)");
        source.Should().Contain("lineSpacing");
        source.Should().Contain("paragraphSpacing");
        source.Should().Contain("lineSpacingAdjustment");
        source.Should().Contain("TextLayoutCompensation.TryCalculateUguiLineSpacing(");
        source.Should().Contain("TextLayoutCompensation.TryCalculateHeightFitFontSize(");

        targetSource.Should().Contain("bool TryGetLineSpacing(out float lineSpacing)");
        targetSource.Should().Contain("bool TrySetLineSpacing(float lineSpacing)");
        targetSource.Should().Contain("bool TryGetFontLineHeight(out float lineHeight)");
        targetSource.Should().Contain("bool TryGetPreferredHeight(out float preferredHeight)");
        targetSource.Should().Contain("bool TryGetRenderedHeight(out float renderedHeight)");
        targetSource.Should().Contain("bool TryGetRectHeight(out float rectHeight)");

        reflectionTargetSource.Should().Contain("public bool TryGetLineSpacing(out float lineSpacing)");
        reflectionTargetSource.Should().Contain("public bool TrySetLineSpacing(float lineSpacing)");
        reflectionTargetSource.Should().Contain("public bool TryGetFontLineHeight(out float lineHeight)");
        reflectionTargetSource.Should().Contain("public bool TryGetPreferredHeight(out float preferredHeight)");
        reflectionTargetSource.Should().Contain("public bool TryGetRenderedHeight(out float renderedHeight)");
        reflectionTargetSource.Should().Contain("public bool TryGetRectHeight(out float rectHeight)");

        hotkeySource.Should().Contain("_resultApplier.ReapplyTextLayoutState(int.MaxValue)");
        uguiScannerSource.Should().Contain("_applier.ApplyCurrentTextLayoutState(target);");
        tmpScannerSource.Should().Contain("_applier.ApplyCurrentTextLayoutState(target);");
        processorSource.Should().Contain("_applier.ApplyCurrentTextLayoutState(target);");
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

    private static void AssertExactCacheHitPrecedesStabilityWait(string source)
    {
        var exactCacheIndex = source.IndexOf("TryApplyExactCachedTranslation", StringComparison.Ordinal);
        exactCacheIndex.Should().BeGreaterThanOrEqualTo(0);
        source.IndexOf("_pipeline.ResolveExactCachedTranslation(", exactCacheIndex, StringComparison.Ordinal)
            .Should().BeGreaterThan(exactCacheIndex);
        source.IndexOf("RememberAndApply(target, text, decision.TranslatedText)", exactCacheIndex, StringComparison.Ordinal)
            .Should().BeGreaterThan(exactCacheIndex);

        var stableIndex = source.IndexOf("var stableDecision = EvaluateStableText", StringComparison.Ordinal);
        stableIndex.Should().BeGreaterThan(exactCacheIndex);
    }

    private static void AssertScannerRequeuesStabilityWait(string source, string targetKind)
    {
        source.Should().Contain("UnityTextChangeQueue? _changeQueue");
        source.Should().Contain("QueueStabilityRetry(component, text);");
        source.Should().Contain("new UnityTextChangeWorkItem(");
        source.Should().Contain(targetKind);
        source.Should().Contain("Time.unscaledTime + FastStaticTextRetrySeconds");
        source.Should().Contain("preferFastStaticRelease: true");

        var waitIndex = source.IndexOf("if (stableDecision == StableTextDecisionKind.Wait)", StringComparison.Ordinal);
        waitIndex.Should().BeGreaterThanOrEqualTo(0);
        source.IndexOf("QueueStabilityRetry(component, text);", waitIndex, StringComparison.Ordinal)
            .Should().BeGreaterThan(waitIndex);
        source.IndexOf("_pipeline.Process(capturedText)", StringComparison.Ordinal)
            .Should().BeGreaterThan(waitIndex);
    }
}

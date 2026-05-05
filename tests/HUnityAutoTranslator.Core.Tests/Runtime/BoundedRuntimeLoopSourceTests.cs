using FluentAssertions;

namespace HUnityAutoTranslator.Core.Tests.Runtime;

public sealed class BoundedRuntimeLoopSourceTests
{
    [Fact]
    public void Unity_text_scanners_use_round_robin_windows_for_bounded_object_scans()
    {
        var moduleSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.Plugin", "Capture", "ITextCaptureModule.cs"));
        var coordinatorSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.Plugin", "Capture", "TextCaptureCoordinator.cs"));
        var tmpSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.Plugin", "Capture", "TmpTextScanner.cs"));
        var uguiSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.Plugin", "Capture", "UguiTextScanner.cs"));
        var finderSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.Plugin", "Capture", "UnityObjectFinder.cs"));

        moduleSource.Should().Contain("bool UsesGlobalObjectScan");
        coordinatorSource.Should().Contain("skipGlobalObjectScanners");
        coordinatorSource.Should().Contain("module.UsesGlobalObjectScan");

        tmpSource.Should().Contain("RoundRobinCursor");
        tmpSource.Should().Contain("public bool UsesGlobalObjectScan => true;");
        tmpSource.Should().Contain("var maxTargets = forceFullScan ? objects.Length : _configProvider().MaxScanTargetsPerTick");
        tmpSource.Should().Contain("_scanCursor.TakeWindow(objects, maxTargets)");
        tmpSource.Should().Contain("UnityObjectFinder.FindObjects(_textType)");
        tmpSource.Should().NotContain("for (var i = 0; i < count; i++)");
        tmpSource.Should().NotContain("FindObjectsOfType(_textType)");

        uguiSource.Should().Contain("RoundRobinCursor");
        uguiSource.Should().Contain("public bool UsesGlobalObjectScan => true;");
        uguiSource.Should().Contain("var maxTargets = forceFullScan ? objects.Length : _configProvider().MaxScanTargetsPerTick");
        uguiSource.Should().Contain("_scanCursor.TakeWindow(objects, maxTargets)");
        uguiSource.Should().Contain("UnityObjectFinder.FindObjects(_textType)");
        uguiSource.Should().NotContain("for (var i = 0; i < count; i++)");
        uguiSource.Should().NotContain("FindObjectsOfType(_textType)");

        finderSource.Should().Contain("#if HUNITY_IL2CPP");
        finderSource.Should().Contain("MakeGenericMethod(type)");
        finderSource.Should().Contain("UnityEngine.Object.FindObjectsOfType(type)");
    }

    [Fact]
    public void Unity_writeback_reapply_scans_a_full_round_before_spending_the_writeback_budget()
    {
        var applierSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.Plugin", "Unity", "UnityMainThreadResultApplier.cs"));
        var reapplyBlock = applierSource[
            applierSource.IndexOf("public int ReapplyRemembered", StringComparison.Ordinal)..
            applierSource.IndexOf("private bool TryFindTarget", StringComparison.Ordinal)];

        applierSource.Should().Contain("_targetOrder");
        reapplyBlock.Should().Contain("EnumerateTargetsFromCursor()");
        reapplyBlock.Should().Contain("if (applied >= maxCount)");
        reapplyBlock.Should().NotContain("_targets.Values.ToArray()");
    }

    [Fact]
    public void Plugin_honors_reapply_remembered_translations_setting_without_full_per_frame_reapply()
    {
        var pluginSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.Plugin", "PluginRuntime.cs"));

        pluginSource.Should().Contain("if (config.ReapplyRememberedTranslations)");
        pluginSource.Should().Contain("_resultApplier.ReapplyRemembered(config.MaxWritebacksPerFrame)");
        pluginSource.Should().NotContain("_resultApplier.ReapplyRemembered(int.MaxValue)");
    }

    [Fact]
    public void Plugin_throttles_highlighter_snapshots_instead_of_copying_targets_every_frame()
    {
        var pluginSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.Plugin", "PluginRuntime.cs"));

        pluginSource.Should().Contain("MaybeRefreshHighlighterSnapshot(");
        pluginSource.Should().Contain("HighlighterSnapshotIntervalSeconds");
        pluginSource.Should().NotContain("_highlighter.RefreshTargetSnapshot(_resultApplier.SnapshotTargets());");
    }

    [Fact]
    public void Unity_text_change_hook_is_wired_for_low_latency_ugui_tmp_updates()
    {
        var hookSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.Plugin", "Capture", "UnityTextChangeHookInstaller.cs"));
        var runtimeSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.Plugin", "PluginRuntime.cs"));
        var processorSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.Plugin", "Capture", "UnityTextTargetProcessor.cs"));

        hookSource.Should().Contain("HarmonyId");
        hookSource.Should().Contain("PatchUguiTextSetter");
        hookSource.Should().Contain("PatchTmpTextEntryPoints");
        hookSource.Should().Contain("PostfixTextChanged");
        hookSource.Should().Contain("IsSuppressed");
        hookSource.Should().Contain("UnityTextTargetProcessor");
        processorSource.Should().Contain("RunSuppressed");
        runtimeSource.Should().Contain("UnityTextChangeHookInstaller");
        runtimeSource.Should().Contain("_textChangeHook?.Start();");
        hookSource.Should().Contain("public bool IsEnabled => _enabled;");
        runtimeSource.Should().Contain("ReflectionScanIntervalWhenTextHooksEnabledSeconds");
        runtimeSource.Should().Contain("_textChangeHook?.IsEnabled == true");
        runtimeSource.Should().Contain("skipGlobalObjectScanners: textHooksEnabled && !runReflectionScan");
    }

    [Fact]
    public void Memory_sensitive_plugin_paths_have_lifecycle_and_size_guards()
    {
        var fontSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.Plugin", "Unity", "UnityTextFontReplacementService.cs"));
        var textureSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.Plugin", "Unity", "UnityTextureReplacementService.cs"));
        var httpSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.Plugin", "Web", "LocalHttpServer.cs"));

        fontSource.Should().Contain("IDisposable");
        fontSource.Should().Contain("MaxImguiFontResolutionCacheEntries");
        fontSource.Should().Contain("RemoveOwnedTmpFallbacks");
        fontSource.Should().Contain("DestroyOwnedUnityObjects");
        fontSource.Should().Contain("GetMemoryDiagnostics");

        textureSource.Should().Contain("markNonReadable: true");
        textureSource.Should().NotContain("public byte[] PngBytes { get; }");
        textureSource.Should().Contain("PruneDeadTextureReferences");
        textureSource.Should().Contain("GetMemoryDiagnostics");

        httpSource.Should().Contain("MaxConcurrentHttpRequests");
        httpSource.Should().Contain("MaxJsonRequestBytes");
        httpSource.Should().Contain("MaxTextureArchiveRequestBytes");
        httpSource.Should().Contain("HandleWithConcurrencyLimitAsync");
        httpSource.Should().Contain("ImportOverridesAsync(boundedArchive");
    }

    [Fact]
    public void Ugui_tmp_capture_paths_share_stability_gate_before_entering_pipeline()
    {
        var runtimeSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.Plugin", "PluginRuntime.cs"));
        var processorSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.Plugin", "Capture", "UnityTextTargetProcessor.cs"));
        var tmpSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.Plugin", "Capture", "TmpTextScanner.cs"));
        var uguiSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.Plugin", "Capture", "UguiTextScanner.cs"));

        runtimeSource.Should().Contain("var textStabilityGate = new UnityTextStabilityGate();");
        runtimeSource.Should().Contain("new UnityTextChangeHookInstaller(pipeline, _resultApplier, _logger, _controlPanel.GetConfig, _fontReplacement, textStabilityGate)");
        runtimeSource.Should().Contain("new UguiTextScanner(pipeline, _resultApplier, _logger, _controlPanel.GetConfig, _fontReplacement, textStabilityGate)");
        runtimeSource.Should().Contain("new TmpTextScanner(pipeline, _resultApplier, _logger, _controlPanel.GetConfig, _fontReplacement, textStabilityGate)");

        AssertUsesStabilityGateBeforePipeline(processorSource);
        AssertUsesStabilityGateBeforePipeline(tmpSource);
        AssertUsesStabilityGateBeforePipeline(uguiSource);
    }

    [Fact]
    public void Runtime_hotkeys_are_wired_to_plugin_actions()
    {
        var pluginSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.Plugin", "PluginRuntime.cs"));
        var hotkeySource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.Plugin", "Hotkeys", "RuntimeHotkeyController.cs"));

        pluginSource.Should().Contain("RuntimeHotkeyController");
        pluginSource.Should().Contain("_hotkeys?.Tick(config);");
        hotkeySource.Should().Contain("SystemBrowserLauncher.TryOpen(_httpServer.Url, _logger);");
        hotkeySource.Should().Contain("_captureCoordinator.Tick(forceFullScan: true);");
        hotkeySource.Should().Contain("_resultApplier.SetTranslatedTextMode(");
        hotkeySource.Should().Contain("_fontReplacement.SetReplacementFontsEnabledForRuntime(");
        var applierSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.Plugin", "Unity", "UnityMainThreadResultApplier.cs"));
        applierSource.Should().Contain("_useTranslatedText = useTranslatedText;");
        applierSource.Should().Contain("var currentText = target.GetText();");
        applierSource.Should().Contain("_writebacks.TryGetDisplayText(target.Id, currentText, _useTranslatedText, out var replacement)");
    }

    [Fact]
    public void Runtime_hotkeys_use_compatible_input_reader_before_legacy_input_manager()
    {
        var hotkeySource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.Plugin", "Hotkeys", "RuntimeHotkey.cs"));
        var inputSourcePath = FindRepositoryFile("src", "HUnityAutoTranslator.Plugin", "Hotkeys", "RuntimeHotkeyInput.cs");

        File.Exists(inputSourcePath).Should().BeTrue("runtime hotkeys must support games that disable UnityEngine.Input");
        var inputSource = File.ReadAllText(inputSourcePath);
        hotkeySource.Should().Contain("RuntimeHotkeyInput");
        hotkeySource.Should().NotContain("Input.GetKeyDown");
        hotkeySource.Should().NotContain("Input.GetKey(");
        inputSource.Should().Contain("UnityEngine.InputSystem.Keyboard");
        inputSource.Should().Contain("wasPressedThisFrame");
        inputSource.Should().Contain("isPressed");
        inputSource.Should().Contain("InvalidOperationException");
        inputSource.Should().Contain("_legacyInputDisabled");
        inputSource.Should().Contain("LogLegacyInputDisabled");
    }

    [Fact]
    public void Plugin_tick_runs_self_check_even_when_hotkey_polling_fails()
    {
        var pluginSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.Plugin", "PluginRuntime.cs"));
        var tickBlock = pluginSource[
            pluginSource.IndexOf("public void Tick()", StringComparison.Ordinal)..
            pluginSource.IndexOf("private void MaybeRefreshHighlighterSnapshot", StringComparison.Ordinal)];

        tickBlock.Should().Contain("_selfCheck?.Tick();");
        tickBlock.Should().Contain("TryTickHotkeys(config);");
        tickBlock.IndexOf("_selfCheck?.Tick();", StringComparison.Ordinal)
            .Should().BeLessThan(tickBlock.IndexOf("TryTickHotkeys(config);", StringComparison.Ordinal));

        var hotkeyGuardBlock = pluginSource[
            pluginSource.IndexOf("private void TryTickHotkeys(RuntimeConfig config)", StringComparison.Ordinal)..
            pluginSource.IndexOf("private void MaybeRefreshHighlighterSnapshot", StringComparison.Ordinal)];
        hotkeyGuardBlock.Should().Contain("try");
        hotkeyGuardBlock.Should().Contain("_hotkeys?.Tick(config);");
        hotkeyGuardBlock.Should().Contain("catch (Exception ex)");
        hotkeyGuardBlock.Should().Contain("_logger.LogWarning");
    }

    [Fact]
    public void Force_scan_hotkey_uses_full_capture_windows()
    {
        var moduleSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.Plugin", "Capture", "ITextCaptureModule.cs"));
        var coordinatorSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.Plugin", "Capture", "TextCaptureCoordinator.cs"));
        var tmpSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.Plugin", "Capture", "TmpTextScanner.cs"));
        var uguiSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.Plugin", "Capture", "UguiTextScanner.cs"));

        moduleSource.Should().Contain("void Tick(bool forceFullScan = false);");
        coordinatorSource.Should().Contain("public void Tick(bool forceFullScan = false, bool skipGlobalObjectScanners = false)");
        coordinatorSource.Should().Contain("module.Tick(forceFullScan)");
        tmpSource.Should().Contain("forceFullScan ? objects.Length : _configProvider().MaxScanTargetsPerTick");
        uguiSource.Should().Contain("forceFullScan ? objects.Length : _configProvider().MaxScanTargetsPerTick");
    }

    [Fact]
    public void Imgui_hook_uses_state_cache_and_frame_budgets_for_hot_path_work()
    {
        var imguiSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.Plugin", "Capture", "ImguiHookInstaller.cs"));

        imguiSource.Should().Contain("ImguiTranslationStateCache");
        imguiSource.Should().Contain("MaxImguiPendingBatchSize = 96");
        imguiSource.Should().Contain("Time.frameCount");
        imguiSource.Should().Contain("Event.current?.type != EventType.Repaint");
        imguiSource.Should().Contain("RefreshDrawContext(config);");
        imguiSource.Should().Contain("_stateCache.ResolveForDraw(");
        imguiSource.Should().Contain("_stateCache.TakePendingBatch(");
        imguiSource.Should().Contain("ProcessPendingImguiText(");
        imguiSource.Should().Contain("publishResult: false");
        imguiSource.Should().Contain("BeginImguiDrawFontScope(__originalMethod, __args, key, context, resolution.DisplayText)");
        imguiSource.Should().Contain("PostfixStringText");
        imguiSource.Should().Contain("__state?.Dispose();");
        imguiSource.Should().NotContain("ApplyPendingImguiFontForDraw");
        imguiSource.Should().NotContain("TryBeginFontScope");
        imguiSource.Should().NotContain("TryResolveImguiFont");

        var prefixBlock = imguiSource[
            imguiSource.IndexOf("private static void PrefixStringText", StringComparison.Ordinal)..
            imguiSource.IndexOf("private ImguiTranslationStateResult ResolveForDraw", StringComparison.Ordinal)];
        prefixBlock.Should().Contain("_configProvider()");
        prefixBlock.Should().NotContain("GetActiveSceneName");

        var pendingBlock = imguiSource[
            imguiSource.IndexOf("private void ProcessPendingImguiText", StringComparison.Ordinal)..
            imguiSource.IndexOf("private void RefreshDrawContext", StringComparison.Ordinal)];
        pendingBlock.Should().NotContain("RequestImguiFont(");
        pendingBlock.Should().NotContain("ApplyToImgui");
        pendingBlock.Should().NotContain("GUI.skin");
    }

    [Fact]
    public void Worker_host_does_not_resume_ignored_imgui_pending_rows()
    {
        var hostSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.Plugin", "TranslationWorkerHost.cs"));
        var resumeBlock = hostSource[
            hostSource.IndexOf("private int ResumePendingTranslations", StringComparison.Ordinal)..
            hostSource.IndexOf("private async Task TryExtractGlossaryAsync", StringComparison.Ordinal)];

        resumeBlock.Should().Contain("IsIgnoredImguiPendingRow(row)");
        resumeBlock.Should().Contain("CompletePendingAsSource(row)");
        resumeBlock.Should().Contain("ImguiTextClassifier.ShouldSkipTranslation(row.SourceText, row.TargetLanguage)");
        resumeBlock.IndexOf("IsIgnoredImguiPendingRow(row)", StringComparison.Ordinal)
            .Should().BeLessThan(resumeBlock.IndexOf("_queue.Enqueue(TranslationJob.Create", StringComparison.Ordinal));
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

    private static void AssertUsesStabilityGateBeforePipeline(string source)
    {
        source.Should().Contain("UnityTextStabilityGate");
        source.Should().Contain("EvaluateStableText(target, text, context, config)");
        source.Should().Contain("_stabilityGate.Evaluate(");

        var gateIndex = source.IndexOf("EvaluateStableText(target, text, context, config)", StringComparison.Ordinal);
        var pipelineIndex = source.IndexOf("_pipeline.Process(capturedText)", StringComparison.Ordinal);
        gateIndex.Should().BeGreaterThanOrEqualTo(0);
        pipelineIndex.Should().BeGreaterThan(gateIndex);
    }
}

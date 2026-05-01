using FluentAssertions;

namespace HUnityAutoTranslator.Core.Tests.Runtime;

public sealed class BoundedRuntimeLoopSourceTests
{
    [Fact]
    public void Unity_text_scanners_use_round_robin_windows_for_bounded_object_scans()
    {
        var tmpSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.Plugin", "Capture", "TmpTextScanner.cs"));
        var uguiSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.Plugin", "Capture", "UguiTextScanner.cs"));
        var finderSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.Plugin", "Capture", "UnityObjectFinder.cs"));

        tmpSource.Should().Contain("RoundRobinCursor");
        tmpSource.Should().Contain("var maxTargets = forceFullScan ? objects.Length : _configProvider().MaxScanTargetsPerTick");
        tmpSource.Should().Contain("_scanCursor.TakeWindow(objects, maxTargets)");
        tmpSource.Should().Contain("UnityObjectFinder.FindObjects(_textType)");
        tmpSource.Should().NotContain("for (var i = 0; i < count; i++)");
        tmpSource.Should().NotContain("FindObjectsOfType(_textType)");

        uguiSource.Should().Contain("RoundRobinCursor");
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

        applierSource.Should().Contain("RoundRobinCursor");
        applierSource.Should().Contain("_reapplyCursor.TakeFullRound(_targets.Values.ToArray())");
        applierSource.Should().Contain("if (applied >= maxCount)");
        applierSource.Should().NotContain("_reapplyCursor.TakeWindow(_targets.Values.ToArray(), maxCount)");
    }

    [Fact]
    public void Plugin_honors_reapply_remembered_translations_setting()
    {
        var pluginSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.Plugin", "PluginRuntime.cs"));

        pluginSource.Should().Contain("if (config.ReapplyRememberedTranslations)");
        pluginSource.Should().Contain("_resultApplier.ReapplyRemembered(int.MaxValue)");
        pluginSource.Should().NotContain("_resultApplier.ReapplyRemembered(config.MaxWritebacksPerFrame)");
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
        applierSource.Should().Contain("_writebacks.TryGetDisplayText(target.Id, target.GetText(), _useTranslatedText, out var replacement)");
    }

    [Fact]
    public void Force_scan_hotkey_uses_full_capture_windows()
    {
        var moduleSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.Plugin", "Capture", "ITextCaptureModule.cs"));
        var coordinatorSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.Plugin", "Capture", "TextCaptureCoordinator.cs"));
        var tmpSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.Plugin", "Capture", "TmpTextScanner.cs"));
        var uguiSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.Plugin", "Capture", "UguiTextScanner.cs"));

        moduleSource.Should().Contain("void Tick(bool forceFullScan = false);");
        coordinatorSource.Should().Contain("public void Tick(bool forceFullScan = false)");
        coordinatorSource.Should().Contain("module.Tick(forceFullScan)");
        tmpSource.Should().Contain("forceFullScan ? objects.Length : _configProvider().MaxScanTargetsPerTick");
        uguiSource.Should().Contain("forceFullScan ? objects.Length : _configProvider().MaxScanTargetsPerTick");
    }

    [Fact]
    public void Imgui_hook_uses_state_cache_and_frame_budgets_for_hot_path_work()
    {
        var imguiSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.Plugin", "Capture", "ImguiHookInstaller.cs"));

        imguiSource.Should().Contain("ImguiTranslationStateCache");
        imguiSource.Should().Contain("MaxImguiNewCapturesPerFrame = 1");
        imguiSource.Should().Contain("MaxImguiCacheRefreshesPerFrame = 1");
        imguiSource.Should().Contain("ImguiNewCaptureIntervalSeconds = 0.25");
        imguiSource.Should().Contain("ImguiCacheRefreshIntervalSeconds = 0.25");
        imguiSource.Should().Contain("Time.frameCount");
        imguiSource.Should().Contain("_stateCache.Resolve(");
        imguiSource.Should().Contain("ProcessImguiText(");
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

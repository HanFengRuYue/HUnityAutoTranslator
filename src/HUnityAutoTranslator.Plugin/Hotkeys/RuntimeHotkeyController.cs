using BepInEx.Logging;
using HUnityAutoTranslator.Core.Configuration;
using HUnityAutoTranslator.Plugin.Capture;
using HUnityAutoTranslator.Plugin.Unity;

namespace HUnityAutoTranslator.Plugin.Hotkeys;

internal sealed class RuntimeHotkeyController
{
    private readonly LocalHttpServer _httpServer;
    private readonly TextCaptureCoordinator _captureCoordinator;
    private readonly UnityMainThreadResultApplier _resultApplier;
    private readonly UnityTextFontReplacementService _fontReplacement;
    private readonly ManualLogSource _logger;
    private bool _useTranslatedText = true;
    private bool _useReplacementFonts = true;

    public RuntimeHotkeyController(
        LocalHttpServer httpServer,
        TextCaptureCoordinator captureCoordinator,
        UnityMainThreadResultApplier resultApplier,
        UnityTextFontReplacementService fontReplacement,
        ManualLogSource logger)
    {
        _httpServer = httpServer;
        _captureCoordinator = captureCoordinator;
        _resultApplier = resultApplier;
        _fontReplacement = fontReplacement;
        _logger = logger;
    }

    public void Tick(RuntimeConfig config)
    {
        if (IsPressed(config.OpenControlPanelHotkey))
        {
            OpenControlPanel();
        }
        else if (IsPressed(config.ToggleTranslationHotkey))
        {
            ToggleTranslatedTextMode();
        }
        else if (IsPressed(config.ForceScanHotkey))
        {
            ForceScanAndUpdate();
        }
        else if (IsPressed(config.ToggleFontHotkey))
        {
            ToggleFontReplacementMode();
        }
    }

    private void OpenControlPanel()
    {
        SystemBrowserLauncher.TryOpen(_httpServer.Url, _logger);
    }

    private void ToggleTranslatedTextMode()
    {
        _useTranslatedText = !_useTranslatedText;
        var changed = _resultApplier.SetTranslatedTextMode(_useTranslatedText, int.MaxValue);
        _logger.LogInfo(_useTranslatedText
            ? $"热键已切换为显示翻译文本（已更新 {changed} 个目标）。"
            : $"热键已切换为显示原文（已更新 {changed} 个目标）。");
    }

    private void ForceScanAndUpdate()
    {
        _captureCoordinator.Tick(forceFullScan: true);
        var changed = _resultApplier.ReapplyRemembered(int.MaxValue);
        _logger.LogInfo($"热键已执行全量文本扫描，并刷新 {changed} 个已记住的目标。");
    }

    private void ToggleFontReplacementMode()
    {
        _useReplacementFonts = !_useReplacementFonts;
        var changed = _fontReplacement.SetReplacementFontsEnabledForRuntime(_useReplacementFonts);
        _logger.LogInfo(_useReplacementFonts
            ? $"热键已切换为替换字体（已更新 {changed} 个目标）。"
            : $"热键已恢复原始字体（已更新 {changed} 个目标）。");
    }

    private static bool IsPressed(string binding)
    {
        return RuntimeHotkey.TryParse(binding, out var hotkey) && hotkey.IsPressed();
    }
}

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
            ? $"Runtime hotkey switched text display to translated text ({changed} target(s) updated)."
            : $"Runtime hotkey switched text display to source text ({changed} target(s) updated).");
    }

    private void ForceScanAndUpdate()
    {
        _captureCoordinator.Tick(forceFullScan: true);
        var changed = _resultApplier.ReapplyRemembered(int.MaxValue);
        _logger.LogInfo($"Runtime hotkey forced a full text scan and refreshed {changed} remembered target(s).");
    }

    private void ToggleFontReplacementMode()
    {
        _useReplacementFonts = !_useReplacementFonts;
        var changed = _fontReplacement.SetReplacementFontsEnabledForRuntime(_useReplacementFonts);
        _logger.LogInfo(_useReplacementFonts
            ? $"Runtime hotkey switched text components to replacement fonts ({changed} target(s) updated)."
            : $"Runtime hotkey restored original text component fonts ({changed} target(s) updated).");
    }

    private static bool IsPressed(string binding)
    {
        return RuntimeHotkey.TryParse(binding, out var hotkey) && hotkey.IsPressed();
    }
}

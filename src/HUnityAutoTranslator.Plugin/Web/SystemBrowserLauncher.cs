using System.Diagnostics;
using BepInEx.Logging;

namespace HUnityAutoTranslator.Plugin;

internal static class SystemBrowserLauncher
{
    public static void TryOpen(string url, ManualLogSource logger)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            logger.LogWarning("Control panel auto-open skipped because the URL is empty.");
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
            logger.LogInfo($"Opened control panel in the system default browser: {url}");
        }
        catch (Exception ex)
        {
            logger.LogWarning($"Failed to open control panel in the system default browser: {ex.Message}");
        }
    }
}

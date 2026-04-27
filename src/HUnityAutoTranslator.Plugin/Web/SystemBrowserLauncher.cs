using System.Diagnostics;
using BepInEx.Logging;

namespace HUnityAutoTranslator.Plugin;

internal static class SystemBrowserLauncher
{
    public static void TryOpen(string url, ManualLogSource logger)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            logger.LogWarning("控制面板地址为空，已跳过自动打开。");
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
            logger.LogInfo($"已在系统默认浏览器打开控制面板：{url}");
        }
        catch (Exception ex)
        {
            logger.LogWarning($"无法自动打开控制面板：{ex.Message}");
        }
    }
}

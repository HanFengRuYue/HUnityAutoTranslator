using BepInEx.Logging;
using HUnityAutoTranslator.Core.Http;
using UnityEngine;

namespace HUnityAutoTranslator.Plugin.Http;

/// <summary>
/// 启动时挑选出站 HTTP 实现。游戏自带 System.Net.Http.dll → HttpClient（已验证路径）；
/// 否则回退到 HttpWebRequest，避免触发插件 bundle 的 net462 旧版 System.Net.Http.dll 里
/// 引用 System.Net.Logging.get_On() 导致的 MissingMethodException（Unity 2019.x 精简 Mono）。
/// </summary>
internal static class HttpTransportFactory
{
    public static IHttpTransport Create(ManualLogSource logger)
    {
#if HUNITY_IL2CPP
        // IL2CPP 插件跑在 BepInEx 的 net6.0 运行时，System.Net.Http 一定可用。
        logger.LogInfo("HTTP 传输：HttpClient（IL2CPP / net6.0 运行时）。");
        return new HttpClientHttpTransport();
#else
        // Mono / BepInEx 5：游戏 Managed/ 里有 System.Net.Http.dll，说明它是 Unity 用对应 Mono 版本
        // 编出来的、和游戏 System.dll ABI 匹配，HttpClient 可安全使用；没有就只会回退到插件 bundle 的
        // net462 旧版（引用了精简 System.dll 里不存在的 System.Net.Logging.get_On），第一次调用即崩。
        try
        {
            var managedHttp = Path.Combine(Application.dataPath, "Managed", "System.Net.Http.dll");
            if (File.Exists(managedHttp))
            {
                logger.LogInfo("HTTP 传输：HttpClient（游戏自带 System.Net.Http.dll）。");
                return new HttpClientHttpTransport();
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning($"探测 System.Net.Http.dll 失败，回退到 HttpWebRequest：{ex.Message}");
        }

        logger.LogInfo("HTTP 传输：HttpWebRequest 回退（游戏未自带 System.Net.Http.dll）。");
        return new WebRequestHttpTransport();
#endif
    }
}

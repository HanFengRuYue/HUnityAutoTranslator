using System;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using BepInEx;

namespace HUnityAutoTranslator.Plugin;

// 读取宿主进程实际加载的 BepInEx 版本，并和插件构建所基于的版本（Directory.Build.props 经
// AssemblyMetadata 写进程序集）比对。三个运行时目标（net462 / netstandard2.1 / net6.0）共用本类型。
internal static class BepInExRuntimeInfo
{
    private const string ExpectedVersionMetadataKey = "ExpectedBepInExVersion";
    private static readonly Regex BleedingEdgeBuildPattern = new("be\\.(\\d+)", RegexOptions.IgnoreCase);

    // 宿主进程实际加载的 BepInEx 版本字符串；读不到时返回 null。
    public static string? GetHostVersionString()
    {
        try
        {
            var bepInExAssembly = typeof(Paths).Assembly;
            var informational = bepInExAssembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                ?.InformationalVersion;
            if (!string.IsNullOrWhiteSpace(informational))
            {
                // BE 构建的 InformationalVersion 形如 6.0.0-be.755+3fab71a，去掉 + 后的 git 元数据。
                return StripBuildMetadata(informational!);
            }

            return bepInExAssembly.GetName().Version?.ToString();
        }
        catch
        {
            return null;
        }
    }

    // 插件构建所基于的 BepInEx 版本（Directory.Build.props 经 AssemblyMetadata 写入）；读不到时返回 null。
    public static string? GetExpectedVersionString()
    {
        try
        {
            var value = typeof(BepInExRuntimeInfo).Assembly
                .GetCustomAttributes<AssemblyMetadataAttribute>()
                .FirstOrDefault(attribute => string.Equals(attribute.Key, ExpectedVersionMetadataKey, StringComparison.Ordinal))
                ?.Value;
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }
        catch
        {
            return null;
        }
    }

    // 判断宿主 BepInEx 是否比插件构建基线更旧。无法可靠比较时返回 false，绝不误报。
    public static bool IsHostOlderThanExpected(string? host, string? expected, out string detail)
    {
        detail = string.Empty;
        if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(expected))
        {
            return false;
        }

        // BepInEx 6 Bleeding Edge：版本号里带 be.<build>，直接比较 build 号。
        if (TryGetBleedingEdgeBuild(host!, out var hostBuild) &&
            TryGetBleedingEdgeBuild(expected!, out var expectedBuild))
        {
            if (hostBuild < expectedBuild)
            {
                detail = string.Format(CultureInfo.InvariantCulture, "宿主 BE 构建 be.{0}，构建基线 be.{1}。", hostBuild, expectedBuild);
                return true;
            }

            return false;
        }

        // BepInEx 5：版本号是 5.4.x.y，按 System.Version 比较。
        if (Version.TryParse(StripBuildMetadata(host!), out var hostVersion) &&
            Version.TryParse(StripBuildMetadata(expected!), out var expectedVersion))
        {
            if (hostVersion < expectedVersion)
            {
                detail = string.Format(CultureInfo.InvariantCulture, "宿主版本 {0}，构建基线 {1}。", hostVersion, expectedVersion);
                return true;
            }

            return false;
        }

        // 两边版本号形态不一致（例如一边是 BE、一边是稳定版）时不做判断，避免误报。
        return false;
    }

    private static bool TryGetBleedingEdgeBuild(string version, out int build)
    {
        var match = BleedingEdgeBuildPattern.Match(version);
        if (match.Success &&
            int.TryParse(match.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out build))
        {
            return true;
        }

        build = 0;
        return false;
    }

    private static string StripBuildMetadata(string version)
    {
        var plusIndex = version.IndexOf('+');
        return plusIndex >= 0 ? version.Substring(0, plusIndex) : version;
    }
}

namespace HUnityAutoTranslator.Toolbox.Core.Installation;

public static class GameInspector
{
    private const string PluginDirectoryName = "HUnityAutoTranslator";
    private const string PluginGuidConfigName = "com.hanfeng.hunityautotranslator.cfg";

    public static GameInspection Inspect(string gameRoot)
    {
        var normalizedRoot = string.IsNullOrWhiteSpace(gameRoot)
            ? string.Empty
            : Path.GetFullPath(gameRoot);
        var exists = normalizedRoot.Length > 0 && Directory.Exists(normalizedRoot);
        if (!exists)
        {
            return new GameInspection(
                normalizedRoot,
                DirectoryExists: false,
                IsValidUnityGame: false,
                GameName: string.Empty,
                UnityBackend.Unknown,
                Architecture: "unknown",
                BepInExInstalled: false,
                BepInExVersion: null,
                ToolboxRuntimeKind.Unknown,
                PluginDirectory: Path.Combine(normalizedRoot, "BepInEx", "plugins", PluginDirectoryName),
                ConfigDirectory: Path.Combine(normalizedRoot, "BepInEx", "config", PluginDirectoryName),
                PluginInstalled: false,
                ProtectedDataPaths: Array.Empty<string>());
        }

        var dataDirectory = FindDataDirectory(normalizedRoot);
        var isUnityGame = dataDirectory != null || Directory.GetFiles(normalizedRoot, "*.exe").Length > 0;
        var backend = DetectBackend(normalizedRoot, dataDirectory);
        var runtime = RecommendRuntime(normalizedRoot, backend);
        var bepInExCore = Path.Combine(normalizedRoot, "BepInEx", "core");
        var bepInExInstalled = Directory.Exists(bepInExCore) || Directory.Exists(Path.Combine(normalizedRoot, "BepInEx"));
        var pluginDirectory = Path.Combine(normalizedRoot, "BepInEx", "plugins", PluginDirectoryName);
        var configDirectory = Path.Combine(normalizedRoot, "BepInEx", "config", PluginDirectoryName);
        var pluginInstalled = Directory.Exists(pluginDirectory) &&
            Directory.EnumerateFiles(pluginDirectory, "HUnityAutoTranslator.Plugin*.dll", SearchOption.TopDirectoryOnly).Any();

        return new GameInspection(
            normalizedRoot,
            DirectoryExists: true,
            IsValidUnityGame: isUnityGame,
            GameName: dataDirectory == null ? Path.GetFileName(normalizedRoot) : Path.GetFileName(dataDirectory)[..^"_Data".Length],
            Backend: backend,
            Architecture: DetectArchitecture(normalizedRoot, backend),
            BepInExInstalled: bepInExInstalled,
            BepInExVersion: DetectBepInExVersion(normalizedRoot, runtime),
            RecommendedRuntime: runtime,
            PluginInstalled: pluginInstalled,
            PluginDirectory: pluginDirectory,
            ConfigDirectory: configDirectory,
            ProtectedDataPaths: FindProtectedDataPaths(normalizedRoot, configDirectory));
    }

    private static string? FindDataDirectory(string gameRoot)
    {
        return Directory.EnumerateDirectories(gameRoot, "*_Data", SearchOption.TopDirectoryOnly)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }

    private static UnityBackend DetectBackend(string gameRoot, string? dataDirectory)
    {
        if (File.Exists(Path.Combine(gameRoot, "GameAssembly.dll")))
        {
            return UnityBackend.IL2CPP;
        }

        if (dataDirectory != null &&
            (File.Exists(Path.Combine(dataDirectory, "Managed", "Assembly-CSharp.dll")) ||
             Directory.Exists(Path.Combine(dataDirectory, "Managed"))))
        {
            return UnityBackend.Mono;
        }

        return UnityBackend.Unknown;
    }

    private static ToolboxRuntimeKind RecommendRuntime(string gameRoot, UnityBackend backend)
    {
        if (backend == UnityBackend.IL2CPP)
        {
            return ToolboxRuntimeKind.IL2CPP;
        }

        var bepInEx6Core = Path.Combine(gameRoot, "BepInEx", "core", "BepInEx.Core.dll");
        if (File.Exists(bepInEx6Core))
        {
            // 已有 BepInEx 6 Mono 安装,沿用以避免覆盖。
            return ToolboxRuntimeKind.Mono;
        }

        var bepInEx5Config = Path.Combine(gameRoot, "BepInEx", "config", PluginGuidConfigName);
        var bepInEx5Core = Path.Combine(gameRoot, "BepInEx", "core", "BepInEx.dll");
        if (File.Exists(bepInEx5Config) || File.Exists(bepInEx5Core))
        {
            // 已有 BepInEx 5 Mono 安装,继续推荐 BepInEx 5 以保护用户既有环境。
            return ToolboxRuntimeKind.BepInEx5Mono;
        }

        // 新装 Mono 游戏一律默认 BepInEx 6 Mono(最新主线)。
        return backend == UnityBackend.Mono ? ToolboxRuntimeKind.Mono : ToolboxRuntimeKind.Unknown;
    }

    private static string DetectArchitecture(string gameRoot, UnityBackend backend)
    {
        if (backend == UnityBackend.IL2CPP)
        {
            return "x64";
        }

        var win64Player = Path.Combine(gameRoot, "UnityPlayer.dll");
        return File.Exists(win64Player) ? "x64" : "unknown";
    }

    private static string? DetectBepInExVersion(string gameRoot, ToolboxRuntimeKind runtime)
    {
        var core = Path.Combine(gameRoot, "BepInEx", "core");
        if (!Directory.Exists(core))
        {
            return null;
        }

        if (runtime == ToolboxRuntimeKind.BepInEx5Mono)
        {
            return "BepInEx 5";
        }

        if (File.Exists(Path.Combine(core, "BepInEx.Core.dll")) ||
            File.Exists(Path.Combine(core, "BepInEx.Unity.IL2CPP.dll")) ||
            File.Exists(Path.Combine(core, "BepInEx.Unity.Mono.dll")))
        {
            return "BepInEx 6";
        }

        return "BepInEx";
    }

    private static IReadOnlyList<string> FindProtectedDataPaths(string gameRoot, string configDirectory)
    {
        var protectedPaths = new List<string>();
        var mainConfig = Path.Combine(gameRoot, "BepInEx", "config", PluginGuidConfigName);
        if (File.Exists(mainConfig))
        {
            protectedPaths.Add(mainConfig);
        }

        if (Directory.Exists(configDirectory))
        {
            protectedPaths.AddRange(Directory.EnumerateFileSystemEntries(configDirectory, "*", SearchOption.AllDirectories));
        }

        return protectedPaths
            .Select(Path.GetFullPath)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}

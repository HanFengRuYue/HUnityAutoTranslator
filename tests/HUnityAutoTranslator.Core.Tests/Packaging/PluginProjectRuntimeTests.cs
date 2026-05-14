using FluentAssertions;

namespace HUnityAutoTranslator.Core.Tests.Packaging;

public sealed class PluginProjectRuntimeTests
{
    [Fact]
    public void Plugin_project_targets_latest_bepinex6_mono_and_il2cpp_runtimes()
    {
        var root = FindRepositoryRoot();
        var project = File.ReadAllText(Path.Combine(
            root,
            "src",
            "HUnityAutoTranslator.Plugin",
            "HUnityAutoTranslator.Plugin.csproj"));
        var directoryBuildProps = File.ReadAllText(Path.Combine(root, "Directory.Build.props"));

        project.Should().Contain("<TargetFrameworks>netstandard2.1;net6.0</TargetFrameworks>");
        project.Should().Contain("HUNITY_IL2CPP");
        project.Should().Contain("BepInEx.Unity.Mono");
        project.Should().Contain("BepInEx.Unity.IL2CPP");
        // BepInEx 6 BE 版本号集中在 Directory.Build.props，且必须 pin 到具体 BE 构建（不是浮动的 6.0.0-be.*）。
        project.Should().Contain("Version=\"$(BepInEx6BeVersion)\"");
        directoryBuildProps.Should().MatchRegex(@"<BepInEx6BeVersion>6\.0\.0-be\.\d+</BepInEx6BeVersion>");
        project.Should().Contain("<AssemblyName Condition=\"'$(TargetFramework)' == 'net6.0'\">HUnityAutoTranslator.Plugin.IL2CPP</AssemblyName>");
    }

    [Fact]
    public void BepInEx5_plugin_project_reuses_the_mono_runtime_sources_with_bepinex5_references()
    {
        var root = FindRepositoryRoot();
        var project = File.ReadAllText(Path.Combine(
            root,
            "src",
            "HUnityAutoTranslator.Plugin.BepInEx5",
            "HUnityAutoTranslator.Plugin.BepInEx5.csproj"));
        var directoryBuildProps = File.ReadAllText(Path.Combine(root, "Directory.Build.props"));

        // Unity 2019.4 LTS 自带的 Mono 没有完整的 netstandard.dll 转发（缺 System.ValueTuple 等），
        // BepInEx 5 插件必须直接打 .NET Framework 4.6.2 才能让 Unity 把 MonoBehaviour 实例化出来。
        project.Should().Contain("<TargetFramework>net462</TargetFramework>");
        project.Should().Contain("<AssemblyName>HUnityAutoTranslator.Plugin.BepInEx5</AssemblyName>");
        project.Should().Contain("HUNITY_BEPINEX5");
        project.Should().Contain("BepInEx.Core");
        // BepInEx 5 版本号集中在 Directory.Build.props，且只作为兼容回退（5.4.x）。
        project.Should().Contain("Version=\"$(BepInEx5Version)\"");
        directoryBuildProps.Should().MatchRegex(@"<BepInEx5Version>5\.4\.\d+(\.\d+)?</BepInEx5Version>");
        project.Should().Contain("..\\HUnityAutoTranslator.Plugin\\**\\*.cs");
        project.Should().Contain("..\\HUnityAutoTranslator.Plugin\\bin\\**");
        project.Should().Contain("..\\HUnityAutoTranslator.Plugin\\obj\\**");
        project.Should().Contain("..\\HUnityAutoTranslator.Core\\Polyfills\\IsExternalInit.cs");
        project.Should().Contain("..\\HUnityAutoTranslator.Core\\HUnityAutoTranslator.Core.csproj");
        project.Should().Contain("PolySharp");
        project.Should().NotContain("BepInEx.Unity.Mono");
        project.Should().NotContain("BepInEx.Unity.IL2CPP");
    }

    [Fact]
    public void Mono_and_il2cpp_entrypoints_share_the_same_runtime_graph()
    {
        var root = FindRepositoryRoot();
        var plugin = File.ReadAllText(Path.Combine(root, "src", "HUnityAutoTranslator.Plugin", "Plugin.cs"));
        var runtime = File.ReadAllText(Path.Combine(root, "src", "HUnityAutoTranslator.Plugin", "PluginRuntime.cs"));
        var il2CppLoop = File.ReadAllText(Path.Combine(root, "src", "HUnityAutoTranslator.Plugin", "Il2CppPluginLoop.cs"));

        plugin.Should().Contain("new PluginRuntime(Logger");
        plugin.Should().Contain("new PluginRuntime(Log");
        plugin.Should().Contain("#elif !HUNITY_BEPINEX5");
        plugin.Should().Contain(": BaseUnityPlugin");
        plugin.Should().Contain(": BasePlugin");
        runtime.Should().Contain("new UguiTextScanner");
        runtime.Should().Contain("new TmpTextScanner");
        runtime.Should().Contain("new ImguiHookInstaller");
        runtime.Should().Contain("new LocalHttpServer");
        runtime.Should().Contain("new RuntimeHotkeyController");
        il2CppLoop.Should().Contain("IL2CPPChainloader.AddUnityComponent(typeof(Il2CppPluginLoop))");
        il2CppLoop.Should().Contain("_runtime?.Tick()");
        il2CppLoop.Should().Contain("_runtime?.LateTick()");
        il2CppLoop.Should().Contain("_runtime?.RenderGui()");
    }

    [Fact]
    public void Plugin_runtime_configures_windows_console_for_utf8_before_chinese_logs()
    {
        var root = FindRepositoryRoot();
        var runtime = File.ReadAllText(Path.Combine(root, "src", "HUnityAutoTranslator.Plugin", "PluginRuntime.cs"));
        var encoding = File.ReadAllText(Path.Combine(root, "src", "HUnityAutoTranslator.Plugin", "WindowsConsoleEncoding.cs"));
        var assemblyInfo = File.ReadAllText(Path.Combine(root, "src", "HUnityAutoTranslator.Core", "Properties", "AssemblyInfo.cs"));

        runtime.Should().Contain("WindowsConsoleEncoding.ConfigureUtf8();");
        runtime.IndexOf("WindowsConsoleEncoding.ConfigureUtf8();", StringComparison.Ordinal)
            .Should()
            .BeLessThan(runtime.IndexOf("_logger.LogInfo($\"{MyPluginInfo.PLUGIN_NAME} 已加载。", StringComparison.Ordinal));
        encoding.Should().Contain("SetConsoleOutputCP(Utf8CodePage)");
        encoding.Should().Contain("SetConsoleCP(Utf8CodePage)");
        encoding.Should().Contain("Console.OutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)");
        encoding.Should().Contain("catch");
        assemblyInfo.Should().Contain("InternalsVisibleTo(\"HUnityAutoTranslator.Plugin.BepInEx5\")");
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "HUnityAutoTranslator.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate HUnityAutoTranslator.sln from test output directory.");
    }
}

using FluentAssertions;

namespace HUnityAutoTranslator.Core.Tests.Packaging;

public sealed class PluginProjectRuntimeTests
{
    [Fact]
    public void Plugin_project_targets_mono_and_latest_bepinex_il2cpp_be()
    {
        var project = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "HUnityAutoTranslator.Plugin",
            "HUnityAutoTranslator.Plugin.csproj"));

        project.Should().Contain("<TargetFrameworks>netstandard2.1;net6.0</TargetFrameworks>");
        project.Should().Contain("HUNITY_IL2CPP");
        project.Should().Contain("BepInEx.Unity.Mono");
        project.Should().Contain("BepInEx.Unity.IL2CPP");
        project.Should().Contain("Version=\"6.0.0-be.*\"");
        project.Should().Contain("<AssemblyName Condition=\"'$(TargetFramework)' == 'net6.0'\">HUnityAutoTranslator.Plugin.IL2CPP</AssemblyName>");
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

        runtime.Should().Contain("WindowsConsoleEncoding.ConfigureUtf8();");
        runtime.IndexOf("WindowsConsoleEncoding.ConfigureUtf8();", StringComparison.Ordinal)
            .Should()
            .BeLessThan(runtime.IndexOf("_logger.LogInfo($\"{MyPluginInfo.PLUGIN_NAME} 已加载。", StringComparison.Ordinal));
        encoding.Should().Contain("SetConsoleOutputCP(Utf8CodePage)");
        encoding.Should().Contain("SetConsoleCP(Utf8CodePage)");
        encoding.Should().Contain("Console.OutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)");
        encoding.Should().Contain("catch");
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

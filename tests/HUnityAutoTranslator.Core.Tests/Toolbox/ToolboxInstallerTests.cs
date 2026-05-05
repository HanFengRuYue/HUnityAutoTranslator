using FluentAssertions;
using HUnityAutoTranslator.Toolbox.Core.Installation;

namespace HUnityAutoTranslator.Core.Tests.Toolbox;

public sealed class ToolboxInstallerTests
{
    [Fact]
    public void GameInspector_detects_il2cpp_game_and_existing_plugin_data()
    {
        var gameRoot = CreateUnityGame(il2Cpp: true);
        Directory.CreateDirectory(Path.Combine(gameRoot, "BepInEx", "plugins", "HUnityAutoTranslator"));
        File.WriteAllText(Path.Combine(gameRoot, "BepInEx", "plugins", "HUnityAutoTranslator", "HUnityAutoTranslator.Plugin.IL2CPP.dll"), "plugin");
        Directory.CreateDirectory(Path.Combine(gameRoot, "BepInEx", "config", "HUnityAutoTranslator"));
        File.WriteAllText(Path.Combine(gameRoot, "BepInEx", "config", "HUnityAutoTranslator", "translation-cache.sqlite"), "cache");

        var inspection = GameInspector.Inspect(gameRoot);

        inspection.IsValidUnityGame.Should().BeTrue();
        inspection.Backend.Should().Be(UnityBackend.IL2CPP);
        inspection.RecommendedRuntime.Should().Be(ToolboxRuntimeKind.IL2CPP);
        inspection.PluginInstalled.Should().BeTrue();
        inspection.ProtectedDataPaths.Should().Contain(path => path.EndsWith("translation-cache.sqlite", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void InstallPlanner_keeps_user_runtime_data_out_of_overwrite_plan()
    {
        var gameRoot = CreateUnityGame(il2Cpp: true);
        Directory.CreateDirectory(Path.Combine(gameRoot, "BepInEx", "config", "HUnityAutoTranslator", "providers"));
        File.WriteAllText(Path.Combine(gameRoot, "BepInEx", "config", "HUnityAutoTranslator", "providers", "main.hutprovider"), "secret");
        var inspection = GameInspector.Inspect(gameRoot);

        var plan = InstallPlanner.CreatePlan(inspection, new InstallPlanOptions(
            PackageVersion: "1.2.3",
            Mode: InstallMode.Full,
            IncludeLlamaCppBackend: true,
            LlamaCppBackend: LlamaCppBackendKind.Cuda13));

        plan.PluginPackageName.Should().Be("HUnityAutoTranslator-1.2.3-il2cpp.zip");
        plan.LlamaCppPackageName.Should().Be("HUnityAutoTranslator-1.2.3-llamacpp-cuda13.zip");
        plan.ProtectedPaths.Should().Contain(path => path.Replace('\\', '/').EndsWith("BepInEx/config/HUnityAutoTranslator/providers/main.hutprovider", StringComparison.OrdinalIgnoreCase));
        plan.Operations.Should().NotContain(operation => operation.DestinationPath.Replace('\\', '/').Contains("BepInEx/config/HUnityAutoTranslator/providers/main.hutprovider", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void SafeZipExtractor_rejects_entries_that_escape_destination()
    {
        var target = Path.Combine(Path.GetTempPath(), "HUnityToolboxZipTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(target);

        var action = () => SafeZipExtractor.GetSafeDestinationPath(target, "../escape.dll");

        action.Should().Throw<InvalidOperationException>()
            .WithMessage("*escape*");
    }

    private static string CreateUnityGame(bool il2Cpp)
    {
        var root = Path.Combine(Path.GetTempPath(), "HUnityToolboxInstallerTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(Path.Combine(root, "Sample_Data", "Managed"));
        File.WriteAllText(Path.Combine(root, "Sample.exe"), string.Empty);
        if (il2Cpp)
        {
            File.WriteAllText(Path.Combine(root, "GameAssembly.dll"), string.Empty);
        }
        else
        {
            File.WriteAllText(Path.Combine(root, "Sample_Data", "Managed", "Assembly-CSharp.dll"), string.Empty);
        }

        return root;
    }
}

using FluentAssertions;
using HUnityAutoTranslator.Toolbox.Core.Installation;

namespace HUnityAutoTranslator.Core.Tests.Toolbox;

[Collection("EmbeddedAssetCatalog")]
public sealed class ToolboxInstallerTests : IDisposable
{
    public ToolboxInstallerTests()
    {
        EmbeddedAssetCatalog.OverrideForTests(EmbeddedCatalogFixture.SyntheticEntries());
    }

    public void Dispose()
    {
        EmbeddedAssetCatalog.OverrideForTests(null);
    }

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
    public void GameInspector_recommends_bepinex6_mono_for_fresh_mono_game()
    {
        var gameRoot = CreateUnityGame(il2Cpp: false);

        var inspection = GameInspector.Inspect(gameRoot);

        inspection.Backend.Should().Be(UnityBackend.Mono);
        inspection.RecommendedRuntime.Should().Be(ToolboxRuntimeKind.Mono);
    }

    [Fact]
    public void GameInspector_preserves_bepinex5_recommendation_when_bepinex5_already_installed()
    {
        var gameRoot = CreateUnityGame(il2Cpp: false);
        Directory.CreateDirectory(Path.Combine(gameRoot, "BepInEx", "core"));
        File.WriteAllText(Path.Combine(gameRoot, "BepInEx", "core", "BepInEx.dll"), "marker");

        var inspection = GameInspector.Inspect(gameRoot);

        inspection.RecommendedRuntime.Should().Be(ToolboxRuntimeKind.BepInEx5Mono);
    }

    [Fact]
    public void GameInspector_preserves_bepinex6_recommendation_when_bepinex6_already_installed()
    {
        var gameRoot = CreateUnityGame(il2Cpp: false);
        Directory.CreateDirectory(Path.Combine(gameRoot, "BepInEx", "core"));
        File.WriteAllText(Path.Combine(gameRoot, "BepInEx", "core", "BepInEx.Core.dll"), "marker");

        var inspection = GameInspector.Inspect(gameRoot);

        inspection.RecommendedRuntime.Should().Be(ToolboxRuntimeKind.Mono);
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
        // The protected provider file must never be the destination of a write operation
        // (CreateDirectory / ExtractPackage / BackupExisting / VerifyFile).
        // PreserveUserData operations DO list it on purpose -- they're informational only for the UI,
        // and the executor never writes anything for them. Exclude them from this assertion.
        plan.Operations
            .Where(op => op.Kind != InstallOperationKind.PreserveUserData)
            .Should()
            .NotContain(operation => operation.DestinationPath.Replace('\\', '/').Contains("BepInEx/config/HUnityAutoTranslator/providers/main.hutprovider", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void InstallPlanner_inserts_framework_extract_step_when_bepinex_missing()
    {
        var gameRoot = CreateUnityGame(il2Cpp: true);
        var inspection = GameInspector.Inspect(gameRoot);
        inspection.BepInExInstalled.Should().BeFalse();

        var plan = InstallPlanner.CreatePlan(inspection, new InstallPlanOptions(
            PackageVersion: "1.2.3",
            Mode: InstallMode.Full,
            IncludeLlamaCppBackend: false,
            LlamaCppBackend: LlamaCppBackendKind.None));

        plan.Operations.Should().Contain(op =>
            op.Kind == InstallOperationKind.ExtractPackage &&
            op.SourceKind == InstallOperationSourceKind.EmbeddedAsset &&
            op.SourcePath == "bepinex6il2cpp-framework");
    }

    [Fact]
    public void InstallPlanner_threads_embedded_asset_key_into_source_path_for_plugin()
    {
        var gameRoot = CreateUnityGame(il2Cpp: true);
        var inspection = GameInspector.Inspect(gameRoot);

        var plan = InstallPlanner.CreatePlan(inspection, new InstallPlanOptions(
            PackageVersion: "1.2.3",
            Mode: InstallMode.Full,
            IncludeLlamaCppBackend: false,
            LlamaCppBackend: LlamaCppBackendKind.None));

        plan.Operations.Should().Contain(op =>
            op.SourceKind == InstallOperationSourceKind.EmbeddedAsset &&
            op.SourcePath == "plugin-il2cpp");
    }

    [Fact]
    public void RuntimeOverride_takes_precedence_over_inspection_recommendation()
    {
        var gameRoot = CreateUnityGame(il2Cpp: true);
        var inspection = GameInspector.Inspect(gameRoot);

        var plan = InstallPlanner.CreatePlan(inspection, new InstallPlanOptions(
            PackageVersion: "1.2.3",
            Mode: InstallMode.Full,
            IncludeLlamaCppBackend: false,
            LlamaCppBackend: LlamaCppBackendKind.None,
            RuntimeOverride: ToolboxRuntimeKind.BepInEx5Mono));

        plan.PluginPackageName.Should().Be("HUnityAutoTranslator-1.2.3-bepinex5.zip");
        plan.Operations.Should().Contain(op => op.SourcePath == "plugin-bepinex5");
    }

    [Fact]
    public void BepInExHandling_Skip_omits_framework_step_even_when_not_installed()
    {
        var gameRoot = CreateUnityGame(il2Cpp: true);
        var inspection = GameInspector.Inspect(gameRoot);
        inspection.BepInExInstalled.Should().BeFalse();

        var plan = InstallPlanner.CreatePlan(inspection, new InstallPlanOptions(
            PackageVersion: "1.2.3",
            Mode: InstallMode.Full,
            IncludeLlamaCppBackend: false,
            LlamaCppBackend: LlamaCppBackendKind.None,
            BepInExHandling: BepInExHandling.Skip));

        plan.Operations.Should().NotContain(op =>
            op.SourceKind == InstallOperationSourceKind.EmbeddedAsset &&
            op.SourcePath.Contains("framework", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void BackupPolicy_Skip_omits_backup_step()
    {
        var gameRoot = CreateUnityGame(il2Cpp: true);
        Directory.CreateDirectory(Path.Combine(gameRoot, "BepInEx", "plugins", "HUnityAutoTranslator"));
        File.WriteAllText(Path.Combine(gameRoot, "BepInEx", "plugins", "HUnityAutoTranslator", "HUnityAutoTranslator.Plugin.IL2CPP.dll"), "plugin");
        var inspection = GameInspector.Inspect(gameRoot);

        var plan = InstallPlanner.CreatePlan(inspection, new InstallPlanOptions(
            PackageVersion: "1.2.3",
            Mode: InstallMode.PluginOnly,
            IncludeLlamaCppBackend: false,
            LlamaCppBackend: LlamaCppBackendKind.None,
            BackupPolicy: BackupPolicy.Skip));

        plan.Operations.Should().NotContain(op => op.Kind == InstallOperationKind.BackupExisting);
    }

    [Fact]
    public void CustomPluginZipPath_marks_operation_as_local_file_source()
    {
        var gameRoot = CreateUnityGame(il2Cpp: true);
        var inspection = GameInspector.Inspect(gameRoot);
        var customZip = Path.Combine(Path.GetTempPath(), "fake-plugin.zip");
        File.WriteAllText(customZip, "stub");

        try
        {
            var plan = InstallPlanner.CreatePlan(inspection, new InstallPlanOptions(
                PackageVersion: "1.2.3",
                Mode: InstallMode.Full,
                IncludeLlamaCppBackend: false,
                LlamaCppBackend: LlamaCppBackendKind.None,
                CustomPluginZipPath: customZip));

            plan.Operations.Should().Contain(op =>
                op.SourceKind == InstallOperationSourceKind.LocalFile &&
                op.SourcePath.EndsWith("fake-plugin.zip", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            File.Delete(customZip);
        }
    }

    [Fact]
    public void DryRun_propagates_to_plan_flag()
    {
        var gameRoot = CreateUnityGame(il2Cpp: true);
        var inspection = GameInspector.Inspect(gameRoot);

        var plan = InstallPlanner.CreatePlan(inspection, new InstallPlanOptions(
            PackageVersion: "1.2.3",
            Mode: InstallMode.Full,
            IncludeLlamaCppBackend: false,
            LlamaCppBackend: LlamaCppBackendKind.None,
            DryRun: true));

        plan.IsDryRun.Should().BeTrue();
    }

    [Fact]
    public void InstallPlanner_inserts_prepare_unity_libs_step_for_il2cpp_when_version_override_present()
    {
        var gameRoot = CreateUnityGame(il2Cpp: true);
        var inspection = GameInspector.Inspect(gameRoot);

        var plan = InstallPlanner.CreatePlan(inspection, new InstallPlanOptions(
            PackageVersion: "1.2.3",
            Mode: InstallMode.Full,
            IncludeLlamaCppBackend: false,
            LlamaCppBackend: LlamaCppBackendKind.None,
            UnityVersionOverride: "2022.3.21f1"));

        plan.Operations.Should().ContainSingle(op =>
            op.Kind == InstallOperationKind.PrepareUnityBaseLibraries &&
            op.SourcePath == "2022.3.21f1" &&
            op.DestinationPath.Replace('\\', '/').EndsWith("BepInEx/unity-libs/2022.3.21f1.zip", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void InstallPlanner_omits_unity_libs_step_for_mono_runtime()
    {
        var gameRoot = CreateUnityGame(il2Cpp: false);
        var inspection = GameInspector.Inspect(gameRoot);

        var plan = InstallPlanner.CreatePlan(inspection, new InstallPlanOptions(
            PackageVersion: "1.2.3",
            Mode: InstallMode.Full,
            IncludeLlamaCppBackend: false,
            LlamaCppBackend: LlamaCppBackendKind.None,
            UnityVersionOverride: "2022.3.21f1"));

        plan.Operations.Should().NotContain(op => op.Kind == InstallOperationKind.PrepareUnityBaseLibraries);
    }

    [Fact]
    public void InstallPlanner_marks_custom_unity_zip_as_local_file_source()
    {
        var gameRoot = CreateUnityGame(il2Cpp: true);
        var customZip = Path.Combine(Path.GetTempPath(), "unity-libs-fake.zip");
        File.WriteAllText(customZip, "stub");
        try
        {
            var inspection = GameInspector.Inspect(gameRoot);
            var plan = InstallPlanner.CreatePlan(inspection, new InstallPlanOptions(
                PackageVersion: "1.2.3",
                Mode: InstallMode.Full,
                IncludeLlamaCppBackend: false,
                LlamaCppBackend: LlamaCppBackendKind.None,
                UnityVersionOverride: "2022.3.21f1",
                CustomUnityLibraryZipPath: customZip));

            plan.Operations.Should().ContainSingle(op =>
                op.Kind == InstallOperationKind.PrepareUnityBaseLibraries &&
                op.SourceKind == InstallOperationSourceKind.LocalFile &&
                op.SourcePath.EndsWith("unity-libs-fake.zip", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            File.Delete(customZip);
        }
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

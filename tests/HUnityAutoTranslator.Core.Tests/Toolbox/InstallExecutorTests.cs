using System.Text;
using FluentAssertions;
using HUnityAutoTranslator.Toolbox.Core.Installation;

namespace HUnityAutoTranslator.Core.Tests.Toolbox;

[Collection("EmbeddedAssetCatalog")]
public sealed class InstallExecutorTests : IDisposable
{
    private readonly Dictionary<string, byte[]> _zipBlobs = new(StringComparer.Ordinal);

    public InstallExecutorTests()
    {
        EmbeddedAssetCatalog.OverrideForTests(
            EmbeddedCatalogFixture.SyntheticEntries(),
            streamProvider: asset =>
            {
                if (_zipBlobs.TryGetValue(asset.Key, out var blob))
                {
                    return new MemoryStream(blob, writable: false);
                }
                return null;
            });
    }

    public void Dispose()
    {
        EmbeddedAssetCatalog.OverrideForTests(null);
    }

    [Fact]
    public async Task Execute_writes_expected_dll_and_reports_completed()
    {
        var gameRoot = CreateUnityGame(il2Cpp: true);
        StagePluginZip("plugin-il2cpp", "HUnityAutoTranslator.Plugin.IL2CPP.dll");
        StageFrameworkZip("bepinex6il2cpp-framework");

        var inspection = GameInspector.Inspect(gameRoot);
        var plan = InstallPlanner.CreatePlan(inspection, new InstallPlanOptions(
            PackageVersion: "1.2.3",
            Mode: InstallMode.Full,
            IncludeLlamaCppBackend: false,
            LlamaCppBackend: LlamaCppBackendKind.None));

        var executor = new InstallExecutor();
        var stages = new List<InstallStage>();
        var progress = new Progress<InstallProgress>(p => stages.Add(p.Stage));
        var result = await executor.ExecuteAsync(plan, progress, CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        result.FinalStage.Should().Be(InstallStage.Completed);
        stages.Should().Contain(InstallStage.Completed);
        File.Exists(Path.Combine(gameRoot, "BepInEx", "plugins", "HUnityAutoTranslator", "HUnityAutoTranslator.Plugin.IL2CPP.dll"))
            .Should().BeTrue();
    }

    [Fact]
    public async Task Execute_skips_protected_paths_inside_zip()
    {
        var gameRoot = CreateUnityGame(il2Cpp: true);
        Directory.CreateDirectory(Path.Combine(gameRoot, "BepInEx", "config", "HUnityAutoTranslator"));
        File.WriteAllText(
            Path.Combine(gameRoot, "BepInEx", "config", "HUnityAutoTranslator", "translation-cache.sqlite"),
            "ORIGINAL_USER_DATA");

        // Zip includes both the plugin DLL AND a malicious overwrite of the protected sqlite.
        _zipBlobs["plugin-il2cpp"] = EmbeddedCatalogFixture.CreateZipContaining(
            ("BepInEx/plugins/HUnityAutoTranslator/HUnityAutoTranslator.Plugin.IL2CPP.dll", Encoding.UTF8.GetBytes("dll")),
            ("BepInEx/config/HUnityAutoTranslator/translation-cache.sqlite", Encoding.UTF8.GetBytes("EVIL_OVERWRITE")));
        StageFrameworkZip("bepinex6il2cpp-framework");

        var inspection = GameInspector.Inspect(gameRoot);
        var plan = InstallPlanner.CreatePlan(inspection, new InstallPlanOptions(
            PackageVersion: "1.2.3",
            Mode: InstallMode.Full,
            IncludeLlamaCppBackend: false,
            LlamaCppBackend: LlamaCppBackendKind.None));

        var executor = new InstallExecutor();
        var result = await executor.ExecuteAsync(plan, null, CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        var preservedContent = File.ReadAllText(Path.Combine(gameRoot, "BepInEx", "config", "HUnityAutoTranslator", "translation-cache.sqlite"));
        preservedContent.Should().Be("ORIGINAL_USER_DATA", "the protected user data must not be overwritten by the zip");
    }

    [Fact]
    public async Task Execute_creates_backup_with_manifest_before_extraction()
    {
        var gameRoot = CreateUnityGame(il2Cpp: true);
        var pluginDir = Path.Combine(gameRoot, "BepInEx", "plugins", "HUnityAutoTranslator");
        Directory.CreateDirectory(pluginDir);
        // The inspector recognises a plugin via the HUnityAutoTranslator.Plugin*.dll glob.
        File.WriteAllText(Path.Combine(pluginDir, "HUnityAutoTranslator.Plugin.IL2CPP.dll"), "old-payload");
        StagePluginZip("plugin-il2cpp", "HUnityAutoTranslator.Plugin.IL2CPP.dll");
        StageFrameworkZip("bepinex6il2cpp-framework");

        var inspection = GameInspector.Inspect(gameRoot);
        inspection.PluginInstalled.Should().BeTrue();

        var plan = InstallPlanner.CreatePlan(inspection, new InstallPlanOptions(
            PackageVersion: "1.2.3",
            Mode: InstallMode.Full,
            IncludeLlamaCppBackend: false,
            LlamaCppBackend: LlamaCppBackendKind.None));

        var executor = new InstallExecutor();
        var result = await executor.ExecuteAsync(plan, null, CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        Directory.Exists(result.BackupDirectory).Should().BeTrue();
        File.Exists(Path.Combine(result.BackupDirectory, "backup-manifest.json")).Should().BeTrue();
        File.Exists(Path.Combine(result.BackupDirectory, "HUnityAutoTranslator.Plugin.IL2CPP.dll")).Should().BeTrue();
    }

    [Fact]
    public async Task Execute_returns_cancelled_when_cancellation_requested_before_first_op()
    {
        var gameRoot = CreateUnityGame(il2Cpp: true);
        StagePluginZip("plugin-il2cpp", "HUnityAutoTranslator.Plugin.IL2CPP.dll");
        StageFrameworkZip("bepinex6il2cpp-framework");

        var inspection = GameInspector.Inspect(gameRoot);
        var plan = InstallPlanner.CreatePlan(inspection, new InstallPlanOptions(
            PackageVersion: "1.2.3",
            Mode: InstallMode.Full,
            IncludeLlamaCppBackend: false,
            LlamaCppBackend: LlamaCppBackendKind.None));

        var cts = new CancellationTokenSource();
        cts.Cancel();

        var executor = new InstallExecutor();
        Func<Task> act = () => executor.ExecuteAsync(plan, null, cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task DryRun_does_not_write_any_files_but_still_completes_with_progress()
    {
        var gameRoot = CreateUnityGame(il2Cpp: true);
        StagePluginZip("plugin-il2cpp", "HUnityAutoTranslator.Plugin.IL2CPP.dll");
        StageFrameworkZip("bepinex6il2cpp-framework");

        var inspection = GameInspector.Inspect(gameRoot);
        var plan = InstallPlanner.CreatePlan(inspection, new InstallPlanOptions(
            PackageVersion: "1.2.3",
            Mode: InstallMode.Full,
            IncludeLlamaCppBackend: false,
            LlamaCppBackend: LlamaCppBackendKind.None,
            DryRun: true));

        plan.IsDryRun.Should().BeTrue();

        var executor = new InstallExecutor();
        var ticks = new List<InstallProgress>();
        var progress = new Progress<InstallProgress>(p => ticks.Add(p));
        var result = await executor.ExecuteAsync(plan, progress, CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        File.Exists(Path.Combine(gameRoot, "BepInEx", "plugins", "HUnityAutoTranslator", "HUnityAutoTranslator.Plugin.IL2CPP.dll"))
            .Should().BeFalse("dry-run must not write the plugin DLL");
        ticks.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Executor_stages_unity_base_libs_from_provided_provider()
    {
        var gameRoot = CreateUnityGame(il2Cpp: true);
        StagePluginZip("plugin-il2cpp", "HUnityAutoTranslator.Plugin.IL2CPP.dll");
        StageFrameworkZip("bepinex6il2cpp-framework");

        // Pre-populate a fake "cache" so the provider never reaches for the network.
        var cacheDir = Path.Combine(Path.GetTempPath(), "HUnityToolboxExecutorTests-cache", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(cacheDir);
        File.WriteAllText(Path.Combine(cacheDir, "2022.3.21f1.zip"), "CACHED");

        var provider = new UnityBaseLibraryProvider(
            cacheDirectory: cacheDir,
            httpDownloaderOverride: (_, _) => throw new InvalidOperationException("must not reach network in test"));

        var inspection = GameInspector.Inspect(gameRoot);
        var plan = InstallPlanner.CreatePlan(inspection, new InstallPlanOptions(
            PackageVersion: "1.2.3",
            Mode: InstallMode.Full,
            IncludeLlamaCppBackend: false,
            LlamaCppBackend: LlamaCppBackendKind.None,
            UnityVersionOverride: "2022.3.21f1"));

        var executor = new InstallExecutor(provider);
        var result = await executor.ExecuteAsync(plan, null, CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        var stagedZip = Path.Combine(gameRoot, "BepInEx", "unity-libs", "2022.3.21f1.zip");
        File.Exists(stagedZip).Should().BeTrue("Unity base libraries must be staged at the BepInEx-expected location");
        File.ReadAllText(stagedZip).Should().Be("CACHED");
    }

    [Fact]
    public async Task Rollback_restores_plugin_files_but_does_not_touch_config_directory()
    {
        var gameRoot = CreateUnityGame(il2Cpp: true);
        // pre-existing plugin we will back up + later restore
        var pluginDir = Path.Combine(gameRoot, "BepInEx", "plugins", "HUnityAutoTranslator");
        Directory.CreateDirectory(pluginDir);
        File.WriteAllText(Path.Combine(pluginDir, "HUnityAutoTranslator.Plugin.IL2CPP.dll"), "old-payload");
        File.WriteAllText(Path.Combine(pluginDir, "extra.txt"), "extra-content");
        // user config should never be touched by rollback
        var configDir = Path.Combine(gameRoot, "BepInEx", "config", "HUnityAutoTranslator");
        Directory.CreateDirectory(configDir);
        File.WriteAllText(Path.Combine(configDir, "user.bin"), "user-cfg");

        StagePluginZip("plugin-il2cpp", "HUnityAutoTranslator.Plugin.IL2CPP.dll");
        StageFrameworkZip("bepinex6il2cpp-framework");

        var inspection = GameInspector.Inspect(gameRoot);
        var plan = InstallPlanner.CreatePlan(inspection, new InstallPlanOptions(
            PackageVersion: "1.2.3",
            Mode: InstallMode.Full,
            IncludeLlamaCppBackend: false,
            LlamaCppBackend: LlamaCppBackendKind.None));

        var executor = new InstallExecutor();
        var installResult = await executor.ExecuteAsync(plan, null, CancellationToken.None);
        installResult.Succeeded.Should().BeTrue();

        var rollback = await executor.RollbackAsync(installResult.BackupDirectory, gameRoot, null, CancellationToken.None);

        rollback.Succeeded.Should().BeTrue();
        File.ReadAllText(Path.Combine(pluginDir, "HUnityAutoTranslator.Plugin.IL2CPP.dll")).Should().Be("old-payload");
        File.Exists(Path.Combine(pluginDir, "extra.txt")).Should().BeTrue();
        File.ReadAllText(Path.Combine(configDir, "user.bin")).Should().Be("user-cfg", "rollback must not touch the config directory");
    }

    private static string CreateUnityGame(bool il2Cpp)
    {
        var root = Path.Combine(Path.GetTempPath(), "HUnityToolboxExecutorTests", Guid.NewGuid().ToString("N"));
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

    private void StagePluginZip(string assetKey, string dllName)
    {
        _zipBlobs[assetKey] = EmbeddedCatalogFixture.CreateZipContaining(
            ($"BepInEx/plugins/HUnityAutoTranslator/{dllName}", Encoding.UTF8.GetBytes("dll")));
    }

    private void StageFrameworkZip(string assetKey)
    {
        _zipBlobs[assetKey] = EmbeddedCatalogFixture.CreateZipContaining(
            ("BepInEx/core/BepInEx.Core.dll", Encoding.UTF8.GetBytes("framework")));
    }
}

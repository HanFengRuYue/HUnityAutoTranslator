using FluentAssertions;
using HUnityAutoTranslator.Toolbox.Core.Installation;

namespace HUnityAutoTranslator.Core.Tests.Packaging;

[Collection("EmbeddedAssetCatalog")]
public sealed class EmbeddedAssetCatalogTests : IDisposable
{
    public EmbeddedAssetCatalogTests()
    {
        EmbeddedAssetCatalog.OverrideForTests(null);
    }

    public void Dispose()
    {
        EmbeddedAssetCatalog.OverrideForTests(null);
    }

    [Fact]
    public void Manifest_file_exists_in_source_tree()
    {
        var root = FindRepositoryRoot();
        var manifest = Path.Combine(root, "src", "HUnityAutoTranslator.Toolbox.Core", "Installation", "EmbeddedAssetManifest.cs");
        File.Exists(manifest).Should().BeTrue("EmbeddedAssetManifest.cs is auto-generated but must always exist in source (committed empty stub when no bundle).");
    }

    [Fact]
    public void Staged_embedded_assets_match_manifest_byte_for_byte_when_present()
    {
        var root = FindRepositoryRoot();
        var stageRoot = Path.Combine(root, "src", "HUnityAutoTranslator.Toolbox", "EmbeddedAssets");
        if (!Directory.Exists(stageRoot))
        {
            return; // dev build without staged bundle - skip
        }

        var stagedFiles = Directory.GetFiles(stageRoot, "*.zip", SearchOption.TopDirectoryOnly)
            .ToDictionary(p => Path.GetFileName(p)!, p => p, StringComparer.OrdinalIgnoreCase);

        var manifestEntries = EmbeddedAssetCatalog.All;
        manifestEntries.Should().NotBeEmpty("staged bundle present implies manifest must list its contents");

        foreach (var entry in manifestEntries)
        {
            var expectedFile = entry.ResourceName.Replace("HUnityAutoTranslator.Toolbox.EmbeddedAssets.", string.Empty);
            stagedFiles.Should().ContainKey(expectedFile, $"manifest references {expectedFile} which must exist in EmbeddedAssets/");
            var actualSha = ComputeSha256(stagedFiles[expectedFile]);
            actualSha.Should().Be(entry.Sha256, $"manifest SHA256 for {entry.Key} must match the staged file");
            new FileInfo(stagedFiles[expectedFile]).Length.Should().Be(entry.SizeBytes);
        }
    }

    [Fact]
    public void Override_allows_test_substitution()
    {
        var fakeEntry = new EmbeddedAsset(
            "test-key", EmbeddedAssetKind.PluginPackage, ToolboxRuntimeKind.IL2CPP, LlamaCppBackendKind.None,
            "9.9.9", "0000", 1, "fake-resource");
        EmbeddedAssetCatalog.OverrideForTests(new[] { fakeEntry });

        EmbeddedAssetCatalog.FindByKey("test-key").Should().BeSameAs(fakeEntry);
        EmbeddedAssetCatalog.FindPluginPackage(ToolboxRuntimeKind.IL2CPP).Should().BeSameAs(fakeEntry);
    }

    [Fact]
    public void Find_returns_correct_bepinex_framework_for_each_runtime()
    {
        EmbeddedAssetCatalog.OverrideForTests(new[]
        {
            new EmbeddedAsset("bepinex5-framework", EmbeddedAssetKind.BepInExFramework, ToolboxRuntimeKind.BepInEx5Mono, LlamaCppBackendKind.None, "5.4.23.5", "x", 1, "r1"),
            new EmbeddedAsset("bepinex6mono-framework", EmbeddedAssetKind.BepInExFramework, ToolboxRuntimeKind.Mono, LlamaCppBackendKind.None, "6.0.0-pre.2", "x", 1, "r2"),
            new EmbeddedAsset("bepinex6il2cpp-framework", EmbeddedAssetKind.BepInExFramework, ToolboxRuntimeKind.IL2CPP, LlamaCppBackendKind.None, "6.0.0-pre.2", "x", 1, "r3"),
        });

        EmbeddedAssetCatalog.FindBepInExFramework(ToolboxRuntimeKind.BepInEx5Mono)!.Key.Should().Be("bepinex5-framework");
        EmbeddedAssetCatalog.FindBepInExFramework(ToolboxRuntimeKind.Mono)!.Key.Should().Be("bepinex6mono-framework");
        EmbeddedAssetCatalog.FindBepInExFramework(ToolboxRuntimeKind.IL2CPP)!.Key.Should().Be("bepinex6il2cpp-framework");
    }

    [Fact]
    public void OpenStream_throws_clear_message_when_resources_missing()
    {
        var entry = new EmbeddedAsset(
            "missing-key", EmbeddedAssetKind.PluginPackage, ToolboxRuntimeKind.IL2CPP, LlamaCppBackendKind.None,
            "0.0.0", "x", 0, "definitely.not.a.resource");
        EmbeddedAssetCatalog.OverrideForTests(new[] { entry });

        Action act = () => EmbeddedAssetCatalog.OpenStream(entry);
        act.Should().Throw<InvalidOperationException>().WithMessage("*嵌入资源缺失*");
    }

    private static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        using var hasher = System.Security.Cryptography.SHA256.Create();
        var hash = hasher.ComputeHash(stream);
        var sb = new System.Text.StringBuilder(hash.Length * 2);
        foreach (var b in hash) sb.Append(b.ToString("x2"));
        return sb.ToString();
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

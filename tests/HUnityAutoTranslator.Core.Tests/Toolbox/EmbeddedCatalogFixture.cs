using System.IO.Compression;
using HUnityAutoTranslator.Toolbox.Core.Installation;

namespace HUnityAutoTranslator.Core.Tests.Toolbox;

/// <summary>
/// Helpers that build a fake <see cref="EmbeddedAssetCatalog"/> contents for tests that exercise
/// <see cref="InstallPlanner"/> and <see cref="InstallExecutor"/>. The synthetic entries are pointed
/// at in-memory zip blobs hosted by <see cref="InMemoryResourceAssembly"/>.
/// </summary>
internal static class EmbeddedCatalogFixture
{
    public static IReadOnlyList<EmbeddedAsset> SyntheticEntries()
    {
        return new[]
        {
            new EmbeddedAsset("plugin-bepinex5", EmbeddedAssetKind.PluginPackage, ToolboxRuntimeKind.BepInEx5Mono, LlamaCppBackendKind.None,
                "0.1.1", "deadbeef", 32, "fake.plugin-bepinex5"),
            new EmbeddedAsset("plugin-mono", EmbeddedAssetKind.PluginPackage, ToolboxRuntimeKind.Mono, LlamaCppBackendKind.None,
                "0.1.1", "deadbeef", 32, "fake.plugin-mono"),
            new EmbeddedAsset("plugin-il2cpp", EmbeddedAssetKind.PluginPackage, ToolboxRuntimeKind.IL2CPP, LlamaCppBackendKind.None,
                "0.1.1", "deadbeef", 32, "fake.plugin-il2cpp"),
            new EmbeddedAsset("bepinex5-framework", EmbeddedAssetKind.BepInExFramework, ToolboxRuntimeKind.BepInEx5Mono, LlamaCppBackendKind.None,
                "5.4.23.5", "deadbeef", 32, "fake.bepinex5"),
            new EmbeddedAsset("bepinex6mono-framework", EmbeddedAssetKind.BepInExFramework, ToolboxRuntimeKind.Mono, LlamaCppBackendKind.None,
                "6.0.0-pre.2", "deadbeef", 32, "fake.bepinex6mono"),
            new EmbeddedAsset("bepinex6il2cpp-framework", EmbeddedAssetKind.BepInExFramework, ToolboxRuntimeKind.IL2CPP, LlamaCppBackendKind.None,
                "6.0.0-pre.2", "deadbeef", 32, "fake.bepinex6il2cpp"),
            new EmbeddedAsset("llamacpp-cuda13", EmbeddedAssetKind.LlamaCppBackend, ToolboxRuntimeKind.Unknown, LlamaCppBackendKind.Cuda13,
                "0.1.1", "deadbeef", 32, "fake.llamacpp-cuda13"),
            new EmbeddedAsset("llamacpp-vulkan", EmbeddedAssetKind.LlamaCppBackend, ToolboxRuntimeKind.Unknown, LlamaCppBackendKind.Vulkan,
                "0.1.1", "deadbeef", 32, "fake.llamacpp-vulkan")
        };
    }

    public static byte[] CreateZipContaining(params (string Path, byte[] Content)[] entries)
    {
        using var ms = new MemoryStream();
        using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (path, content) in entries)
            {
                var entry = archive.CreateEntry(path, CompressionLevel.Fastest);
                using var stream = entry.Open();
                stream.Write(content, 0, content.Length);
            }
        }
        return ms.ToArray();
    }
}

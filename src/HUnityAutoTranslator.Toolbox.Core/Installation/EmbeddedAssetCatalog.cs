using System.Reflection;

namespace HUnityAutoTranslator.Toolbox.Core.Installation;

public enum EmbeddedAssetKind
{
    BepInExFramework = 0,
    PluginPackage = 1,
    LlamaCppBackend = 2
}

public sealed record EmbeddedAsset(
    string Key,
    EmbeddedAssetKind Kind,
    ToolboxRuntimeKind Runtime,
    LlamaCppBackendKind Backend,
    string Version,
    string Sha256,
    long SizeBytes,
    string ResourceName);

public static class EmbeddedAssetCatalog
{
    private static Assembly _assembly = typeof(EmbeddedAssetCatalog).Assembly;
    private static IReadOnlyList<EmbeddedAsset>? _override;
    private static Func<EmbeddedAsset, Stream?>? _streamProviderOverride;

    /// <summary>
    /// Bind the catalog to the assembly that actually contains the embedded zip resources.
    /// The Toolbox WinExe calls this from MainWindow.OnLoaded so Toolbox.Core (which has no resources of its own)
    /// can still read from the host's manifest resources.
    /// </summary>
    public static void UseAssembly(Assembly assembly)
    {
        _assembly = assembly ?? throw new ArgumentNullException(nameof(assembly));
    }

    /// <summary>
    /// For tests: substitute an in-memory entries set, an assembly, and/or a stream provider.
    /// Pass null to restore the generated manifest.
    /// </summary>
    public static void OverrideForTests(
        IReadOnlyList<EmbeddedAsset>? entries,
        Assembly? assembly = null,
        Func<EmbeddedAsset, Stream?>? streamProvider = null)
    {
        _override = entries;
        _streamProviderOverride = streamProvider;
        if (assembly is not null)
        {
            _assembly = assembly;
        }
    }

    public static IReadOnlyList<EmbeddedAsset> All => _override ?? EmbeddedAssetManifest.Entries;

    public static EmbeddedAsset? FindBepInExFramework(ToolboxRuntimeKind runtime)
    {
        return All.FirstOrDefault(asset => asset.Kind == EmbeddedAssetKind.BepInExFramework && asset.Runtime == runtime);
    }

    public static EmbeddedAsset? FindPluginPackage(ToolboxRuntimeKind runtime)
    {
        return All.FirstOrDefault(asset => asset.Kind == EmbeddedAssetKind.PluginPackage && asset.Runtime == runtime);
    }

    public static EmbeddedAsset? FindLlamaCppBackend(LlamaCppBackendKind backend)
    {
        return All.FirstOrDefault(asset => asset.Kind == EmbeddedAssetKind.LlamaCppBackend && asset.Backend == backend);
    }

    public static EmbeddedAsset? FindByKey(string key)
    {
        return All.FirstOrDefault(asset => string.Equals(asset.Key, key, StringComparison.Ordinal));
    }

    public static Stream OpenStream(EmbeddedAsset asset)
    {
        if (asset is null)
        {
            throw new ArgumentNullException(nameof(asset));
        }

        if (_streamProviderOverride is not null)
        {
            var providerStream = _streamProviderOverride(asset);
            if (providerStream is null)
            {
                throw new InvalidOperationException($"测试桩 stream provider 未提供 {asset.Key} 资源。");
            }
            return providerStream;
        }

        var stream = _assembly.GetManifestResourceStream(asset.ResourceName);
        if (stream is null)
        {
            throw new InvalidOperationException(
                $"嵌入资源缺失：{asset.ResourceName}。请运行 build/package-toolbox.ps1 生成内置资源后再启动工具箱。");
        }

        return stream;
    }
}

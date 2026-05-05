namespace HUnityAutoTranslator.Toolbox.Core.Installation;

public static class InstallPlanner
{
    public static InstallPlan CreatePlan(GameInspection inspection, InstallPlanOptions options)
    {
        if (!inspection.DirectoryExists || !inspection.IsValidUnityGame)
        {
            throw new InvalidOperationException("The selected directory is not a recognized Unity game root.");
        }

        var packageName = BuildPluginPackageName(options.PackageVersion, inspection.RecommendedRuntime);
        var llamaPackageName = options.IncludeLlamaCppBackend
            ? BuildLlamaCppPackageName(options.PackageVersion, options.LlamaCppBackend)
            : null;
        var backupDirectory = Path.Combine(
            inspection.GameRoot,
            "BepInEx",
            "config",
            "HUnityAutoTranslator",
            "toolbox-backups",
            DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss"));

        var operations = new List<InstallOperation>();
        operations.Add(new InstallOperation(
            InstallOperationKind.CreateDirectory,
            SourcePath: string.Empty,
            DestinationPath: inspection.PluginDirectory,
            Description: "确保插件目录存在"));

        if (inspection.PluginInstalled)
        {
            operations.Add(new InstallOperation(
                InstallOperationKind.BackupExisting,
                SourcePath: inspection.PluginDirectory,
                DestinationPath: backupDirectory,
                Description: "备份旧插件文件"));
        }

        if (options.Mode != InstallMode.LlamaCppBackendOnly)
        {
            operations.Add(new InstallOperation(
                InstallOperationKind.ExtractPackage,
                SourcePath: packageName,
                DestinationPath: inspection.GameRoot,
                Description: "解压 HUnityAutoTranslator 插件包"));
        }

        if (llamaPackageName != null)
        {
            operations.Add(new InstallOperation(
                InstallOperationKind.ExtractPackage,
                SourcePath: llamaPackageName,
                DestinationPath: inspection.GameRoot,
                Description: "解压 llama.cpp 后端包"));
        }

        operations.Add(new InstallOperation(
            InstallOperationKind.VerifyFile,
            SourcePath: string.Empty,
            DestinationPath: Path.Combine(inspection.PluginDirectory, BuildPluginAssemblyName(inspection.RecommendedRuntime)),
            Description: "验证插件 DLL"));

        return new InstallPlan(
            inspection,
            options.Mode,
            packageName,
            llamaPackageName,
            inspection.ProtectedDataPaths,
            operations,
            backupDirectory);
    }

    private static string BuildPluginPackageName(string version, ToolboxRuntimeKind runtime)
    {
        var normalizedVersion = string.IsNullOrWhiteSpace(version) ? "0.1.0" : version.Trim();
        return runtime switch
        {
            ToolboxRuntimeKind.BepInEx5Mono => $"HUnityAutoTranslator-{normalizedVersion}-bepinex5.zip",
            ToolboxRuntimeKind.IL2CPP => $"HUnityAutoTranslator-{normalizedVersion}-il2cpp.zip",
            ToolboxRuntimeKind.Mono => $"HUnityAutoTranslator-{normalizedVersion}.zip",
            _ => $"HUnityAutoTranslator-{normalizedVersion}.zip"
        };
    }

    private static string BuildPluginAssemblyName(ToolboxRuntimeKind runtime)
    {
        return runtime switch
        {
            ToolboxRuntimeKind.BepInEx5Mono => "HUnityAutoTranslator.Plugin.BepInEx5.dll",
            ToolboxRuntimeKind.IL2CPP => "HUnityAutoTranslator.Plugin.IL2CPP.dll",
            _ => "HUnityAutoTranslator.Plugin.dll"
        };
    }

    private static string BuildLlamaCppPackageName(string version, LlamaCppBackendKind backend)
    {
        var normalizedVersion = string.IsNullOrWhiteSpace(version) ? "0.1.0" : version.Trim();
        return backend switch
        {
            LlamaCppBackendKind.Cuda13 => $"HUnityAutoTranslator-{normalizedVersion}-llamacpp-cuda13.zip",
            LlamaCppBackendKind.Vulkan => $"HUnityAutoTranslator-{normalizedVersion}-llamacpp-vulkan.zip",
            _ => throw new InvalidOperationException("请选择 llama.cpp 后端类型。")
        };
    }
}

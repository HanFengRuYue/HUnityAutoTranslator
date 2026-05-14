namespace HUnityAutoTranslator.Toolbox.Core.Installation;

public static class InstallPlanner
{
    public static InstallPlan CreatePlan(GameInspection inspection, InstallPlanOptions options)
    {
        if (!inspection.DirectoryExists || !inspection.IsValidUnityGame)
        {
            throw new InvalidOperationException("当前选中的目录不是一个有效的 Unity 游戏根目录。");
        }

        var runtime = options.RuntimeOverride ?? inspection.RecommendedRuntime;
        if (runtime == ToolboxRuntimeKind.Unknown)
        {
            throw new InvalidOperationException("无法判断游戏运行时(Mono / IL2CPP),请在「自定义安装 → 高级」里手动指定。");
        }

        var pluginDirectory = string.IsNullOrWhiteSpace(options.CustomPluginDirectory)
            ? inspection.PluginDirectory
            : Path.GetFullPath(options.CustomPluginDirectory);

        var backupDirectory = string.IsNullOrWhiteSpace(options.CustomBackupDirectory)
            ? Path.Combine(
                inspection.GameRoot,
                "BepInEx",
                "config",
                "HUnityAutoTranslator",
                "toolbox-backups",
                DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss"))
            : Path.GetFullPath(options.CustomBackupDirectory);

        var pluginAsset = EmbeddedAssetCatalog.FindPluginPackage(runtime);
        var bepInExAsset = EmbeddedAssetCatalog.FindBepInExFramework(runtime);
        var llamaCppAsset = options.IncludeLlamaCppBackend
            ? EmbeddedAssetCatalog.FindLlamaCppBackend(options.LlamaCppBackend)
            : null;

        var pluginPackageName = BuildPluginPackageName(options.PackageVersion, runtime);
        var llamaCppPackageName = options.IncludeLlamaCppBackend
            ? BuildLlamaCppPackageName(options.PackageVersion, options.LlamaCppBackend)
            : null;

        var operations = new List<InstallOperation>();

        // 1. 确保插件目录存在
        operations.Add(new InstallOperation(
            InstallOperationKind.CreateDirectory,
            SourcePath: string.Empty,
            DestinationPath: pluginDirectory,
            Description: "确保插件目录存在",
            SourceKind: InstallOperationSourceKind.None));

        // 2. 备份(根据策略)
        var shouldBackup = options.BackupPolicy switch
        {
            BackupPolicy.Skip => false,
            BackupPolicy.Always => true,
            _ => inspection.PluginInstalled
        };
        if (shouldBackup)
        {
            operations.Add(new InstallOperation(
                InstallOperationKind.BackupExisting,
                SourcePath: pluginDirectory,
                DestinationPath: backupDirectory,
                Description: "备份旧插件文件",
                SourceKind: InstallOperationSourceKind.Directory));
        }

        // 3. BepInEx 框架(根据策略;LlamaCppBackendOnly 模式跳过框架与插件)
        if (options.Mode != InstallMode.LlamaCppBackendOnly)
        {
            var shouldInstallFramework = options.BepInExHandling switch
            {
                BepInExHandling.Skip => false,
                BepInExHandling.Always => true,
                _ => !inspection.BepInExInstalled || options.Mode == InstallMode.Full
            };

            if (shouldInstallFramework)
            {
                if (!string.IsNullOrWhiteSpace(options.CustomBepInExZipPath))
                {
                    var path = Path.GetFullPath(options.CustomBepInExZipPath);
                    operations.Add(new InstallOperation(
                        InstallOperationKind.ExtractPackage,
                        SourcePath: path,
                        DestinationPath: inspection.GameRoot,
                        Description: $"安装 BepInEx 框架(自定义 zip):{Path.GetFileName(path)}",
                        SourceKind: InstallOperationSourceKind.LocalFile));
                }
                else if (bepInExAsset is not null)
                {
                    operations.Add(new InstallOperation(
                        InstallOperationKind.ExtractPackage,
                        SourcePath: bepInExAsset.Key,
                        DestinationPath: inspection.GameRoot,
                        Description: $"安装 BepInEx 框架({bepInExAsset.Version})",
                        SourceKind: InstallOperationSourceKind.EmbeddedAsset));
                }
                else
                {
                    throw new InvalidOperationException(
                        "工具箱未内置该运行时所需的 BepInEx 框架包。请运行 build/package-toolbox.ps1 重新打包,或在「自定义安装 → 开发者」里指定本地 BepInEx zip。");
                }
            }
        }

        // 4. 插件包(LlamaCppBackendOnly 模式跳过)
        if (options.Mode != InstallMode.LlamaCppBackendOnly)
        {
            if (!string.IsNullOrWhiteSpace(options.CustomPluginZipPath))
            {
                var path = Path.GetFullPath(options.CustomPluginZipPath);
                operations.Add(new InstallOperation(
                    InstallOperationKind.ExtractPackage,
                    SourcePath: path,
                    DestinationPath: inspection.GameRoot,
                    Description: $"解压 HUnityAutoTranslator 插件包(自定义 zip):{Path.GetFileName(path)}",
                    SourceKind: InstallOperationSourceKind.LocalFile));
            }
            else if (pluginAsset is not null)
            {
                operations.Add(new InstallOperation(
                    InstallOperationKind.ExtractPackage,
                    SourcePath: pluginAsset.Key,
                    DestinationPath: inspection.GameRoot,
                    Description: $"解压 HUnityAutoTranslator 插件包({pluginAsset.Version})",
                    SourceKind: InstallOperationSourceKind.EmbeddedAsset));
            }
            else
            {
                throw new InvalidOperationException(
                    "工具箱未内置该运行时对应的插件包。请运行 build/package-toolbox.ps1 重新打包,或在「自定义安装 → 开发者」里指定本地插件 zip。");
            }
        }

        // 4.5 IL2CPP 专属:为 BepInEx 6 IL2CPP 预下载/复用 Unity 基础库,避免游戏首次启动联网。
        // 编码规范: SourcePath = Unity 版本号; DestinationPath = 目标 zip 路径; SourceKind 标识具体来路。
        if (runtime == ToolboxRuntimeKind.IL2CPP && options.Mode != InstallMode.LlamaCppBackendOnly)
        {
            var unityVersion = options.UnityVersionOverride;
            if (string.IsNullOrWhiteSpace(unityVersion))
            {
                unityVersion = UnityVersionDetector.TryDetect(inspection.GameRoot)?.Version;
            }

            if (!string.IsNullOrWhiteSpace(unityVersion))
            {
                var unityZipDestination = Path.Combine(inspection.GameRoot, "BepInEx", "unity-libs", unityVersion + ".zip");
                var sourceKind = !string.IsNullOrWhiteSpace(options.CustomUnityLibraryZipPath)
                    ? InstallOperationSourceKind.LocalFile
                    : InstallOperationSourceKind.None;
                var sourcePath = !string.IsNullOrWhiteSpace(options.CustomUnityLibraryZipPath)
                    ? Path.GetFullPath(options.CustomUnityLibraryZipPath!)
                    : unityVersion;
                var desc = !string.IsNullOrWhiteSpace(options.CustomUnityLibraryZipPath)
                    ? $"准备 Unity {unityVersion} 基础库(自定义 zip)"
                    : $"准备 Unity {unityVersion} 基础库(全局缓存或一次性下载)";
                operations.Add(new InstallOperation(
                    InstallOperationKind.PrepareUnityBaseLibraries,
                    SourcePath: sourcePath,
                    DestinationPath: unityZipDestination,
                    Description: desc,
                    SourceKind: sourceKind));
            }
            // 检测不出 Unity 版本时不阻塞安装,但用户首次启动游戏会触发 BepInEx 自己的下载流程。
        }

        // 5. llama.cpp 后端
        if (llamaCppAsset is not null || !string.IsNullOrWhiteSpace(options.CustomLlamaCppZipPath))
        {
            if (!string.IsNullOrWhiteSpace(options.CustomLlamaCppZipPath))
            {
                var path = Path.GetFullPath(options.CustomLlamaCppZipPath);
                operations.Add(new InstallOperation(
                    InstallOperationKind.ExtractPackage,
                    SourcePath: path,
                    DestinationPath: inspection.GameRoot,
                    Description: $"解压 llama.cpp 后端包(自定义 zip):{Path.GetFileName(path)}",
                    SourceKind: InstallOperationSourceKind.LocalFile));
            }
            else
            {
                operations.Add(new InstallOperation(
                    InstallOperationKind.ExtractPackage,
                    SourcePath: llamaCppAsset!.Key,
                    DestinationPath: inspection.GameRoot,
                    Description: $"解压 llama.cpp 后端包({llamaCppAsset.Version},{llamaCppAsset.Backend})",
                    SourceKind: InstallOperationSourceKind.EmbeddedAsset));
            }
        }
        else if (options.IncludeLlamaCppBackend && options.LlamaCppBackend != LlamaCppBackendKind.None)
        {
            throw new InvalidOperationException(
                $"工具箱未内置 llama.cpp {options.LlamaCppBackend} 后端包。请重新打包,或在「自定义安装 → 开发者」里指定本地 zip。");
        }

        // 6. 占位的"受保护用户数据"操作(每个 protected path 一条,UI 用于展示)
        foreach (var path in inspection.ProtectedDataPaths)
        {
            operations.Add(new InstallOperation(
                InstallOperationKind.PreserveUserData,
                SourcePath: string.Empty,
                DestinationPath: path,
                Description: "保留用户数据",
                SourceKind: InstallOperationSourceKind.None));
        }

        // 7. 安装后校验(除非用户显式跳过或仅装 llama.cpp)
        if (!options.SkipPostInstallVerification && options.Mode != InstallMode.LlamaCppBackendOnly)
        {
            operations.Add(new InstallOperation(
                InstallOperationKind.VerifyFile,
                SourcePath: string.Empty,
                DestinationPath: Path.Combine(pluginDirectory, BuildPluginAssemblyName(runtime)),
                Description: "验证插件 DLL",
                SourceKind: InstallOperationSourceKind.None));
        }

        return new InstallPlan(
            inspection,
            options.Mode,
            pluginPackageName,
            llamaCppPackageName,
            inspection.ProtectedDataPaths,
            operations,
            backupDirectory,
            IsDryRun: options.DryRun);
    }

    private static string BuildPluginPackageName(string version, ToolboxRuntimeKind runtime)
    {
        var normalizedVersion = string.IsNullOrWhiteSpace(version) ? "0.1.1" : version.Trim();
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
        var normalizedVersion = string.IsNullOrWhiteSpace(version) ? "0.1.1" : version.Trim();
        return backend switch
        {
            LlamaCppBackendKind.Cuda13 => $"HUnityAutoTranslator-{normalizedVersion}-llamacpp-cuda13.zip",
            LlamaCppBackendKind.Vulkan => $"HUnityAutoTranslator-{normalizedVersion}-llamacpp-vulkan.zip",
            _ => throw new InvalidOperationException("请选择 llama.cpp 后端类型。")
        };
    }
}

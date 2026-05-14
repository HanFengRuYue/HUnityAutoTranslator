using System.Text.Json;
using System.Text.Json.Serialization;

namespace HUnityAutoTranslator.Toolbox.Core.Installation;

public sealed class InstallExecutor
{
    private static readonly JsonSerializerOptions BackupManifestJsonOptions = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private readonly UnityBaseLibraryProvider _unityLibraryProvider;

    public InstallExecutor(UnityBaseLibraryProvider? unityLibraryProvider = null)
    {
        _unityLibraryProvider = unityLibraryProvider ?? new UnityBaseLibraryProvider();
    }

    public Task<InstallResult> ExecuteAsync(
        InstallPlan plan,
        IProgress<InstallProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (plan is null)
        {
            throw new ArgumentNullException(nameof(plan));
        }

        return Task.Run(() => Execute(plan, progress, cancellationToken, _unityLibraryProvider), cancellationToken);
    }

    public Task<RollbackResult> RollbackAsync(
        string backupDirectory,
        string gameRoot,
        IProgress<InstallProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(backupDirectory))
        {
            throw new ArgumentException("缺少备份目录。", nameof(backupDirectory));
        }
        if (string.IsNullOrWhiteSpace(gameRoot))
        {
            throw new ArgumentException("缺少游戏根目录。", nameof(gameRoot));
        }

        return Task.Run(() => Rollback(backupDirectory, gameRoot, progress, cancellationToken), cancellationToken);
    }

    private static InstallResult Execute(
        InstallPlan plan,
        IProgress<InstallProgress>? progress,
        CancellationToken cancellationToken,
        UnityBaseLibraryProvider unityLibraryProvider)
    {
        var operationCount = plan.Operations.Count;
        var protectedSet = BuildProtectedPathSet(plan.ProtectedPaths);
        var written = new List<string>();
        var skipped = new List<string>();
        var errors = new List<string>();
        string? backupDirectoryActual = null;

        Report(progress, 0, operationCount, InstallStage.Preparing, "开始安装", percent: 0d);

        for (var i = 0; i < operationCount; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var op = plan.Operations[i];
            var stage = ResolveStageFromOperation(op);
            var startPercent = operationCount == 0 ? 0d : (double)i / operationCount;
            Report(progress, i + 1, operationCount, stage, op.Description, startPercent, op.DestinationPath);

            try
            {
                if (plan.IsDryRun)
                {
                    // 干跑模式: 仅推送进度,不做任何 IO。
                }
                else
                {
                    switch (op.Kind)
                    {
                        case InstallOperationKind.CreateDirectory:
                            Directory.CreateDirectory(op.DestinationPath);
                            break;

                        case InstallOperationKind.BackupExisting:
                            backupDirectoryActual = PerformBackup(op, plan.Inspection.GameRoot, cancellationToken);
                            break;

                        case InstallOperationKind.ExtractPackage:
                            ExtractPackage(op, protectedSet, written, skipped, cancellationToken);
                            break;

                        case InstallOperationKind.PreserveUserData:
                            // 仅作为 UI 上的可见步骤,SafeZipExtractor 会通过 protectedSet 拦截真正的覆盖。
                            break;

                        case InstallOperationKind.VerifyFile:
                            if (!File.Exists(op.DestinationPath))
                            {
                                throw new FileNotFoundException($"安装后校验失败,未找到文件：{op.DestinationPath}", op.DestinationPath);
                            }
                            break;

                        case InstallOperationKind.PrepareUnityBaseLibraries:
                            PrepareUnityBaseLibraries(op, plan, unityLibraryProvider, written, progress, i + 1, operationCount, cancellationToken);
                            break;

                        default:
                            throw new InvalidOperationException($"不支持的安装操作：{op.Kind}");
                    }
                }

                var endPercent = operationCount == 0 ? 1d : (double)(i + 1) / operationCount;
                Report(progress, i + 1, operationCount, stage, op.Description + " - 完成", endPercent, op.DestinationPath);
            }
            catch (OperationCanceledException)
            {
                Report(progress, i + 1, operationCount, InstallStage.Cancelled, "安装已取消", percent: (double)i / Math.Max(1, operationCount), op.DestinationPath);
                return new InstallResult(
                    Succeeded: false,
                    Message: "安装已取消。",
                    BackupDirectory: backupDirectoryActual ?? plan.BackupDirectory,
                    WrittenPaths: written,
                    Errors: errors,
                    SkippedProtectedPaths: skipped,
                    FinalStage: InstallStage.Cancelled,
                    FailedOperationIndex: i);
            }
            catch (Exception ex)
            {
                errors.Add($"步骤 #{i + 1} {op.Description} 失败：{ex.Message}");
                Report(progress, i + 1, operationCount, InstallStage.Failed, $"失败：{ex.Message}", percent: (double)(i + 1) / Math.Max(1, operationCount), op.DestinationPath);
                return new InstallResult(
                    Succeeded: false,
                    Message: ex.Message,
                    BackupDirectory: backupDirectoryActual ?? plan.BackupDirectory,
                    WrittenPaths: written,
                    Errors: errors,
                    SkippedProtectedPaths: skipped,
                    FinalStage: InstallStage.Failed,
                    FailedOperationIndex: i);
            }
        }

        Report(progress, operationCount, operationCount, InstallStage.Completed, plan.IsDryRun ? "干跑完成" : "安装完成", percent: 1d);
        return new InstallResult(
            Succeeded: true,
            Message: plan.IsDryRun ? "干跑成功,未对游戏目录做任何写入。" : "安装成功。",
            BackupDirectory: backupDirectoryActual ?? plan.BackupDirectory,
            WrittenPaths: written,
            Errors: errors,
            SkippedProtectedPaths: skipped,
            FinalStage: InstallStage.Completed,
            FailedOperationIndex: -1);
    }

    private static string PerformBackup(InstallOperation op, string gameRoot, CancellationToken cancellationToken)
    {
        var source = op.SourcePath;
        var finalBackupDir = op.DestinationPath;

        if (!Directory.Exists(source))
        {
            // 没有现存插件目录可备份;依然创建空目录占位,方便回滚流程一致。
            Directory.CreateDirectory(finalBackupDir);
            return finalBackupDir;
        }

        var stagingRoot = Path.Combine(gameRoot, ".toolbox-staging", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stagingRoot);
        var files = new List<string>();
        try
        {
            CopyDirectory(source, stagingRoot, files, cancellationToken);

            if (Directory.Exists(finalBackupDir))
            {
                Directory.Delete(finalBackupDir, recursive: true);
            }
            var parent = Path.GetDirectoryName(finalBackupDir);
            if (!string.IsNullOrEmpty(parent))
            {
                Directory.CreateDirectory(parent);
            }
            Directory.Move(stagingRoot, finalBackupDir);

            var manifest = new BackupManifest(
                CreatedUtc: DateTimeOffset.UtcNow,
                SourceDirectory: source,
                Files: files
                    .Select(absolute => Path.GetRelativePath(stagingRoot, absolute))
                    .ToArray());
            var manifestPath = Path.Combine(finalBackupDir, "backup-manifest.json");
            File.WriteAllText(manifestPath, JsonSerializer.Serialize(manifest, BackupManifestJsonOptions));
            return finalBackupDir;
        }
        catch
        {
            // 备份失败,清理 staging 目录后让上层捕获并报告。
            if (Directory.Exists(stagingRoot))
            {
                try { Directory.Delete(stagingRoot, recursive: true); } catch { /* swallow */ }
            }
            throw;
        }
    }

    private static void PrepareUnityBaseLibraries(
        InstallOperation op,
        InstallPlan plan,
        UnityBaseLibraryProvider provider,
        List<string> written,
        IProgress<InstallProgress>? progress,
        int operationIndex,
        int operationCount,
        CancellationToken cancellationToken)
    {
        // SourceKind == LocalFile 表示用户在「自定义安装→开发者」里指定了一个本地 zip;
        // 否则 SourcePath 为 Unity 版本号,Provider 会自行决定走全局缓存还是网络。
        var customZipPath = op.SourceKind == InstallOperationSourceKind.LocalFile ? op.SourcePath : null;
        var unityVersion = op.SourceKind == InstallOperationSourceKind.LocalFile
            // 自定义 zip 时仍然需要一个版本字符串放到目标文件名里 — 从目标路径反推。
            ? Path.GetFileNameWithoutExtension(op.DestinationPath)
            : op.SourcePath;

        var bridge = progress is null
            ? null
            : new Progress<UnityBaseLibraryProgress>(p =>
            {
                progress.Report(new InstallProgress(
                    operationIndex,
                    operationCount,
                    InstallStage.PrepareUnityLibs,
                    p.Message,
                    operationCount == 0 ? 0 : (double)(operationIndex - 1) / operationCount,
                    op.DestinationPath));
            });

        var result = provider.EnsureAsync(plan.Inspection.GameRoot, unityVersion, customZipPath, bridge, cancellationToken)
            .GetAwaiter().GetResult();
        written.Add(result.DestinationPath);
    }

    private static void ExtractPackage(
        InstallOperation op,
        IReadOnlySet<string> protectedSet,
        List<string> written,
        List<string> skipped,
        CancellationToken cancellationToken)
    {
        Stream stream;
        switch (op.SourceKind)
        {
            case InstallOperationSourceKind.EmbeddedAsset:
                var asset = EmbeddedAssetCatalog.FindByKey(op.SourcePath)
                    ?? throw new InvalidOperationException($"内置资源条目缺失：{op.SourcePath}");
                stream = EmbeddedAssetCatalog.OpenStream(asset);
                break;

            case InstallOperationSourceKind.LocalFile:
                if (!File.Exists(op.SourcePath))
                {
                    throw new FileNotFoundException($"自定义 zip 文件不存在：{op.SourcePath}", op.SourcePath);
                }
                stream = File.OpenRead(op.SourcePath);
                break;

            default:
                throw new InvalidOperationException($"ExtractPackage 操作缺少有效的 SourceKind: {op.SourceKind}");
        }

        try
        {
            SafeZipExtractor.ExtractToDirectory(
                stream,
                op.DestinationPath,
                overwrite: true,
                protectedAbsolutePaths: protectedSet,
                onEntryWritten: entry =>
                {
                    var colonIndex = entry.IndexOf(':');
                    if (colonIndex < 0) { return; }
                    var tag = entry[..colonIndex];
                    var path = entry[(colonIndex + 1)..];
                    if (tag == "written") written.Add(path);
                    else if (tag == "skipped") skipped.Add(path);
                },
                cancellationToken: cancellationToken);
        }
        finally
        {
            stream.Dispose();
        }
    }

    private static RollbackResult Rollback(
        string backupDirectory,
        string gameRoot,
        IProgress<InstallProgress>? progress,
        CancellationToken cancellationToken)
    {
        var errors = new List<string>();
        var restored = new List<string>();
        Report(progress, 0, 3, InstallStage.Rollback, "读取备份清单", 0d);

        if (!Directory.Exists(backupDirectory))
        {
            return new RollbackResult(false, restored, new[] { $"备份目录不存在：{backupDirectory}" });
        }

        if (File.Exists(Path.Combine(backupDirectory, "rolled-back.flag")))
        {
            return new RollbackResult(false, restored, new[] { "该备份已被使用过,无法重复回滚。" });
        }

        BackupManifest? manifest = null;
        var manifestPath = Path.Combine(backupDirectory, "backup-manifest.json");
        if (File.Exists(manifestPath))
        {
            try
            {
                manifest = JsonSerializer.Deserialize<BackupManifest>(File.ReadAllText(manifestPath), BackupManifestJsonOptions);
            }
            catch (Exception ex)
            {
                errors.Add($"备份清单解析失败：{ex.Message}");
            }
        }

        // 删除当前插件目录,但绝不动 config/。
        var pluginDirectory = manifest?.SourceDirectory ?? Path.Combine(gameRoot, "BepInEx", "plugins", "HUnityAutoTranslator");
        Report(progress, 1, 3, InstallStage.Rollback, "清理当前插件目录", 0.3d, pluginDirectory);
        try
        {
            if (Directory.Exists(pluginDirectory))
            {
                Directory.Delete(pluginDirectory, recursive: true);
            }
            Directory.CreateDirectory(pluginDirectory);
        }
        catch (Exception ex)
        {
            errors.Add($"清理插件目录失败：{ex.Message}");
            return new RollbackResult(false, restored, errors);
        }

        // 把备份的文件复制回原位置。
        Report(progress, 2, 3, InstallStage.Rollback, "还原备份文件", 0.6d, pluginDirectory);
        try
        {
            foreach (var entry in Directory.EnumerateFileSystemEntries(backupDirectory, "*", SearchOption.AllDirectories))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var relative = Path.GetRelativePath(backupDirectory, entry);
                if (string.Equals(relative, "backup-manifest.json", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(relative, "rolled-back.flag", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var destinationPath = Path.Combine(pluginDirectory, relative);
                if (Directory.Exists(entry))
                {
                    Directory.CreateDirectory(destinationPath);
                    continue;
                }

                var destinationDir = Path.GetDirectoryName(destinationPath);
                if (!string.IsNullOrEmpty(destinationDir))
                {
                    Directory.CreateDirectory(destinationDir);
                }
                File.Copy(entry, destinationPath, overwrite: true);
                restored.Add(destinationPath);
            }
        }
        catch (Exception ex)
        {
            errors.Add($"还原失败：{ex.Message}");
            return new RollbackResult(false, restored, errors);
        }

        // 标记该备份已使用,防止重复回滚。
        try
        {
            File.WriteAllText(Path.Combine(backupDirectory, "rolled-back.flag"), DateTimeOffset.UtcNow.ToString("O"));
        }
        catch
        {
            // 标记失败不影响回滚结果。
        }

        Report(progress, 3, 3, InstallStage.Completed, "回滚完成", 1d);
        return new RollbackResult(true, restored, errors);
    }

    private static InstallStage ResolveStageFromOperation(InstallOperation op)
    {
        if (op.Kind == InstallOperationKind.BackupExisting) return InstallStage.Backup;
        if (op.Kind == InstallOperationKind.VerifyFile) return InstallStage.Verify;
        if (op.Kind == InstallOperationKind.PrepareUnityBaseLibraries) return InstallStage.PrepareUnityLibs;
        if (op.Kind == InstallOperationKind.ExtractPackage)
        {
            if (op.SourceKind == InstallOperationSourceKind.EmbeddedAsset)
            {
                var asset = EmbeddedAssetCatalog.FindByKey(op.SourcePath);
                if (asset is not null)
                {
                    return asset.Kind switch
                    {
                        EmbeddedAssetKind.BepInExFramework => InstallStage.ExtractFramework,
                        EmbeddedAssetKind.PluginPackage => InstallStage.ExtractPlugin,
                        EmbeddedAssetKind.LlamaCppBackend => InstallStage.ExtractLlamaCpp,
                        _ => InstallStage.Preparing
                    };
                }
            }
            // LocalFile 类型时按 description 关键字猜测,fallback 为 ExtractPlugin。
            if (op.Description.Contains("BepInEx", StringComparison.OrdinalIgnoreCase)) return InstallStage.ExtractFramework;
            if (op.Description.Contains("llama", StringComparison.OrdinalIgnoreCase)) return InstallStage.ExtractLlamaCpp;
            return InstallStage.ExtractPlugin;
        }
        return InstallStage.Preparing;
    }

    private static IReadOnlySet<string> BuildProtectedPathSet(IReadOnlyList<string> protectedPaths)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in protectedPaths)
        {
            if (string.IsNullOrWhiteSpace(path)) continue;
            set.Add(Path.GetFullPath(path));
        }
        return set;
    }

    private static void CopyDirectory(string sourceDirectory, string targetDirectory, List<string> filesOut, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(targetDirectory);
        foreach (var directory in Directory.EnumerateDirectories(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relative = Path.GetRelativePath(sourceDirectory, directory);
            Directory.CreateDirectory(Path.Combine(targetDirectory, relative));
        }
        foreach (var file in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relative = Path.GetRelativePath(sourceDirectory, file);
            var destination = Path.Combine(targetDirectory, relative);
            var destinationDir = Path.GetDirectoryName(destination);
            if (!string.IsNullOrEmpty(destinationDir))
            {
                Directory.CreateDirectory(destinationDir);
            }
            File.Copy(file, destination, overwrite: true);
            filesOut.Add(destination);
        }
    }

    private static void Report(IProgress<InstallProgress>? progress, int index, int count, InstallStage stage, string message, double percent, string? destination = null)
    {
        progress?.Report(new InstallProgress(index, count, stage, message, Math.Clamp(percent, 0d, 1d), destination));
    }

    private sealed record BackupManifest(
        [property: JsonPropertyName("createdUtc")] DateTimeOffset CreatedUtc,
        [property: JsonPropertyName("sourceDirectory")] string SourceDirectory,
        [property: JsonPropertyName("files")] IReadOnlyList<string> Files);
}

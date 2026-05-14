namespace HUnityAutoTranslator.Toolbox.Core.Installation;

public enum UnityBackend
{
    Unknown = 0,
    Mono = 1,
    IL2CPP = 2
}

public enum ToolboxRuntimeKind
{
    Unknown = 0,
    BepInEx5Mono = 1,
    Mono = 2,
    IL2CPP = 3
}

public enum InstallMode
{
    Full = 0,
    PluginOnly = 1,
    LlamaCppBackendOnly = 2
}

public enum LlamaCppBackendKind
{
    None = 0,
    Cuda13 = 1,
    Vulkan = 2
}

public enum InstallOperationKind
{
    CreateDirectory = 0,
    ExtractPackage = 1,
    BackupExisting = 2,
    PreserveUserData = 3,
    VerifyFile = 4,
    PrepareUnityBaseLibraries = 5
}

public enum InstallOperationSourceKind
{
    None = 0,
    EmbeddedAsset = 1,
    LocalFile = 2,
    Directory = 3
}

public enum BepInExHandling
{
    Auto = 0,
    Always = 1,
    Skip = 2
}

public enum BackupPolicy
{
    Auto = 0,
    Always = 1,
    Skip = 2
}

public enum InstallStage
{
    Preparing = 0,
    Backup = 1,
    ExtractFramework = 2,
    ExtractPlugin = 3,
    ExtractLlamaCpp = 4,
    Verify = 5,
    Completed = 6,
    Failed = 7,
    Cancelled = 8,
    Rollback = 9,
    PrepareUnityLibs = 10
}

public sealed record GameInspection(
    string GameRoot,
    bool DirectoryExists,
    bool IsValidUnityGame,
    string GameName,
    UnityBackend Backend,
    string Architecture,
    bool BepInExInstalled,
    string? BepInExVersion,
    ToolboxRuntimeKind RecommendedRuntime,
    bool PluginInstalled,
    string PluginDirectory,
    string ConfigDirectory,
    IReadOnlyList<string> ProtectedDataPaths);

public sealed record InstallPlanOptions(
    string PackageVersion,
    InstallMode Mode,
    bool IncludeLlamaCppBackend,
    LlamaCppBackendKind LlamaCppBackend,
    ToolboxRuntimeKind? RuntimeOverride = null,
    BepInExHandling BepInExHandling = BepInExHandling.Auto,
    BackupPolicy BackupPolicy = BackupPolicy.Auto,
    string? CustomPluginDirectory = null,
    string? CustomConfigDirectory = null,
    string? CustomBackupDirectory = null,
    string? CustomPluginZipPath = null,
    string? CustomBepInExZipPath = null,
    string? CustomLlamaCppZipPath = null,
    string? CustomUnityLibraryZipPath = null,
    string? UnityVersionOverride = null,
    bool DryRun = false,
    bool ForceReinstall = false,
    bool SkipPostInstallVerification = false);

public sealed record InstallOperation(
    InstallOperationKind Kind,
    string SourcePath,
    string DestinationPath,
    string Description,
    InstallOperationSourceKind SourceKind = InstallOperationSourceKind.None);

public sealed record InstallPlan(
    GameInspection Inspection,
    InstallMode Mode,
    string PluginPackageName,
    string? LlamaCppPackageName,
    IReadOnlyList<string> ProtectedPaths,
    IReadOnlyList<InstallOperation> Operations,
    string BackupDirectory,
    bool IsDryRun = false);

public sealed record InstallProgress(
    int OperationIndex,
    int OperationCount,
    InstallStage Stage,
    string Message,
    double Percent,
    string? CurrentDestination = null);

public sealed record InstallResult(
    bool Succeeded,
    string Message,
    string BackupDirectory,
    IReadOnlyList<string> WrittenPaths,
    IReadOnlyList<string> Errors,
    IReadOnlyList<string>? SkippedProtectedPaths = null,
    InstallStage FinalStage = InstallStage.Completed,
    int FailedOperationIndex = -1);

public sealed record RollbackResult(
    bool Succeeded,
    IReadOnlyList<string> RestoredPaths,
    IReadOnlyList<string> Errors);

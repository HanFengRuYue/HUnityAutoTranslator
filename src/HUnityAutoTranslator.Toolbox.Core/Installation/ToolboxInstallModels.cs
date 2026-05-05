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
    VerifyFile = 4
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
    LlamaCppBackendKind LlamaCppBackend);

public sealed record InstallOperation(
    InstallOperationKind Kind,
    string SourcePath,
    string DestinationPath,
    string Description);

public sealed record InstallPlan(
    GameInspection Inspection,
    InstallMode Mode,
    string PluginPackageName,
    string? LlamaCppPackageName,
    IReadOnlyList<string> ProtectedPaths,
    IReadOnlyList<InstallOperation> Operations,
    string BackupDirectory);

public sealed record InstallResult(
    bool Succeeded,
    string Message,
    string BackupDirectory,
    IReadOnlyList<string> WrittenPaths,
    IReadOnlyList<string> Errors);

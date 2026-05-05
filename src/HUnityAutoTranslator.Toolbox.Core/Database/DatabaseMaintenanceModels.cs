namespace HUnityAutoTranslator.Toolbox.Core.Database;

public sealed record DatabaseMaintenanceRequest(
    string DatabasePath,
    bool CreateBackup,
    bool RunIntegrityCheck,
    bool Reindex,
    bool Vacuum);

public sealed record DatabaseMaintenanceResult(
    string DatabasePath,
    string? BackupPath,
    bool IntegrityOk,
    IReadOnlyList<string> Actions,
    IReadOnlyList<string> Errors);

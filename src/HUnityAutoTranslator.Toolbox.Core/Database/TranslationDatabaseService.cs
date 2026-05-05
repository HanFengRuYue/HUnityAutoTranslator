using Microsoft.Data.Sqlite;

namespace HUnityAutoTranslator.Toolbox.Core.Database;

public static class TranslationDatabaseService
{
    public static DatabaseMaintenanceResult RunMaintenance(DatabaseMaintenanceRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.DatabasePath) || !File.Exists(request.DatabasePath))
        {
            throw new FileNotFoundException("SQLite database was not found.", request.DatabasePath);
        }

        var actions = new List<string>();
        var errors = new List<string>();
        string? backupPath = null;
        if (request.CreateBackup)
        {
            backupPath = CreateBackup(request.DatabasePath);
            actions.Add("BACKUP");
        }

        var integrityOk = true;
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = request.DatabasePath,
            Mode = SqliteOpenMode.ReadWriteCreate
        }.ToString();

        using var connection = new SqliteConnection(connectionString);
        connection.Open();

        if (request.RunIntegrityCheck)
        {
            using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA integrity_check;";
            var result = Convert.ToString(command.ExecuteScalar());
            integrityOk = string.Equals(result, "ok", StringComparison.OrdinalIgnoreCase);
            actions.Add("PRAGMA integrity_check");
            if (!integrityOk)
            {
                errors.Add(result ?? "integrity_check returned no result");
            }
        }

        if (request.Reindex)
        {
            using var command = connection.CreateCommand();
            command.CommandText = "REINDEX;";
            command.ExecuteNonQuery();
            actions.Add("REINDEX");
        }

        if (request.Vacuum)
        {
            using var command = connection.CreateCommand();
            command.CommandText = "VACUUM;";
            command.ExecuteNonQuery();
            actions.Add("VACUUM");
        }

        return new DatabaseMaintenanceResult(request.DatabasePath, backupPath, integrityOk, actions, errors);
    }

    private static string CreateBackup(string databasePath)
    {
        var directory = Path.GetDirectoryName(databasePath) ?? ".";
        var name = Path.GetFileNameWithoutExtension(databasePath);
        var extension = Path.GetExtension(databasePath);
        var backupDirectory = Path.Combine(directory, "toolbox-backups");
        Directory.CreateDirectory(backupDirectory);
        var backupPath = Path.Combine(backupDirectory, $"{name}-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}{extension}");
        File.Copy(databasePath, backupPath, overwrite: false);

        var walPath = databasePath + "-wal";
        if (File.Exists(walPath))
        {
            File.Copy(walPath, backupPath + "-wal", overwrite: false);
        }

        var shmPath = databasePath + "-shm";
        if (File.Exists(shmPath))
        {
            File.Copy(shmPath, backupPath + "-shm", overwrite: false);
        }

        return backupPath;
    }
}

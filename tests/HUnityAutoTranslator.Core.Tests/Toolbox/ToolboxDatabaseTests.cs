using FluentAssertions;
using HUnityAutoTranslator.Core.Caching;
using HUnityAutoTranslator.Core.Configuration;
using HUnityAutoTranslator.Toolbox.Core.Database;

namespace HUnityAutoTranslator.Core.Tests.Toolbox;

public sealed class ToolboxDatabaseTests
{
    [Fact]
    public void TranslationDatabaseService_creates_timestamped_backup_before_maintenance()
    {
        var root = Path.Combine(Path.GetTempPath(), "HUnityToolboxDatabaseTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var databasePath = Path.Combine(root, "translation-cache.sqlite");
        using (var cache = new SqliteTranslationCache(databasePath))
        {
            var key = TranslationCacheKey.Create("Start Game", "zh-Hans", ProviderProfile.DefaultOpenAi(), "prompt-v1");
            cache.Set(key, "开始游戏");
        }

        var result = TranslationDatabaseService.RunMaintenance(new DatabaseMaintenanceRequest(
            DatabasePath: databasePath,
            CreateBackup: true,
            RunIntegrityCheck: true,
            Reindex: true,
            Vacuum: false));

        result.BackupPath.Should().NotBeNull();
        File.Exists(result.BackupPath).Should().BeTrue();
        result.IntegrityOk.Should().BeTrue();
        result.Actions.Should().Contain("REINDEX");
    }
}

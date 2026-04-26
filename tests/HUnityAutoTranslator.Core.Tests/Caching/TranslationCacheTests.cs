using FluentAssertions;
using HUnityAutoTranslator.Core.Caching;
using HUnityAutoTranslator.Core.Configuration;
using Microsoft.Data.Sqlite;
using System.Text;

namespace HUnityAutoTranslator.Core.Tests.Caching;

public sealed class TranslationCacheTests
{
    [Fact]
    public void CacheKey_changes_when_target_language_or_model_changes()
    {
        var source = "Start Game";
        var openAi = new ProviderProfile(ProviderKind.OpenAI, "https://api.openai.com", "/v1/responses", "gpt-5.5", true);
        var deepSeek = new ProviderProfile(ProviderKind.DeepSeek, "https://api.deepseek.com", "/chat/completions", "deepseek-v4-flash", true);

        var zh = TranslationCacheKey.Create(source, "zh-Hans", openAi, "prompt-v1");
        var ja = TranslationCacheKey.Create(source, "ja", openAi, "prompt-v1");
        var ds = TranslationCacheKey.Create(source, "zh-Hans", deepSeek, "prompt-v1");

        zh.Should().NotBe(ja);
        zh.Should().NotBe(ds);
    }

    [Fact]
    public void CacheKey_keeps_readable_source_text_instead_of_hashing_it()
    {
        var key = TranslationCacheKey.Create("Start Game", "zh-Hans", ProviderProfile.DefaultOpenAi(), "prompt-v1");

        key.SourceText.Should().Be("Start Game");
    }

    [Fact]
    public void MemoryCache_roundtrips_translation()
    {
        var cache = new MemoryTranslationCache();
        var key = TranslationCacheKey.Create("Start Game", "zh-Hans", ProviderProfile.DefaultOpenAi(), "prompt-v1");

        cache.TryGet(key, out _).Should().BeFalse();
        cache.Set(key, "Start Game translated");
        cache.TryGet(key, out var translated).Should().BeTrue();
        translated.Should().Be("Start Game translated");
    }

    [Fact]
    public void SqliteCache_roundtrips_translation_after_reopen()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "translation-cache.sqlite");
        var key = TranslationCacheKey.Create("Start Game", "zh-Hans", ProviderProfile.DefaultOpenAi(), "prompt-v1");

        using (var first = new SqliteTranslationCache(path))
        {
            first.TryGet(key, out _).Should().BeFalse();
            first.Set(key, "Start Game translated");
            first.Count.Should().Be(1);
        }

        using var second = new SqliteTranslationCache(path);
        second.Count.Should().Be(1);
        second.TryGet(key, out var translated).Should().BeTrue();
        translated.Should().Be("Start Game translated");
    }

    [Fact]
    public void SqliteCache_stores_source_text_context_and_created_updated_timestamps()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "translation-cache.sqlite");
        var key = TranslationCacheKey.Create("Start Game", "zh-Hans", ProviderProfile.DefaultOpenAi(), "prompt-v1");
        var context = new TranslationCacheContext(
            SceneName: "MainMenu",
            ComponentHierarchy: "Canvas/Menu/StartButton",
            ComponentType: "UnityEngine.UI.Text");

        using (var cache = new SqliteTranslationCache(path))
        {
            cache.Set(key, "Start Game translated", context);
        }

        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path }.ToString());
        connection.Open();
        using var firstRead = connection.CreateCommand();
        firstRead.CommandText = """
SELECT source_text, target_language, provider_kind, provider_model, prompt_policy_version,
       translated_text, scene_name, component_hierarchy, component_type, created_utc, updated_utc
FROM translations;
""";

        using var firstReader = firstRead.ExecuteReader();
        firstReader.Read().Should().BeTrue();
        firstReader.GetString(0).Should().Be("Start Game");
        firstReader.GetString(1).Should().Be("zh-Hans");
        firstReader.GetString(2).Should().Be(nameof(ProviderKind.OpenAI));
        firstReader.GetString(3).Should().Be("gpt-5.5");
        firstReader.GetString(4).Should().Be("prompt-v1");
        firstReader.GetString(5).Should().Be("Start Game translated");
        firstReader.GetString(6).Should().Be("MainMenu");
        firstReader.GetString(7).Should().Be("Canvas/Menu/StartButton");
        firstReader.GetString(8).Should().Be("UnityEngine.UI.Text");
        var createdUtc = firstReader.GetString(9);
        var updatedUtc = firstReader.GetString(10);
        createdUtc.Should().NotBeNullOrWhiteSpace();
        updatedUtc.Should().Be(createdUtc);
        firstReader.Read().Should().BeFalse();

        Thread.Sleep(20);
        using (var cache = new SqliteTranslationCache(path))
        {
            cache.Set(
                key,
                "Start Game translated again",
                new TranslationCacheContext("Gameplay", "Root/Hud/StartButton", "TMPro.TMP_Text"));
        }

        using var secondRead = connection.CreateCommand();
        secondRead.CommandText = """
SELECT translated_text, scene_name, component_hierarchy, component_type, created_utc, updated_utc
FROM translations;
""";

        using var secondReader = secondRead.ExecuteReader();
        secondReader.Read().Should().BeTrue();
        secondReader.GetString(0).Should().Be("Start Game translated again");
        secondReader.GetString(1).Should().Be("Gameplay");
        secondReader.GetString(2).Should().Be("Root/Hud/StartButton");
        secondReader.GetString(3).Should().Be("TMPro.TMP_Text");
        secondReader.GetString(4).Should().Be(createdUtc);
        secondReader.GetString(5).Should().NotBe(createdUtc);
    }

    [Fact]
    public void SqliteCache_records_pending_source_without_treating_it_as_cached_translation()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "translation-cache.sqlite");
        var provider = ProviderProfile.DefaultOpenAi();
        var key = TranslationCacheKey.Create("Continue", "zh-Hans", provider, "prompt-v1");
        var context = new TranslationCacheContext("MainMenu", "Canvas/Continue", "UnityEngine.UI.Text");

        using (var cache = new SqliteTranslationCache(path))
        {
            cache.RecordCaptured(key, context);

            cache.TryGet(key, out _).Should().BeFalse();
            var pending = cache.GetPendingTranslations("zh-Hans", provider, "prompt-v1", limit: 10);
            pending.Should().ContainSingle();
            pending[0].SourceText.Should().Be("Continue");
            pending[0].TranslatedText.Should().BeNull();
            pending[0].SceneName.Should().Be("MainMenu");
        }

        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT translated_text FROM translations WHERE source_text = 'Continue';";
        command.ExecuteScalar().Should().Be(DBNull.Value);
    }

    [Fact]
    public void MemoryCache_count_excludes_pending_captures()
    {
        var cache = new MemoryTranslationCache();
        var key = TranslationCacheKey.Create("Continue", "zh-Hans", ProviderProfile.DefaultOpenAi(), "prompt-v1");

        cache.RecordCaptured(key, TranslationCacheContext.Empty);

        cache.Count.Should().Be(0);
        cache.Set(key, "Continue translated", TranslationCacheContext.Empty);
        cache.Count.Should().Be(1);
    }

    [Fact]
    public void SqliteCache_count_excludes_pending_captures()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "translation-cache.sqlite");
        var cache = new SqliteTranslationCache(path);
        var key = TranslationCacheKey.Create("Continue", "zh-Hans", ProviderProfile.DefaultOpenAi(), "prompt-v1");

        cache.RecordCaptured(key, TranslationCacheContext.Empty);

        cache.Count.Should().Be(0);
        cache.Set(key, "Continue translated", TranslationCacheContext.Empty);
        cache.Count.Should().Be(1);
    }

    [Fact]
    public void SqliteCache_completes_pending_source_and_preserves_created_timestamp()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "translation-cache.sqlite");
        var provider = ProviderProfile.DefaultOpenAi();
        var key = TranslationCacheKey.Create("Continue", "zh-Hans", provider, "prompt-v1");

        DateTimeOffset createdUtc;
        using (var cache = new SqliteTranslationCache(path))
        {
            cache.RecordCaptured(key, new TranslationCacheContext("MainMenu", "Canvas/Continue", "UnityEngine.UI.Text"));
            createdUtc = cache.Query(new TranslationCacheQuery("Continue", "source_text", false, 0, 10)).Items[0].CreatedUtc;
            Thread.Sleep(20);

            cache.Set(key, "继续", new TranslationCacheContext("MainMenu", "Canvas/Continue", "UnityEngine.UI.Text"));
        }

        using var reopened = new SqliteTranslationCache(path);
        reopened.TryGet(key, out var translated).Should().BeTrue();
        translated.Should().Be("继续");
        reopened.GetPendingTranslations("zh-Hans", provider, "prompt-v1", limit: 10).Should().BeEmpty();
        var row = reopened.Query(new TranslationCacheQuery("Continue", "source_text", false, 0, 10)).Items[0];
        row.CreatedUtc.Should().Be(createdUtc);
        row.UpdatedUtc.Should().BeAfter(createdUtc);
    }

    [Fact]
    public void SqliteCache_keeps_readable_rows_when_migrating_translated_text_to_nullable()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "translation-cache.sqlite");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path }.ToString()))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
CREATE TABLE translations (
    source_text TEXT NOT NULL,
    target_language TEXT NOT NULL,
    provider_kind TEXT NOT NULL,
    provider_base_url TEXT NOT NULL,
    provider_endpoint TEXT NOT NULL,
    provider_model TEXT NOT NULL,
    prompt_policy_version TEXT NOT NULL,
    translated_text TEXT NOT NULL,
    scene_name TEXT NULL,
    component_hierarchy TEXT NULL,
    component_type TEXT NULL,
    created_utc TEXT NOT NULL,
    updated_utc TEXT NOT NULL,
    PRIMARY KEY (
        source_text,
        target_language,
        provider_kind,
        provider_base_url,
        provider_endpoint,
        provider_model,
        prompt_policy_version)
);
INSERT INTO translations (
    source_text,
    target_language,
    provider_kind,
    provider_base_url,
    provider_endpoint,
    provider_model,
    prompt_policy_version,
    translated_text,
    scene_name,
    component_hierarchy,
    component_type,
    created_utc,
    updated_utc)
VALUES (
    'Start Game',
    'zh-Hans',
    'OpenAI',
    'https://api.openai.com',
    '/v1/responses',
    'gpt-5.5',
    'prompt-v1',
    '开始游戏',
    'Menu',
    'Canvas/Start',
    'Text',
    '2026-04-26T00:00:00Z',
    '2026-04-26T00:00:00Z');
""";
            command.ExecuteNonQuery();
        }

        using (var cache = new SqliteTranslationCache(path))
        {
            cache.Count.Should().Be(1);
            cache.RecordCaptured(
                TranslationCacheKey.Create("Options", "zh-Hans", ProviderProfile.DefaultOpenAi(), "prompt-v1"),
                new TranslationCacheContext("Menu", "Canvas/Options", "Text"));
        }

        using var readConnection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path }.ToString());
        readConnection.Open();
        using var schema = readConnection.CreateCommand();
        schema.CommandText = "SELECT [notnull] FROM pragma_table_info('translations') WHERE name = 'translated_text';";
        Convert.ToInt32(schema.ExecuteScalar()).Should().Be(0);

        using var rows = readConnection.CreateCommand();
        rows.CommandText = "SELECT translated_text FROM translations WHERE source_text = 'Start Game';";
        rows.ExecuteScalar().Should().Be("开始游戏");

        using var pending = readConnection.CreateCommand();
        pending.CommandText = "SELECT translated_text FROM translations WHERE source_text = 'Options';";
        pending.ExecuteScalar().Should().Be(DBNull.Value);
    }

    [Fact]
    public void SqliteCache_starts_empty_when_only_legacy_tsv_exists()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var sqlitePath = Path.Combine(directory, "translation-cache.sqlite");
        var legacyPath = Path.Combine(directory, "translation-cache.tsv");
        var key = TranslationCacheKey.Create("legacy source", "zh-Hans", ProviderProfile.DefaultOpenAi(), "prompt-v1");
        Directory.CreateDirectory(directory);
        File.WriteAllText(
            legacyPath,
            "legacy-cache-key\t" + Convert.ToBase64String(Encoding.UTF8.GetBytes("legacy translated")));

        using var cache = new SqliteTranslationCache(sqlitePath);

        cache.Count.Should().Be(0);
        cache.TryGet(key, out _).Should().BeFalse();
    }

    [Fact]
    public void SqliteCache_discards_legacy_hash_schema_and_starts_new_readable_schema()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "translation-cache.sqlite");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path }.ToString()))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
CREATE TABLE translations (
    key TEXT PRIMARY KEY NOT NULL,
    translated_text TEXT NOT NULL,
    updated_utc TEXT NOT NULL
);
INSERT INTO translations (key, translated_text, updated_utc)
VALUES ('legacy-cache-key', 'legacy translated', '2026-04-25T00:00:00Z');
""";
            command.ExecuteNonQuery();
        }

        using (var cache = new SqliteTranslationCache(path))
        {
            cache.Count.Should().Be(0);
        }

        using var readConnection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path }.ToString());
        readConnection.Open();
        using var legacyTable = readConnection.CreateCommand();
        legacyTable.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'translations_legacy_hash';";
        Convert.ToInt32(legacyTable.ExecuteScalar()).Should().Be(0);

        using var schema = readConnection.CreateCommand();
        schema.CommandText = "SELECT COUNT(*) FROM pragma_table_info('translations') WHERE name = 'source_text';";
        Convert.ToInt32(schema.ExecuteScalar()).Should().Be(1);

        using var rowCount = readConnection.CreateCommand();
        rowCount.CommandText = "SELECT COUNT(*) FROM translations;";
        Convert.ToInt32(rowCount.ExecuteScalar()).Should().Be(0);
    }

    [Fact]
    public void Sqlite_cache_queries_updates_and_exports_translation_rows()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "translation-cache.sqlite");
        using var cache = new SqliteTranslationCache(path);
        var key = TranslationCacheKey.Create("Start Game", "zh-Hans", ProviderProfile.DefaultOpenAi(), "prompt-v1");
        cache.Set(key, "Start translated", new TranslationCacheContext("Menu", "Canvas/Button", "Text"));

        var page = cache.Query(new TranslationCacheQuery(Search: "Start", SortColumn: "source_text", SortDescending: false, Offset: 0, Limit: 20));
        page.TotalCount.Should().Be(1);
        page.Items[0].SourceText.Should().Be("Start Game");
        page.Items[0].TranslatedText.Should().Be("Start translated");

        cache.Update(new TranslationCacheEntry(
            SourceText: "Start Game",
            TargetLanguage: "zh-Hans",
            ProviderKind: "OpenAI",
            ProviderBaseUrl: "https://api.openai.com",
            ProviderEndpoint: "/v1/responses",
            ProviderModel: "gpt-5.5",
            PromptPolicyVersion: "prompt-v1",
            TranslatedText: "Start edited",
            SceneName: "Menu",
            ComponentHierarchy: "Canvas/Button",
            ComponentType: "Text",
            ReplacementFont: null,
            CreatedUtc: page.Items[0].CreatedUtc,
            UpdatedUtc: DateTimeOffset.Parse("2026-04-26T00:00:00Z")));

        cache.Query(new TranslationCacheQuery(Search: "Start", SortColumn: "updated_utc", SortDescending: true, Offset: 0, Limit: 20))
            .Items[0].TranslatedText.Should().Be("Start edited");
        cache.Export("json").Should().Contain("Start Game");
        cache.Export("csv").Should().Contain("source_text");
    }

    [Fact]
    public void Sqlite_cache_deletes_selected_translation_row()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "translation-cache.sqlite");
        using var cache = new SqliteTranslationCache(path);
        var key = TranslationCacheKey.Create("Delete Me", "zh-Hans", ProviderProfile.DefaultOpenAi(), "prompt-v1");
        cache.Set(key, "Delete translated", new TranslationCacheContext("Menu", "Canvas/Delete", "Text"));
        var row = cache.Query(new TranslationCacheQuery(Search: "Delete", SortColumn: "source_text", SortDescending: false, Offset: 0, Limit: 20)).Items[0];

        cache.Delete(row);

        cache.Count.Should().Be(0);
        cache.TryGet(key, out _).Should().BeFalse();
    }

    [Fact]
    public void Sqlite_cache_imports_valid_json_rows()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "translation-cache.sqlite");
        using var cache = new SqliteTranslationCache(path);
        var json = """
[
  {
    "source_text": "Inventory Full",
    "target_language": "zh-Hans",
    "provider_kind": "OpenAI",
    "provider_base_url": "https://api.openai.com",
    "provider_endpoint": "/v1/responses",
    "provider_model": "gpt-5.5",
    "prompt_policy_version": "prompt-v1",
    "translated_text": "Inventory full translated",
    "scene_name": "Hud",
    "component_hierarchy": "Canvas/Toast",
    "component_type": "Text"
  }
]
""";

        var result = cache.Import(json, "json");

        result.ImportedCount.Should().Be(1);
        cache.Query(new TranslationCacheQuery(Search: "Inventory", SortColumn: "source_text", SortDescending: false, Offset: 0, Limit: 20))
            .Items[0].TranslatedText.Should().Be("Inventory full translated");
    }

    [Fact]
    public void Sqlite_cache_imports_its_exported_json_rows()
    {
        var sourcePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "source.sqlite");
        var targetPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "target.sqlite");
        var key = TranslationCacheKey.Create("Settings", "zh-Hans", ProviderProfile.DefaultOpenAi(), "prompt-v1");
        using var source = new SqliteTranslationCache(sourcePath);
        source.Set(key, "Settings translated", new TranslationCacheContext("Menu", "Canvas/Settings", "Text"));
        var json = source.Export("json");

        using var target = new SqliteTranslationCache(targetPath);
        var result = target.Import(json, "json");

        result.ImportedCount.Should().Be(1);
        result.Errors.Should().BeEmpty();
        target.Query(new TranslationCacheQuery(Search: "Settings", SortColumn: "source_text", SortDescending: false, Offset: 0, Limit: 20))
            .Items[0].TranslatedText.Should().Be("Settings translated");
    }

    [Fact]
    public void Cache_rows_store_component_font_override_and_match_exact_context()
    {
        var memory = new MemoryTranslationCache();
        var key = TranslationCacheKey.Create("Start Game", "zh-Hans", ProviderProfile.DefaultOpenAi(), "prompt-v1");
        var context = new TranslationCacheContext("Menu", "Canvas/Start", "UnityEngine.UI.Text");
        var now = DateTimeOffset.UtcNow;

        memory.Update(new TranslationCacheEntry(
            SourceText: "Start Game",
            TargetLanguage: "zh-Hans",
            ProviderKind: "OpenAI",
            ProviderBaseUrl: "https://api.openai.com",
            ProviderEndpoint: "/v1/responses",
            ProviderModel: "gpt-5.5",
            PromptPolicyVersion: "prompt-v1",
            TranslatedText: "Start translated",
            SceneName: "Menu",
            ComponentHierarchy: "Canvas/Start",
            ComponentType: "UnityEngine.UI.Text",
            ReplacementFont: "Noto Sans SC",
            CreatedUtc: now,
            UpdatedUtc: now));

        memory.TryGetReplacementFont(key, context, out var replacementFont).Should().BeTrue();
        replacementFont.Should().Be("Noto Sans SC");
        memory.TryGetReplacementFont(key, new TranslationCacheContext("Menu", "Canvas/Other", "UnityEngine.UI.Text"), out _)
            .Should().BeFalse();
    }

    [Fact]
    public void Sqlite_cache_persists_component_font_override_and_exports_it()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "translation-cache.sqlite");
        var key = TranslationCacheKey.Create("Start Game", "zh-Hans", ProviderProfile.DefaultOpenAi(), "prompt-v1");
        var context = new TranslationCacheContext("Menu", "Canvas/Start", "UnityEngine.UI.Text");
        var now = DateTimeOffset.UtcNow;

        using (var cache = new SqliteTranslationCache(path))
        {
            cache.Update(new TranslationCacheEntry(
                SourceText: "Start Game",
                TargetLanguage: "zh-Hans",
                ProviderKind: "OpenAI",
                ProviderBaseUrl: "https://api.openai.com",
                ProviderEndpoint: "/v1/responses",
                ProviderModel: "gpt-5.5",
                PromptPolicyVersion: "prompt-v1",
                TranslatedText: "Start translated",
                SceneName: "Menu",
                ComponentHierarchy: "Canvas/Start",
                ComponentType: "UnityEngine.UI.Text",
                ReplacementFont: @"C:\Fonts\NotoSansSC-Regular.otf",
                CreatedUtc: now,
                UpdatedUtc: now));
        }

        using var reopened = new SqliteTranslationCache(path);
        reopened.TryGetReplacementFont(key, context, out var replacementFont).Should().BeTrue();
        replacementFont.Should().Be(@"C:\Fonts\NotoSansSC-Regular.otf");
        reopened.Query(new TranslationCacheQuery("NotoSans", "replacement_font", false, 0, 10))
            .Items[0].ReplacementFont.Should().Be(@"C:\Fonts\NotoSansSC-Regular.otf");
        reopened.Export("json").Should().Contain("ReplacementFont");
        reopened.Export("csv").Should().Contain("replacement_font");
    }
}

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

        cache.TryGet(key, TranslationCacheContext.Empty, out _).Should().BeFalse();
        cache.Set(key, "Start Game translated");
        cache.TryGet(key, TranslationCacheContext.Empty, out var translated).Should().BeTrue();
        translated.Should().Be("Start Game translated");
    }

    [Fact]
    public void MemoryCache_keeps_context_specific_translations_for_same_source_text()
    {
        var cache = new MemoryTranslationCache();
        var key = TranslationCacheKey.Create("Back", "zh-Hans", ProviderProfile.DefaultOpenAi(), "prompt-v1");
        var menuContext = new TranslationCacheContext("MainMenu", "Canvas/Menu/Back", "UnityEngine.UI.Text");
        var hudContext = new TranslationCacheContext("Gameplay", "Canvas/Hud/Back", "UnityEngine.UI.Text");

        cache.Set(key, "返回菜单", menuContext);
        cache.Set(key, "返回", hudContext);

        cache.Count.Should().Be(2);
    }

    [Fact]
    public void MemoryCache_reuses_translation_when_provider_changes_but_lookup_context_matches()
    {
        var cache = new MemoryTranslationCache();
        var context = new TranslationCacheContext("MainMenu", "Canvas/Menu/Start", "UnityEngine.UI.Text");
        var openAiKey = TranslationCacheKey.Create("Start Game", "zh-Hans", ProviderProfile.DefaultOpenAi(), "prompt-v1");
        var deepSeekKey = TranslationCacheKey.Create("Start Game", "zh-Hans", ProviderProfile.DefaultDeepSeek(), "prompt-v1");

        cache.Set(openAiKey, "开始游戏", context);

        cache.TryGet(deepSeekKey, context, out var translated).Should().BeTrue();
        translated.Should().Be("开始游戏");
    }

    [Fact]
    public void Memory_cache_applies_column_filters_with_and_between_columns_and_or_within_one_column()
    {
        var cache = new MemoryTranslationCache();
        var now = DateTimeOffset.Parse("2026-04-26T00:00:00Z");
        cache.Update(SampleRow("Start Game", "Menu", "Canvas/Start", "UnityEngine.UI.Text", "Start translated", now));
        cache.Update(SampleRow("Options", "Menu", "Canvas/Options", "TMPro.TextMeshProUGUI", "Options translated", now.AddMinutes(1)));
        cache.Update(SampleRow("Jump", "Gameplay", "Canvas/Hud/Jump", "UnityEngine.UI.Text", "Jump translated", now.AddMinutes(2)));

        var page = cache.Query(new TranslationCacheQuery(
            Search: null,
            SortColumn: "source_text",
            SortDescending: false,
            Offset: 0,
            Limit: 20,
            ColumnFilters: new[]
            {
                new TranslationCacheColumnFilter("scene_name", new string?[] { "Menu" }),
                new TranslationCacheColumnFilter("component_type", new string?[] { "UnityEngine.UI.Text", "TMPro.TextMeshProUGUI" })
            }));

        page.TotalCount.Should().Be(2);
        page.Items.Select(row => row.SourceText).Should().Equal("Options", "Start Game");
    }

    [Fact]
    public void Memory_cache_column_filters_match_empty_context_values()
    {
        var cache = new MemoryTranslationCache();
        var now = DateTimeOffset.Parse("2026-04-26T00:00:00Z");
        cache.Update(SampleRow("No Scene", "", "", null, "No scene translated", now));
        cache.Update(SampleRow("Menu Row", "Menu", "Canvas/Menu", "UnityEngine.UI.Text", "Menu translated", now.AddMinutes(1)));

        var page = cache.Query(new TranslationCacheQuery(
            Search: null,
            SortColumn: "source_text",
            SortDescending: false,
            Offset: 0,
            Limit: 20,
            ColumnFilters: new[]
            {
                new TranslationCacheColumnFilter("scene_name", new string?[] { null })
            }));

        page.TotalCount.Should().Be(1);
        page.Items[0].SourceText.Should().Be("No Scene");
    }

    [Fact]
    public void Memory_cache_filter_options_respect_other_active_column_filters()
    {
        var cache = new MemoryTranslationCache();
        var now = DateTimeOffset.Parse("2026-04-26T00:00:00Z");
        cache.Update(SampleRow("Start Game", "Menu", "Canvas/Start", "UnityEngine.UI.Text", "Start translated", now));
        cache.Update(SampleRow("Options", "Menu", "Canvas/Options", "TMPro.TextMeshProUGUI", "Options translated", now.AddMinutes(1)));
        cache.Update(SampleRow("Jump", "Gameplay", "Canvas/Hud/Jump", "UnityEngine.UI.Text", "Jump translated", now.AddMinutes(2)));

        var options = cache.GetFilterOptions(new TranslationCacheFilterOptionsQuery(
            Column: "component_type",
            Search: null,
            ColumnFilters: new[]
            {
                new TranslationCacheColumnFilter("scene_name", new string?[] { "Menu" })
            },
            OptionSearch: null,
            Limit: 20));

        options.Column.Should().Be("component_type");
        options.Items.Select(item => item.Value).Should().Equal("TMPro.TextMeshProUGUI", "UnityEngine.UI.Text");
        options.Items.Sum(item => item.Count).Should().Be(2);
    }

    [Fact]
    public void Memory_cache_returns_component_context_before_scene_fallback()
    {
        var cache = new MemoryTranslationCache();
        var now = DateTimeOffset.Parse("2026-04-26T00:00:00Z");
        cache.Update(SampleRow("Current", "Menu", "Canvas/Dialog", "Text", "Current translated", now.AddMinutes(9)));
        cache.Update(SampleRow("Same component newer", "Menu", "Canvas/Dialog", "Text", "Component newer translated", now.AddMinutes(3)));
        cache.Update(SampleRow("Same component older", "Menu", "Canvas/Dialog", "Text", "Component older translated", now.AddMinutes(2)));
        cache.Update(SampleRow("Scene fallback newest", "Menu", "Canvas/Other", "Text", "Scene fallback translated", now.AddMinutes(8)));
        cache.Update(SampleRow("Other scene", "Battle", "Canvas/Dialog", "Text", "Other scene translated", now.AddMinutes(7)));
        cache.Update(SampleRow("Pending only", "Menu", "Canvas/Dialog", "Text", null, now.AddMinutes(6)));

        var examples = cache.GetTranslationContextExamples(
            "Current",
            "zh-Hans",
            new TranslationCacheContext("Menu", "Canvas/Dialog", "Text"),
            maxExamples: 3,
            maxCharacters: 1000);

        examples.Select(item => item.SourceText).Should().Equal(
            "Same component newer",
            "Same component older",
            "Scene fallback newest");
    }

    [Fact]
    public void Memory_cache_limits_context_examples_by_combined_source_and_translation_characters()
    {
        var cache = new MemoryTranslationCache();
        var now = DateTimeOffset.Parse("2026-04-26T00:00:00Z");
        cache.Update(SampleRow("Short", "Menu", "Canvas/Dialog", "Text", "Tiny", now.AddMinutes(2)));
        cache.Update(SampleRow("Very long source text", "Menu", "Canvas/Dialog", "Text", "Very long translated text", now.AddMinutes(1)));

        var examples = cache.GetTranslationContextExamples(
            "Current",
            "zh-Hans",
            new TranslationCacheContext("Menu", "Canvas/Dialog", "Text"),
            maxExamples: 4,
            maxCharacters: 10);

        examples.Should().ContainSingle();
        examples[0].SourceText.Should().Be("Short");
    }

    [Fact]
    public void SqliteCache_roundtrips_translation_after_reopen()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "translation-cache.sqlite");
        var key = TranslationCacheKey.Create("Start Game", "zh-Hans", ProviderProfile.DefaultOpenAi(), "prompt-v1");

        using (var first = new SqliteTranslationCache(path))
        {
            first.TryGet(key, TranslationCacheContext.Empty, out _).Should().BeFalse();
            first.Set(key, "Start Game translated");
            first.Count.Should().Be(1);
        }

        using var second = new SqliteTranslationCache(path);
        second.Count.Should().Be(1);
        second.TryGet(key, TranslationCacheContext.Empty, out var translated).Should().BeTrue();
        translated.Should().Be("Start Game translated");
    }

    [Fact]
    public void SqliteCache_keeps_context_specific_translations_for_same_source_text()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "translation-cache.sqlite");
        var key = TranslationCacheKey.Create("Back", "zh-Hans", ProviderProfile.DefaultOpenAi(), "prompt-v1");
        var menuContext = new TranslationCacheContext("MainMenu", "Canvas/Menu/Back", "UnityEngine.UI.Text");
        var hudContext = new TranslationCacheContext("Gameplay", "Canvas/Hud/Back", "UnityEngine.UI.Text");

        using (var cache = new SqliteTranslationCache(path))
        {
            cache.Set(key, "返回菜单", menuContext);
            cache.Set(key, "返回", hudContext);
        }

        using var reopened = new SqliteTranslationCache(path);
        reopened.Count.Should().Be(2);
        var rows = reopened.Query(new TranslationCacheQuery("Back", "component_hierarchy", false, 0, 10)).Items;
        rows.Should().HaveCount(2);
        rows.Should().Contain(row => row.ComponentHierarchy == "Canvas/Menu/Back" && row.TranslatedText == "返回菜单");
        rows.Should().Contain(row => row.ComponentHierarchy == "Canvas/Hud/Back" && row.TranslatedText == "返回");
    }

    [Fact]
    public void SqliteCache_reuses_translation_when_provider_changes_but_lookup_context_matches()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "translation-cache.sqlite");
        var context = new TranslationCacheContext("MainMenu", "Canvas/Menu/Start", "UnityEngine.UI.Text");
        var openAiKey = TranslationCacheKey.Create("Start Game", "zh-Hans", ProviderProfile.DefaultOpenAi(), "prompt-v1");
        var deepSeekKey = TranslationCacheKey.Create("Start Game", "zh-Hans", ProviderProfile.DefaultDeepSeek(), "prompt-v1");

        using (var cache = new SqliteTranslationCache(path))
        {
            cache.Set(openAiKey, "开始游戏", context);
        }

        using var reopened = new SqliteTranslationCache(path);
        reopened.TryGet(deepSeekKey, context, out var translated).Should().BeTrue();
        translated.Should().Be("开始游戏");
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

        using var originalRow = connection.CreateCommand();
        originalRow.CommandText = "SELECT translated_text FROM translations WHERE scene_name = 'MainMenu' AND component_hierarchy = 'Canvas/Menu/StartButton';";
        originalRow.ExecuteScalar().Should().Be("Start Game translated");

        using var secondRead = connection.CreateCommand();
        secondRead.CommandText = """
SELECT translated_text, scene_name, component_hierarchy, component_type, created_utc, updated_utc
FROM translations
WHERE scene_name = 'Gameplay'
  AND component_hierarchy = 'Root/Hud/StartButton';
""";

        using var secondReader = secondRead.ExecuteReader();
        secondReader.Read().Should().BeTrue();
        secondReader.GetString(0).Should().Be("Start Game translated again");
        secondReader.GetString(1).Should().Be("Gameplay");
        secondReader.GetString(2).Should().Be("Root/Hud/StartButton");
        secondReader.GetString(3).Should().Be("TMPro.TMP_Text");
        secondReader.GetString(4).Should().NotBeNullOrWhiteSpace();
        secondReader.GetString(5).Should().Be(secondReader.GetString(4));
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

            cache.TryGet(key, context, out _).Should().BeFalse();
            var pending = cache.GetPendingTranslations("zh-Hans", "prompt-v1", limit: 10);
            pending.Should().ContainSingle();
            pending[0].SourceText.Should().Be("Continue");
            pending[0].TranslatedText.Should().BeNull();
            pending[0].ProviderKind.Should().BeEmpty();
            pending[0].ProviderBaseUrl.Should().BeEmpty();
            pending[0].ProviderEndpoint.Should().BeEmpty();
            pending[0].ProviderModel.Should().BeEmpty();
            pending[0].SceneName.Should().Be("MainMenu");
        }

        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
SELECT translated_text, provider_kind, provider_base_url, provider_endpoint, provider_model
FROM translations
WHERE source_text = 'Continue';
""";
        using var reader = command.ExecuteReader();
        reader.Read().Should().BeTrue();
        reader.IsDBNull(0).Should().BeTrue();
        reader.IsDBNull(1).Should().BeTrue();
        reader.IsDBNull(2).Should().BeTrue();
        reader.IsDBNull(3).Should().BeTrue();
        reader.IsDBNull(4).Should().BeTrue();
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
        reopened.TryGet(key, new TranslationCacheContext("MainMenu", "Canvas/Continue", "UnityEngine.UI.Text"), out var translated).Should().BeTrue();
        translated.Should().Be("继续");
        reopened.GetPendingTranslations("zh-Hans", "prompt-v1", limit: 10).Should().BeEmpty();
        var row = reopened.Query(new TranslationCacheQuery("Continue", "source_text", false, 0, 10)).Items[0];
        row.CreatedUtc.Should().Be(createdUtc);
        row.UpdatedUtc.Should().BeAfter(createdUtc);
    }

    [Fact]
    public void SqliteCache_records_provider_metadata_only_when_translation_is_written()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "translation-cache.sqlite");
        var openAiKey = TranslationCacheKey.Create("Settings", "zh-Hans", ProviderProfile.DefaultOpenAi(), "prompt-v1");
        var deepSeek = ProviderProfile.DefaultDeepSeek();
        var deepSeekKey = TranslationCacheKey.Create("Settings", "zh-Hans", deepSeek, "prompt-v1");
        var context = new TranslationCacheContext("Menu", "Canvas/Settings", "Text");

        using (var cache = new SqliteTranslationCache(path))
        {
            cache.RecordCaptured(openAiKey, context);
            cache.Set(deepSeekKey, "Settings translated", context);
        }

        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
SELECT provider_kind, provider_base_url, provider_endpoint, provider_model, translated_text
FROM translations
WHERE source_text = 'Settings';
""";
        using var reader = command.ExecuteReader();
        reader.Read().Should().BeTrue();
        reader.GetString(0).Should().Be(nameof(ProviderKind.DeepSeek));
        reader.GetString(1).Should().Be(deepSeek.BaseUrl);
        reader.GetString(2).Should().Be(deepSeek.Endpoint);
        reader.GetString(3).Should().Be(deepSeek.Model);
        reader.GetString(4).Should().Be("Settings translated");
    }

    [Fact]
    public void SqliteCache_migrates_context_key_schema_to_nullable_pending_provider_metadata()
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
    translated_text TEXT NULL,
    scene_name TEXT NOT NULL,
    component_hierarchy TEXT NOT NULL,
    component_type TEXT NULL,
    replacement_font TEXT NULL,
    created_utc TEXT NOT NULL,
    updated_utc TEXT NOT NULL,
    PRIMARY KEY (
        source_text,
        target_language,
        scene_name,
        component_hierarchy)
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
    replacement_font,
    created_utc,
    updated_utc)
VALUES
    (
        'Pending text',
        'zh-Hans',
        'OpenAI',
        'https://api.openai.com',
        '/v1/responses',
        'gpt-5.5',
        'prompt-v1',
        NULL,
        'Menu',
        'Canvas/Pending',
        'Text',
        NULL,
        '2026-04-26T00:00:00Z',
        '2026-04-26T00:00:00Z'),
    (
        'Completed text',
        'zh-Hans',
        'DeepSeek',
        'https://api.deepseek.com',
        '/chat/completions',
        'deepseek-v4-flash',
        'prompt-v1',
        'Completed translated',
        'Menu',
        'Canvas/Completed',
        'Text',
        NULL,
        '2026-04-26T00:00:00Z',
        '2026-04-26T00:00:00Z');
""";
            command.ExecuteNonQuery();
        }

        using (var cache = new SqliteTranslationCache(path))
        {
            var pending = cache.GetPendingTranslations("zh-Hans", "prompt-v1", limit: 10);
            pending.Should().ContainSingle();
            pending[0].SourceText.Should().Be("Pending text");
            pending[0].ProviderKind.Should().BeEmpty();
            cache.Count.Should().Be(1);
        }

        using var readConnection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path }.ToString());
        readConnection.Open();
        using var schema = readConnection.CreateCommand();
        schema.CommandText = """
SELECT COUNT(*)
FROM pragma_table_info('translations')
WHERE name IN ('provider_kind', 'provider_base_url', 'provider_endpoint', 'provider_model')
  AND [notnull] = 0;
""";
        Convert.ToInt32(schema.ExecuteScalar()).Should().Be(4);

        using var pendingRow = readConnection.CreateCommand();
        pendingRow.CommandText = """
SELECT provider_kind, provider_base_url, provider_endpoint, provider_model
FROM translations
WHERE source_text = 'Pending text';
""";
        using (var reader = pendingRow.ExecuteReader())
        {
            reader.Read().Should().BeTrue();
            reader.IsDBNull(0).Should().BeTrue();
            reader.IsDBNull(1).Should().BeTrue();
            reader.IsDBNull(2).Should().BeTrue();
            reader.IsDBNull(3).Should().BeTrue();
        }

        using var completedRow = readConnection.CreateCommand();
        completedRow.CommandText = """
SELECT provider_kind, provider_model
FROM translations
WHERE source_text = 'Completed text';
""";
        using var completedReader = completedRow.ExecuteReader();
        completedReader.Read().Should().BeTrue();
        completedReader.GetString(0).Should().Be(nameof(ProviderKind.DeepSeek));
        completedReader.GetString(1).Should().Be("deepseek-v4-flash");
    }

    [Fact]
    public void SqliteCache_discards_old_readable_schema_and_starts_new_context_key_schema()
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
            cache.Count.Should().Be(0);
            cache.RecordCaptured(
                TranslationCacheKey.Create("Options", "zh-Hans", ProviderProfile.DefaultOpenAi(), "prompt-v1"),
                new TranslationCacheContext("Menu", "Canvas/Options", "Text"));
        }

        using var readConnection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path }.ToString());
        readConnection.Open();
        using var schema = readConnection.CreateCommand();
        schema.CommandText = """
SELECT group_concat(name, ',')
FROM (
    SELECT name
    FROM pragma_table_info('translations')
    WHERE pk > 0
    ORDER BY pk
);
""";
        schema.ExecuteScalar().Should().Be("source_text,target_language,scene_name,component_hierarchy");

        using var rows = readConnection.CreateCommand();
        rows.CommandText = "SELECT COUNT(*) FROM translations WHERE source_text = 'Start Game';";
        Convert.ToInt32(rows.ExecuteScalar()).Should().Be(0);

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
        cache.TryGet(key, TranslationCacheContext.Empty, out _).Should().BeFalse();
    }

    [Fact]
    public void SqliteCache_discards_legacy_hash_schema_and_starts_new_context_key_schema()
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
    public void Sqlite_cache_applies_multi_column_filters_and_sorting_after_filtering()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "translation-cache.sqlite");
        using var cache = new SqliteTranslationCache(path);
        var now = DateTimeOffset.Parse("2026-04-26T00:00:00Z");
        cache.Update(SampleRow("Start Game", "Menu", "Canvas/Start", "UnityEngine.UI.Text", "Start translated", now));
        cache.Update(SampleRow("Options", "Menu", "Canvas/Options", "TMPro.TextMeshProUGUI", "Options translated", now.AddMinutes(1)));
        cache.Update(SampleRow("Jump", "Gameplay", "Canvas/Hud/Jump", "UnityEngine.UI.Text", "Jump translated", now.AddMinutes(2)));

        var page = cache.Query(new TranslationCacheQuery(
            Search: null,
            SortColumn: "source_text",
            SortDescending: false,
            Offset: 0,
            Limit: 20,
            ColumnFilters: new[]
            {
                new TranslationCacheColumnFilter("scene_name", new string?[] { "Menu" }),
                new TranslationCacheColumnFilter("component_type", new string?[] { "UnityEngine.UI.Text", "TMPro.TextMeshProUGUI" })
            }));

        page.TotalCount.Should().Be(2);
        page.Items.Select(row => row.SourceText).Should().Equal("Options", "Start Game");
    }

    [Fact]
    public void Sqlite_cache_filter_options_ignore_the_requested_column_filter()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "translation-cache.sqlite");
        using var cache = new SqliteTranslationCache(path);
        var now = DateTimeOffset.Parse("2026-04-26T00:00:00Z");
        cache.Update(SampleRow("Start Game", "Menu", "Canvas/Start", "UnityEngine.UI.Text", "Start translated", now));
        cache.Update(SampleRow("Options", "Menu", "Canvas/Options", "TMPro.TextMeshProUGUI", "Options translated", now.AddMinutes(1)));
        cache.Update(SampleRow("Jump", "Gameplay", "Canvas/Hud/Jump", "UnityEngine.UI.Text", "Jump translated", now.AddMinutes(2)));

        var options = cache.GetFilterOptions(new TranslationCacheFilterOptionsQuery(
            Column: "component_type",
            Search: null,
            ColumnFilters: new[]
            {
                new TranslationCacheColumnFilter("scene_name", new string?[] { "Menu" }),
                new TranslationCacheColumnFilter("component_type", new string?[] { "UnityEngine.UI.Text" })
            },
            OptionSearch: null,
            Limit: 20));

        options.Items.Select(item => item.Value).Should().Equal("TMPro.TextMeshProUGUI", "UnityEngine.UI.Text");
    }

    [Fact]
    public void Sqlite_cache_returns_translation_context_examples_from_current_scene()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "translation-cache.sqlite");
        using var cache = new SqliteTranslationCache(path);
        var now = DateTimeOffset.Parse("2026-04-26T00:00:00Z");
        cache.Update(SampleRow("Current", "Menu", "Canvas/Dialog", "Text", "Current translated", now.AddMinutes(9)));
        cache.Update(SampleRow("Same component", "Menu", "Canvas/Dialog", "Text", "Same component translated", now.AddMinutes(1)));
        cache.Update(SampleRow("Scene fallback", "Menu", "Canvas/Other", "Text", "Scene fallback translated", now.AddMinutes(8)));
        cache.Update(SampleRow("Other scene", "Battle", "Canvas/Dialog", "Text", "Other scene translated", now.AddMinutes(7)));

        var examples = cache.GetTranslationContextExamples(
            "Current",
            "zh-Hans",
            new TranslationCacheContext("Menu", "Canvas/Dialog", "Text"),
            maxExamples: 2,
            maxCharacters: 1000);

        examples.Select(item => item.SourceText).Should().Equal("Same component", "Scene fallback");
    }

    [Fact]
    public void Sqlite_context_examples_keep_component_priority_when_scene_has_many_newer_rows()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "translation-cache.sqlite");
        using var cache = new SqliteTranslationCache(path);
        var now = DateTimeOffset.Parse("2026-04-26T00:00:00Z");
        cache.Update(SampleRow("Same component older", "Menu", "Canvas/Dialog", "Text", "Same component translated", now));
        for (var i = 0; i < 205; i++)
        {
            cache.Update(SampleRow(
                "Other component " + i,
                "Menu",
                "Canvas/Other",
                "Text",
                "Other translated " + i,
                now.AddMinutes(i + 1)));
        }

        var examples = cache.GetTranslationContextExamples(
            "Current",
            "zh-Hans",
            new TranslationCacheContext("Menu", "Canvas/Dialog", "Text"),
            maxExamples: 1,
            maxCharacters: 1000);

        examples.Should().ContainSingle();
        examples[0].SourceText.Should().Be("Same component older");
    }

    [Fact]
    public void Sqlite_cache_column_filters_match_empty_values()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "translation-cache.sqlite");
        using var cache = new SqliteTranslationCache(path);
        var now = DateTimeOffset.Parse("2026-04-26T00:00:00Z");
        cache.Update(SampleRow("No Component", "Menu", "Canvas/Label", null, "No component translated", now));
        cache.Update(SampleRow("Button", "Menu", "Canvas/Button", "UnityEngine.UI.Text", "Button translated", now.AddMinutes(1)));

        var page = cache.Query(new TranslationCacheQuery(
            Search: null,
            SortColumn: "source_text",
            SortDescending: false,
            Offset: 0,
            Limit: 20,
            ColumnFilters: new[]
            {
                new TranslationCacheColumnFilter("component_type", new string?[] { null })
            }));

        page.TotalCount.Should().Be(1);
        page.Items[0].SourceText.Should().Be("No Component");
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
        cache.TryGet(key, new TranslationCacheContext("Menu", "Canvas/Delete", "Text"), out _).Should().BeFalse();
    }

    [Fact]
    public void Memory_cache_update_treats_blank_translation_as_untranslated()
    {
        var cache = new MemoryTranslationCache();
        var key = TranslationCacheKey.Create("Clear Me", "zh-Hans", ProviderProfile.DefaultOpenAi(), "prompt-v1");
        var context = new TranslationCacheContext("Menu", "Canvas/Clear", "Text");

        cache.Update(SampleRow("Clear Me", "Menu", "Canvas/Clear", "Text", "", DateTimeOffset.UtcNow));

        cache.Count.Should().Be(0);
        cache.TryGet(key, context, out _).Should().BeFalse();
        cache.Query(new TranslationCacheQuery("Clear", "source_text", false, 0, 10))
            .Items[0].TranslatedText.Should().BeNull();
    }

    [Fact]
    public void Disk_cache_update_treats_blank_translation_as_untranslated()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "translation-cache.jsonl");
        using var cache = new DiskTranslationCache(path);
        var key = TranslationCacheKey.Create("Clear Me", "zh-Hans", ProviderProfile.DefaultOpenAi(), "prompt-v1");
        var context = new TranslationCacheContext("Menu", "Canvas/Clear", "Text");

        cache.Update(SampleRow("Clear Me", "Menu", "Canvas/Clear", "Text", "", DateTimeOffset.UtcNow));

        cache.Count.Should().Be(0);
        cache.TryGet(key, context, out _).Should().BeFalse();
        cache.Query(new TranslationCacheQuery("Clear", "source_text", false, 0, 10))
            .Items[0].TranslatedText.Should().BeNull();
    }

    [Fact]
    public void Sqlite_cache_update_treats_blank_translation_as_untranslated()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "translation-cache.sqlite");
        using var cache = new SqliteTranslationCache(path);
        var key = TranslationCacheKey.Create("Clear Me", "zh-Hans", ProviderProfile.DefaultOpenAi(), "prompt-v1");
        var context = new TranslationCacheContext("Menu", "Canvas/Clear", "Text");

        cache.Update(SampleRow("Clear Me", "Menu", "Canvas/Clear", "Text", "", DateTimeOffset.UtcNow));

        cache.Count.Should().Be(0);
        cache.TryGet(key, context, out _).Should().BeFalse();
        cache.Query(new TranslationCacheQuery("Clear", "source_text", false, 0, 10))
            .Items[0].TranslatedText.Should().BeNull();
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

    private static TranslationCacheEntry SampleRow(
        string sourceText,
        string sceneName,
        string componentHierarchy,
        string? componentType,
        string? translatedText,
        DateTimeOffset timestamp)
    {
        return new TranslationCacheEntry(
            SourceText: sourceText,
            TargetLanguage: "zh-Hans",
            ProviderKind: "OpenAI",
            ProviderBaseUrl: "https://api.openai.com",
            ProviderEndpoint: "/v1/responses",
            ProviderModel: "gpt-5.5",
            PromptPolicyVersion: "prompt-v1",
            TranslatedText: translatedText,
            SceneName: sceneName,
            ComponentHierarchy: componentHierarchy,
            ComponentType: componentType,
            ReplacementFont: null,
            CreatedUtc: timestamp,
            UpdatedUtc: timestamp);
    }
}

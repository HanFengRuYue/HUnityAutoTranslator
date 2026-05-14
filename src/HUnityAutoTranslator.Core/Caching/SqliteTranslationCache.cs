using Microsoft.Data.Sqlite;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SQLitePCL;
using HUnityAutoTranslator.Core.Configuration;

namespace HUnityAutoTranslator.Core.Caching;

public sealed class SqliteTranslationCache : ITranslationCache, IDisposable
{
    private const int SchemaVersion = 5;
    private const int MaxReadCacheEntries = 4096;
    private static int s_sqliteInitialized;
    private static readonly Dictionary<string, string> SortColumns = new(StringComparer.OrdinalIgnoreCase)
    {
        ["source_text"] = "source_text",
        ["translated_text"] = "translated_text",
        ["target_language"] = "target_language",
        ["provider_kind"] = "provider_kind",
        ["provider_model"] = "provider_model",
        ["scene_name"] = "scene_name",
        ["component_hierarchy"] = "component_hierarchy",
        ["component_type"] = "component_type",
        ["replacement_font"] = "replacement_font",
        ["created_utc"] = "created_utc",
        ["updated_utc"] = "updated_utc"
    };
    private static readonly Dictionary<string, string> FilterColumns = new(StringComparer.OrdinalIgnoreCase)
    {
        ["source_text"] = "source_text",
        ["translated_text"] = "translated_text",
        ["target_language"] = "target_language",
        ["provider_kind"] = "provider_kind",
        ["provider_model"] = "provider_model",
        ["scene_name"] = "scene_name",
        ["component_hierarchy"] = "component_hierarchy",
        ["component_type"] = "component_type",
        ["replacement_font"] = "replacement_font",
        ["created_utc"] = "created_utc",
        ["updated_utc"] = "updated_utc"
    };

    private readonly string _filePath;
    private readonly string _connectionString;
    private readonly object _readCacheGate = new();
    private readonly Dictionary<TranslationLookupReadCacheKey, string?> _translationLookupCache = new();
    private readonly Dictionary<ReplacementFontReadCacheKey, string?> _replacementFontLookupCache = new();
    private readonly Dictionary<CompletedBySourceReadCacheKey, IReadOnlyList<TranslationCacheEntry>> _completedBySourceCache = new();
    private bool _disposed;

    public SqliteTranslationCache(string filePath)
    {
        EnsureSqliteInitialized();
        _filePath = filePath;
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = _filePath,
            Mode = SqliteOpenMode.ReadWriteCreate
        }.ToString();

        Initialize();
    }

    public int Count
    {
        get
        {
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM translations WHERE translated_text IS NOT NULL;";
            return Convert.ToInt32(command.ExecuteScalar());
        }
    }

    public bool TryGet(TranslationCacheKey key, TranslationCacheContext? context, out string translatedText)
    {
        context ??= TranslationCacheContext.Empty;
        var cacheKey = TranslationLookupReadCacheKey.Create(key, context);
        lock (_readCacheGate)
        {
            if (_translationLookupCache.TryGetValue(cacheKey, out var cached))
            {
                translatedText = cached ?? string.Empty;
                return cached != null;
            }
        }

        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
SELECT translated_text
FROM translations
WHERE source_text = $source_text
  AND target_language = $target_language
  AND prompt_policy_version = $prompt_policy_version
  AND scene_name = $scene_name
  AND component_hierarchy = $component_hierarchy
  AND translated_text IS NOT NULL;
""";
        AddLookupParameters(command, key, context);
        command.Parameters.AddWithValue("$prompt_policy_version", key.PromptPolicyVersion);
        var result = command.ExecuteScalar();
        if (result is string value)
        {
            CacheTranslationLookup(cacheKey, value);
            translatedText = value;
            return true;
        }

        CacheTranslationLookup(cacheKey, null);
        translatedText = string.Empty;
        return false;
    }

    public bool TryGetReplacementFont(TranslationCacheKey key, TranslationCacheContext context, out string replacementFont)
    {
        var cacheKey = ReplacementFontReadCacheKey.Create(key, context);
        lock (_readCacheGate)
        {
            if (_replacementFontLookupCache.TryGetValue(cacheKey, out var cached))
            {
                replacementFont = cached ?? string.Empty;
                return cached != null;
            }
        }

        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
SELECT replacement_font
FROM translations
WHERE source_text = $source_text
  AND target_language = $target_language
  AND scene_name = $scene_name
  AND component_hierarchy = $component_hierarchy
  AND component_type IS $component_type
  AND translated_text IS NOT NULL
  AND replacement_font IS NOT NULL
  AND replacement_font <> '';
""";
        AddLookupParameters(command, key, context);
        command.Parameters.AddWithValue("$component_type", ToDbValue(context.ComponentType));
        var result = command.ExecuteScalar();
        if (result is string value)
        {
            CacheReplacementFontLookup(cacheKey, value);
            replacementFont = value;
            return true;
        }

        CacheReplacementFontLookup(cacheKey, null);
        replacementFont = string.Empty;
        return false;
    }

    public IReadOnlyList<TranslationCacheEntry> GetCompletedTranslationsBySource(
        TranslationCacheKey key,
        int limit)
    {
        var take = Math.Min(500, Math.Max(1, limit));
        var cacheKey = CompletedBySourceReadCacheKey.Create(key, take);
        lock (_readCacheGate)
        {
            if (_completedBySourceCache.TryGetValue(cacheKey, out var cached))
            {
                return cached;
            }
        }

        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
SELECT source_text,
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
       updated_utc
FROM translations
WHERE source_text = $source_text
  AND target_language = $target_language
  AND prompt_policy_version = $prompt_policy_version
  AND translated_text IS NOT NULL
  AND trim(translated_text) <> ''
ORDER BY updated_utc DESC
LIMIT $limit;
""";
        command.Parameters.AddWithValue("$source_text", key.SourceText);
        command.Parameters.AddWithValue("$target_language", key.TargetLanguage);
        command.Parameters.AddWithValue("$prompt_policy_version", key.PromptPolicyVersion);
        command.Parameters.AddWithValue("$limit", take);

        var rows = new List<TranslationCacheEntry>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            rows.Add(ReadEntry(reader));
        }

        var result = rows.ToArray();
        CacheCompletedBySource(cacheKey, result);
        return result;
    }

    public void RecordCaptured(TranslationCacheKey key, TranslationCacheContext? context = null)
    {
        var nowUtc = DateTime.UtcNow.ToString("O");
        context ??= TranslationCacheContext.Empty;

        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
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
VALUES (
    $source_text,
    $target_language,
    NULL,
    NULL,
    NULL,
    NULL,
    $prompt_policy_version,
    NULL,
    $scene_name,
    $component_hierarchy,
    $component_type,
    NULL,
    $now_utc,
    $now_utc)
ON CONFLICT(
    source_text,
    target_language,
    scene_name,
    component_hierarchy)
DO UPDATE SET
    provider_kind = CASE WHEN translations.translated_text IS NULL THEN NULL ELSE translations.provider_kind END,
    provider_base_url = CASE WHEN translations.translated_text IS NULL THEN NULL ELSE translations.provider_base_url END,
    provider_endpoint = CASE WHEN translations.translated_text IS NULL THEN NULL ELSE translations.provider_endpoint END,
    provider_model = CASE WHEN translations.translated_text IS NULL THEN NULL ELSE translations.provider_model END,
    prompt_policy_version = CASE WHEN translations.translated_text IS NULL THEN excluded.prompt_policy_version ELSE translations.prompt_policy_version END,
    scene_name = CASE WHEN translations.translated_text IS NULL THEN excluded.scene_name ELSE translations.scene_name END,
    component_hierarchy = CASE WHEN translations.translated_text IS NULL THEN excluded.component_hierarchy ELSE translations.component_hierarchy END,
    component_type = CASE WHEN translations.translated_text IS NULL THEN excluded.component_type ELSE translations.component_type END,
    updated_utc = CASE WHEN translations.translated_text IS NULL THEN excluded.updated_utc ELSE translations.updated_utc END;
""";
        AddPendingParameters(command, key);
        command.Parameters.AddWithValue("$scene_name", TranslationCacheLookupKey.NormalizeContextPart(context.SceneName));
        command.Parameters.AddWithValue("$component_hierarchy", TranslationCacheLookupKey.NormalizeContextPart(context.ComponentHierarchy));
        command.Parameters.AddWithValue("$component_type", ToDbValue(context.ComponentType));
        command.Parameters.AddWithValue("$now_utc", nowUtc);
        command.ExecuteNonQuery();
    }

    public void Set(TranslationCacheKey key, string translatedText, TranslationCacheContext? context = null)
    {
        var nowUtc = DateTime.UtcNow.ToString("O");
        context ??= TranslationCacheContext.Empty;

        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
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
VALUES (
    $source_text,
    $target_language,
    $provider_kind,
    $provider_base_url,
    $provider_endpoint,
    $provider_model,
    $prompt_policy_version,
    $translated_text,
    $scene_name,
    $component_hierarchy,
    $component_type,
    NULL,
    $now_utc,
    $now_utc)
ON CONFLICT(
    source_text,
    target_language,
    scene_name,
    component_hierarchy)
DO UPDATE SET
    provider_kind = excluded.provider_kind,
    provider_base_url = excluded.provider_base_url,
    provider_endpoint = excluded.provider_endpoint,
    provider_model = excluded.provider_model,
    prompt_policy_version = excluded.prompt_policy_version,
    translated_text = excluded.translated_text,
    scene_name = excluded.scene_name,
    component_hierarchy = excluded.component_hierarchy,
    component_type = excluded.component_type,
    updated_utc = excluded.updated_utc;
""";
        AddKeyParameters(command, key);
        command.Parameters.AddWithValue("$translated_text", translatedText);
        command.Parameters.AddWithValue("$scene_name", TranslationCacheLookupKey.NormalizeContextPart(context.SceneName));
        command.Parameters.AddWithValue("$component_hierarchy", TranslationCacheLookupKey.NormalizeContextPart(context.ComponentHierarchy));
        command.Parameters.AddWithValue("$component_type", ToDbValue(context.ComponentType));
        command.Parameters.AddWithValue("$now_utc", nowUtc);
        command.ExecuteNonQuery();
        InvalidateReadCaches();
    }

    public IReadOnlyList<TranslationCacheEntry> GetPendingTranslations(
        string targetLanguage,
        string promptPolicyVersion,
        int limit)
    {
        var take = Math.Min(500, Math.Max(1, limit));
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
SELECT source_text,
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
       updated_utc
FROM translations
WHERE translated_text IS NULL
  AND target_language = $target_language
  AND prompt_policy_version = $prompt_policy_version
ORDER BY created_utc ASC
LIMIT $limit;
""";
        command.Parameters.AddWithValue("$target_language", targetLanguage);
        command.Parameters.AddWithValue("$prompt_policy_version", promptPolicyVersion);
        command.Parameters.AddWithValue("$limit", take);

        var rows = new List<TranslationCacheEntry>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            rows.Add(ReadEntry(reader));
        }

        return rows;
    }

    public IReadOnlyList<TranslationContextExample> GetTranslationContextExamples(
        string currentSourceText,
        string targetLanguage,
        TranslationCacheContext? context,
        int maxExamples,
        int maxCharacters)
    {
        context ??= TranslationCacheContext.Empty;
        var sceneName = TranslationCacheLookupKey.NormalizeContextPart(context.SceneName);
        if (sceneName.Length == 0 || maxExamples <= 0 || maxCharacters <= 0)
        {
            return Array.Empty<TranslationContextExample>();
        }

        using var connection = OpenConnection();
        var rows = new List<TranslationCacheEntry>();
        var componentHierarchy = TranslationCacheLookupKey.NormalizeContextPart(context.ComponentHierarchy);
        if (componentHierarchy.Length > 0)
        {
            AddRows(componentHierarchy);
        }

        AddRows(componentHierarchyFilter: null);

        return TranslationContextSelector.Select(
            rows,
            currentSourceText,
            targetLanguage,
            context,
            Math.Min(20, Math.Max(0, maxExamples)),
            Math.Min(8000, Math.Max(0, maxCharacters)));

        void AddRows(string? componentHierarchyFilter)
        {
            using var command = connection.CreateCommand();
            var componentClause = componentHierarchyFilter == null
                ? string.Empty
                : "  AND component_hierarchy = $component_hierarchy\n";
            command.CommandText = """
SELECT source_text,
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
       updated_utc
FROM translations
WHERE target_language = $target_language
  AND scene_name = $scene_name
""" + componentClause + """
  AND translated_text IS NOT NULL
ORDER BY updated_utc DESC
LIMIT 200;
""";
            command.Parameters.AddWithValue("$target_language", targetLanguage);
            command.Parameters.AddWithValue("$scene_name", sceneName);
            if (componentHierarchyFilter != null)
            {
                command.Parameters.AddWithValue("$component_hierarchy", componentHierarchyFilter);
            }

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                rows.Add(ReadEntry(reader));
            }
        }
    }

    public TranslationCachePage Query(TranslationCacheQuery query)
    {
        var sortColumn = SortColumns.TryGetValue(query.SortColumn, out var column) ? column : "updated_utc";
        var direction = query.SortDescending ? "DESC" : "ASC";
        var limit = Math.Min(500, Math.Max(1, query.Limit));
        var offset = Math.Max(0, query.Offset);
        var hasSearch = !string.IsNullOrWhiteSpace(query.Search);

        using var connection = OpenConnection();
        using var countCommand = connection.CreateCommand();
        var countWhereClause = BuildWhereClause(hasSearch, query.ColumnFilters, countCommand);
        countCommand.CommandText = "SELECT COUNT(*) FROM translations" + countWhereClause + ";";
        if (hasSearch)
        {
            countCommand.Parameters.AddWithValue("$search", "%" + query.Search!.Trim() + "%");
        }

        var total = Convert.ToInt32(countCommand.ExecuteScalar());

        using var command = connection.CreateCommand();
        var whereClause = BuildWhereClause(hasSearch, query.ColumnFilters, command);
        command.CommandText = $"""
SELECT source_text,
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
       updated_utc
FROM translations
{whereClause}
ORDER BY {sortColumn} {direction}
LIMIT $limit OFFSET $offset;
""";
        if (hasSearch)
        {
            command.Parameters.AddWithValue("$search", "%" + query.Search!.Trim() + "%");
        }

        command.Parameters.AddWithValue("$limit", limit);
        command.Parameters.AddWithValue("$offset", offset);

        var rows = new List<TranslationCacheEntry>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            rows.Add(ReadEntry(reader));
        }

        return new TranslationCachePage(total, rows);
    }

    public TranslationCacheFilterOptionPage GetFilterOptions(TranslationCacheFilterOptionsQuery query)
    {
        var column = TranslationCacheColumns.NormalizeColumn(query.Column);
        if (!FilterColumns.TryGetValue(column, out var columnName))
        {
            return new TranslationCacheFilterOptionPage(string.Empty, Array.Empty<TranslationCacheFilterOption>());
        }

        var limit = Math.Min(500, Math.Max(1, query.Limit));
        var hasSearch = !string.IsNullOrWhiteSpace(query.Search);
        var hasOptionSearch = !string.IsNullOrWhiteSpace(query.OptionSearch);
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        var filters = TranslationCacheColumns.NormalizeFilters(query.ColumnFilters, column);
        var whereClause = BuildWhereClause(hasSearch, filters, command);

        var optionSearchClause = hasOptionSearch
            ? $" AND COALESCE({columnName}, '') LIKE $option_search"
            : string.Empty;
        if (whereClause.Length == 0 && hasOptionSearch)
        {
            optionSearchClause = $" WHERE COALESCE({columnName}, '') LIKE $option_search";
        }

        command.CommandText = $"""
SELECT NULLIF({columnName}, '') AS value,
       COUNT(*) AS count
FROM translations
{whereClause}{optionSearchClause}
GROUP BY NULLIF({columnName}, '')
ORDER BY value IS NOT NULL, value COLLATE NOCASE
LIMIT $limit;
""";
        if (hasSearch)
        {
            command.Parameters.AddWithValue("$search", "%" + query.Search!.Trim() + "%");
        }

        if (hasOptionSearch)
        {
            command.Parameters.AddWithValue("$option_search", "%" + query.OptionSearch!.Trim() + "%");
        }

        command.Parameters.AddWithValue("$limit", limit);

        var items = new List<TranslationCacheFilterOption>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            items.Add(new TranslationCacheFilterOption(
                reader.IsDBNull(0) ? null : reader.GetString(0),
                Convert.ToInt32(reader.GetValue(1))));
        }

        return new TranslationCacheFilterOptionPage(column, items);
    }

    public void Update(TranslationCacheEntry entry)
    {
        var now = (entry.UpdatedUtc == default ? DateTimeOffset.UtcNow : entry.UpdatedUtc).ToString("O");
        var created = (entry.CreatedUtc == default ? DateTimeOffset.UtcNow : entry.CreatedUtc).ToString("O");
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
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
VALUES (
    $source_text,
    $target_language,
    $provider_kind,
    $provider_base_url,
    $provider_endpoint,
    $provider_model,
    $prompt_policy_version,
    $translated_text,
    $scene_name,
    $component_hierarchy,
    $component_type,
    $replacement_font,
    $created_utc,
    $updated_utc)
ON CONFLICT(
    source_text,
    target_language,
    scene_name,
    component_hierarchy)
DO UPDATE SET
    provider_kind = excluded.provider_kind,
    provider_base_url = excluded.provider_base_url,
    provider_endpoint = excluded.provider_endpoint,
    provider_model = excluded.provider_model,
    prompt_policy_version = excluded.prompt_policy_version,
    translated_text = excluded.translated_text,
    scene_name = excluded.scene_name,
    component_hierarchy = excluded.component_hierarchy,
    component_type = excluded.component_type,
    replacement_font = excluded.replacement_font,
    updated_utc = excluded.updated_utc;
""";
        AddEntryParameters(command, entry, created, now);
        command.ExecuteNonQuery();
        InvalidateReadCaches();
    }

    public void Delete(TranslationCacheEntry entry)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
DELETE FROM translations
WHERE source_text = $source_text
  AND target_language = $target_language
  AND scene_name = $scene_name
  AND component_hierarchy = $component_hierarchy;
""";
        AddEntryKeyParameters(command, entry);
        command.ExecuteNonQuery();
        InvalidateReadCaches();
    }

    public string Export(string format)
    {
        var rows = Query(new TranslationCacheQuery(null, "updated_utc", true, 0, int.MaxValue)).Items;
        return string.Equals(format, "csv", StringComparison.OrdinalIgnoreCase)
            ? CsvTranslationCacheFormat.Write(rows)
            : JsonConvert.SerializeObject(rows, Formatting.Indented);
    }

    public TranslationCacheImportResult Import(string content, string format)
    {
        try
        {
            var rows = string.Equals(format, "csv", StringComparison.OrdinalIgnoreCase)
                ? CsvTranslationCacheFormat.Read(content)
                : ReadJsonEntries(content);
            var imported = 0;
            foreach (var row in rows)
            {
                Update(row);
                imported++;
            }

            return new TranslationCacheImportResult(imported, Array.Empty<string>());
        }
        catch (Exception ex)
        {
            return new TranslationCacheImportResult(0, new[] { ex.Message });
        }
    }

    public void Dispose()
    {
        _disposed = true;
    }

    private void Initialize()
    {
        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        using var connection = OpenConnection();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "PRAGMA journal_mode=WAL;";
            command.ExecuteNonQuery();
        }

        if (!TableExists(connection, "translations"))
        {
            CreateCurrentSchema(connection);
            return;
        }

        var columns = GetColumnNames(connection, "translations");
        if (IsCurrentSchema(connection, columns))
        {
            EnsureCurrentMetadata(connection);
            return;
        }

        if (CanMigrateProviderMetadataNullability(connection, columns))
        {
            MigrateProviderMetadataNullability(connection);
            return;
        }

        ResetSchema(connection);
    }

    private SqliteConnection OpenConnection()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(SqliteTranslationCache));
        }

        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        return connection;
    }

    private static void CreateCurrentSchema(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = CreateCurrentSchemaSql();
        command.ExecuteNonQuery();
    }

    private static void EnsureCurrentMetadata(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"""
{CurrentIndexesSql()}
PRAGMA user_version={SchemaVersion};
""";
        command.ExecuteNonQuery();
    }

    private static void ResetSchema(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
BEGIN IMMEDIATE;
DROP TABLE translations;
COMMIT;
""";
        command.ExecuteNonQuery();

        CreateCurrentSchema(connection);
    }

    private static void MigrateProviderMetadataNullability(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"""
BEGIN IMMEDIATE;
ALTER TABLE translations RENAME TO translations_old;
CREATE TABLE translations (
    source_text TEXT NOT NULL,
    target_language TEXT NOT NULL,
    provider_kind TEXT NULL,
    provider_base_url TEXT NULL,
    provider_endpoint TEXT NULL,
    provider_model TEXT NULL,
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
SELECT
    source_text,
    target_language,
    CASE WHEN translated_text IS NULL THEN NULL ELSE provider_kind END,
    CASE WHEN translated_text IS NULL THEN NULL ELSE provider_base_url END,
    CASE WHEN translated_text IS NULL THEN NULL ELSE provider_endpoint END,
    CASE WHEN translated_text IS NULL THEN NULL ELSE provider_model END,
    prompt_policy_version,
    translated_text,
    scene_name,
    component_hierarchy,
    component_type,
    replacement_font,
    created_utc,
    updated_utc
FROM translations_old;
DROP TABLE translations_old;
{CurrentIndexesSql()}
PRAGMA user_version={SchemaVersion};
COMMIT;
""";
        command.ExecuteNonQuery();
    }

    private static string CreateCurrentSchemaSql()
    {
        return $"""
CREATE TABLE IF NOT EXISTS translations (
    source_text TEXT NOT NULL,
    target_language TEXT NOT NULL,
    provider_kind TEXT NULL,
    provider_base_url TEXT NULL,
    provider_endpoint TEXT NULL,
    provider_model TEXT NULL,
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
{CurrentIndexesSql()}
PRAGMA user_version={SchemaVersion};
""";
    }

    private static string CurrentIndexesSql()
    {
        return """
CREATE INDEX IF NOT EXISTS ix_translations_updated_utc ON translations (updated_utc);
CREATE INDEX IF NOT EXISTS ix_translations_source_policy_updated ON translations (source_text, target_language, prompt_policy_version, updated_utc);
CREATE INDEX IF NOT EXISTS ix_translations_pending_resume ON translations (target_language, prompt_policy_version, translated_text, created_utc);
CREATE INDEX IF NOT EXISTS ix_translations_context_examples ON translations (target_language, scene_name, component_hierarchy, translated_text, updated_utc);
""";
    }

    private void CacheTranslationLookup(TranslationLookupReadCacheKey key, string? value)
    {
        lock (_readCacheGate)
        {
            TrimCacheIfNeeded(_translationLookupCache);
            _translationLookupCache[key] = value;
        }
    }

    private void CacheReplacementFontLookup(ReplacementFontReadCacheKey key, string? value)
    {
        lock (_readCacheGate)
        {
            TrimCacheIfNeeded(_replacementFontLookupCache);
            _replacementFontLookupCache[key] = value;
        }
    }

    private void CacheCompletedBySource(CompletedBySourceReadCacheKey key, IReadOnlyList<TranslationCacheEntry> value)
    {
        lock (_readCacheGate)
        {
            TrimCacheIfNeeded(_completedBySourceCache);
            _completedBySourceCache[key] = value;
        }
    }

    private void InvalidateReadCaches()
    {
        lock (_readCacheGate)
        {
            _translationLookupCache.Clear();
            _replacementFontLookupCache.Clear();
            _completedBySourceCache.Clear();
        }
    }

    private static void TrimCacheIfNeeded<TKey, TValue>(Dictionary<TKey, TValue> cache)
        where TKey : notnull
    {
        if (cache.Count < MaxReadCacheEntries)
        {
            return;
        }

        cache.Clear();
    }

    private static bool TableExists(SqliteConnection connection, string tableName)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = $name;";
        command.Parameters.AddWithValue("$name", tableName);
        return Convert.ToInt32(command.ExecuteScalar()) > 0;
    }

    private static HashSet<string> GetColumnNames(SqliteConnection connection, string tableName)
    {
        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT name FROM pragma_table_info($table_name);";
        command.Parameters.AddWithValue("$table_name", tableName);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            columns.Add(reader.GetString(0));
        }

        return columns;
    }

    private static bool IsColumnNotNull(SqliteConnection connection, string columnName)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT [notnull] FROM pragma_table_info('translations') WHERE name = $column_name;";
        command.Parameters.AddWithValue("$column_name", columnName);
        return Convert.ToInt32(command.ExecuteScalar()) != 0;
    }

    private static bool IsCurrentSchema(SqliteConnection connection, HashSet<string> columns)
    {
        return HasCurrentContextSchemaColumns(columns)
            && !IsColumnNotNull(connection, "translated_text")
            && !IsColumnNotNull(connection, "provider_kind")
            && !IsColumnNotNull(connection, "provider_base_url")
            && !IsColumnNotNull(connection, "provider_endpoint")
            && !IsColumnNotNull(connection, "provider_model")
            && IsColumnNotNull(connection, "scene_name")
            && IsColumnNotNull(connection, "component_hierarchy")
            && IsCurrentPrimaryKey(connection);
    }

    private static bool CanMigrateProviderMetadataNullability(SqliteConnection connection, HashSet<string> columns)
    {
        return HasCurrentContextSchemaColumns(columns)
            && !IsColumnNotNull(connection, "translated_text")
            && IsColumnNotNull(connection, "scene_name")
            && IsColumnNotNull(connection, "component_hierarchy")
            && IsCurrentPrimaryKey(connection)
            && (IsColumnNotNull(connection, "provider_kind")
                || IsColumnNotNull(connection, "provider_base_url")
                || IsColumnNotNull(connection, "provider_endpoint")
                || IsColumnNotNull(connection, "provider_model"));
    }

    private static bool HasCurrentContextSchemaColumns(HashSet<string> columns)
    {
        return columns.Contains("source_text")
            && columns.Contains("target_language")
            && columns.Contains("provider_kind")
            && columns.Contains("provider_base_url")
            && columns.Contains("provider_endpoint")
            && columns.Contains("provider_model")
            && columns.Contains("prompt_policy_version")
            && columns.Contains("translated_text")
            && columns.Contains("scene_name")
            && columns.Contains("component_hierarchy")
            && columns.Contains("component_type")
            && columns.Contains("replacement_font")
            && columns.Contains("created_utc")
            && columns.Contains("updated_utc");
    }

    private static bool IsCurrentPrimaryKey(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT name FROM pragma_table_info('translations') WHERE pk > 0 ORDER BY pk;";
        using var reader = command.ExecuteReader();
        var columns = new List<string>();
        while (reader.Read())
        {
            columns.Add(reader.GetString(0));
        }

        return columns.SequenceEqual(new[]
        {
            "source_text",
            "target_language",
            "scene_name",
            "component_hierarchy"
        });
    }

    private static void AddKeyParameters(SqliteCommand command, TranslationCacheKey key)
    {
        command.Parameters.AddWithValue("$source_text", key.SourceText);
        command.Parameters.AddWithValue("$target_language", key.TargetLanguage);
        command.Parameters.AddWithValue("$provider_kind", key.ProviderKind.ToString());
        command.Parameters.AddWithValue("$provider_base_url", key.ProviderBaseUrl);
        command.Parameters.AddWithValue("$provider_endpoint", key.ProviderEndpoint);
        command.Parameters.AddWithValue("$provider_model", key.ProviderModel);
        command.Parameters.AddWithValue("$prompt_policy_version", key.PromptPolicyVersion);
    }

    private static void AddPendingParameters(SqliteCommand command, TranslationCacheKey key)
    {
        command.Parameters.AddWithValue("$source_text", key.SourceText);
        command.Parameters.AddWithValue("$target_language", key.TargetLanguage);
        command.Parameters.AddWithValue("$prompt_policy_version", key.PromptPolicyVersion);
    }

    private static void AddLookupParameters(SqliteCommand command, TranslationCacheKey key, TranslationCacheContext context)
    {
        command.Parameters.AddWithValue("$source_text", key.SourceText);
        command.Parameters.AddWithValue("$target_language", key.TargetLanguage);
        command.Parameters.AddWithValue("$scene_name", TranslationCacheLookupKey.NormalizeContextPart(context.SceneName));
        command.Parameters.AddWithValue("$component_hierarchy", TranslationCacheLookupKey.NormalizeContextPart(context.ComponentHierarchy));
    }

    private static void AddEntryParameters(SqliteCommand command, TranslationCacheEntry entry, string createdUtc, string updatedUtc)
    {
        AddEntryKeyParameters(command, entry);
        command.Parameters.AddWithValue("$translated_text", string.IsNullOrWhiteSpace(entry.TranslatedText) ? DBNull.Value : entry.TranslatedText);
        command.Parameters.AddWithValue("$component_type", ToDbValue(entry.ComponentType));
        command.Parameters.AddWithValue("$replacement_font", ToDbValue(entry.ReplacementFont));
        command.Parameters.AddWithValue("$created_utc", createdUtc);
        command.Parameters.AddWithValue("$updated_utc", updatedUtc);
    }

    private static void AddEntryKeyParameters(SqliteCommand command, TranslationCacheEntry entry)
    {
        command.Parameters.AddWithValue("$source_text", entry.SourceText);
        command.Parameters.AddWithValue("$target_language", entry.TargetLanguage);
        command.Parameters.AddWithValue("$provider_kind", ToDbValue(entry.ProviderKind));
        command.Parameters.AddWithValue("$provider_base_url", ToDbValue(entry.ProviderBaseUrl));
        command.Parameters.AddWithValue("$provider_endpoint", ToDbValue(entry.ProviderEndpoint));
        command.Parameters.AddWithValue("$provider_model", ToDbValue(entry.ProviderModel));
        command.Parameters.AddWithValue("$prompt_policy_version", entry.PromptPolicyVersion);
        command.Parameters.AddWithValue("$scene_name", TranslationCacheLookupKey.NormalizeContextPart(entry.SceneName));
        command.Parameters.AddWithValue("$component_hierarchy", TranslationCacheLookupKey.NormalizeContextPart(entry.ComponentHierarchy));
    }

    private static string BuildWhereClause(
        bool hasSearch,
        IReadOnlyList<TranslationCacheColumnFilter>? filters,
        SqliteCommand command)
    {
        var parts = new List<string>();
        if (hasSearch)
        {
            parts.Add("""
(source_text LIKE $search
    OR translated_text LIKE $search
    OR scene_name LIKE $search
    OR component_hierarchy LIKE $search
    OR component_type LIKE $search
    OR replacement_font LIKE $search)
""");
        }

        var filterIndex = 0;
        foreach (var filter in TranslationCacheColumns.NormalizeFilters(filters))
        {
            if (!FilterColumns.TryGetValue(filter.Column, out var columnName))
            {
                continue;
            }

            var valueParts = new List<string>();
            for (var valueIndex = 0; valueIndex < filter.Values.Count; valueIndex++)
            {
                var value = filter.Values[valueIndex];
                if (string.IsNullOrEmpty(value))
                {
                    valueParts.Add($"({columnName} IS NULL OR {columnName} = '')");
                    continue;
                }

                var parameterName = $"$filter_{filterIndex}_{valueIndex}";
                valueParts.Add($"{columnName} = {parameterName}");
                command.Parameters.AddWithValue(parameterName, value);
            }

            if (valueParts.Count > 0)
            {
                parts.Add("(" + string.Join(" OR ", valueParts) + ")");
            }

            filterIndex++;
        }

        return parts.Count == 0 ? string.Empty : " WHERE " + string.Join(" AND ", parts);
    }

    private static TranslationCacheEntry ReadEntry(SqliteDataReader reader)
    {
        return new TranslationCacheEntry(
            SourceText: reader.GetString(0),
            TargetLanguage: reader.GetString(1),
            ProviderKind: reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
            ProviderBaseUrl: reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
            ProviderEndpoint: reader.IsDBNull(4) ? string.Empty : reader.GetString(4),
            ProviderModel: reader.IsDBNull(5) ? string.Empty : reader.GetString(5),
            PromptPolicyVersion: reader.GetString(6),
            TranslatedText: reader.IsDBNull(7) ? null : reader.GetString(7),
            SceneName: reader.IsDBNull(8) ? string.Empty : reader.GetString(8),
            ComponentHierarchy: reader.IsDBNull(9) ? string.Empty : reader.GetString(9),
            ComponentType: reader.IsDBNull(10) ? null : reader.GetString(10),
            ReplacementFont: reader.IsDBNull(11) ? null : reader.GetString(11),
            CreatedUtc: ParseDate(reader.GetString(12)),
            UpdatedUtc: ParseDate(reader.GetString(13)));
    }

    private static IReadOnlyList<TranslationCacheEntry> ReadJsonEntries(string content)
    {
        var array = JArray.Parse(content);
        var rows = new List<TranslationCacheEntry>();
        foreach (var item in array.OfType<JObject>())
        {
            var now = DateTimeOffset.UtcNow;
            var createdUtc = ParseDate(Optional(item, "created_utc", "CreatedUtc"));
            var updatedUtc = ParseDate(Optional(item, "updated_utc", "UpdatedUtc"));
            rows.Add(new TranslationCacheEntry(
                Required(item, "source_text", "SourceText"),
                Required(item, "target_language", "TargetLanguage"),
                Optional(item, "provider_kind", "ProviderKind") ?? string.Empty,
                Optional(item, "provider_base_url", "ProviderBaseUrl") ?? string.Empty,
                Optional(item, "provider_endpoint", "ProviderEndpoint") ?? string.Empty,
                Optional(item, "provider_model", "ProviderModel") ?? string.Empty,
                Required(item, "prompt_policy_version", "PromptPolicyVersion"),
                Optional(item, "translated_text", "TranslatedText"),
                TranslationCacheLookupKey.NormalizeContextPart(Optional(item, "scene_name", "SceneName")),
                TranslationCacheLookupKey.NormalizeContextPart(Optional(item, "component_hierarchy", "ComponentHierarchy")),
                Optional(item, "component_type", "ComponentType"),
                Optional(item, "replacement_font", "ReplacementFont"),
                createdUtc == default ? now : createdUtc,
                updatedUtc == default ? now : updatedUtc));
        }

        return rows;
    }

    private static string Required(JObject item, string name, string alternateName)
    {
        var value = ReadString(item, name, alternateName);
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new FormatException($"Missing required translation cache field: {name}");
        }

        return value;
    }

    private static string? Optional(JObject item, string name, string alternateName)
    {
        var value = ReadString(item, name, alternateName);
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static string? ReadString(JObject item, string name, string alternateName)
    {
        return item.Value<string>(name) ?? item.Value<string>(alternateName);
    }

    private static DateTimeOffset ParseDate(string? value)
    {
        return DateTimeOffset.TryParse(value, out var parsed) ? parsed : default;
    }

    private static object ToDbValue(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? DBNull.Value : value.Trim();
    }

    private sealed record TranslationLookupReadCacheKey(
        string SourceText,
        string TargetLanguage,
        string PromptPolicyVersion,
        string SceneName,
        string ComponentHierarchy)
    {
        public static TranslationLookupReadCacheKey Create(TranslationCacheKey key, TranslationCacheContext context)
        {
            return new TranslationLookupReadCacheKey(
                key.SourceText,
                key.TargetLanguage,
                key.PromptPolicyVersion,
                TranslationCacheLookupKey.NormalizeContextPart(context.SceneName),
                TranslationCacheLookupKey.NormalizeContextPart(context.ComponentHierarchy));
        }
    }

    private sealed record ReplacementFontReadCacheKey(
        string SourceText,
        string TargetLanguage,
        string SceneName,
        string ComponentHierarchy,
        string ComponentType)
    {
        public static ReplacementFontReadCacheKey Create(TranslationCacheKey key, TranslationCacheContext context)
        {
            return new ReplacementFontReadCacheKey(
                key.SourceText,
                key.TargetLanguage,
                TranslationCacheLookupKey.NormalizeContextPart(context.SceneName),
                TranslationCacheLookupKey.NormalizeContextPart(context.ComponentHierarchy),
                context.ComponentType?.Trim() ?? string.Empty);
        }
    }

    private sealed record CompletedBySourceReadCacheKey(
        string SourceText,
        string TargetLanguage,
        string PromptPolicyVersion,
        int Limit)
    {
        public static CompletedBySourceReadCacheKey Create(TranslationCacheKey key, int limit)
        {
            return new CompletedBySourceReadCacheKey(
                key.SourceText,
                key.TargetLanguage,
                key.PromptPolicyVersion,
                limit);
        }
    }

    private static void EnsureSqliteInitialized()
    {
        if (Interlocked.Exchange(ref s_sqliteInitialized, 1) == 0)
        {
            // 直接挂 e_sqlite3 静态提供者，绕过 SQLitePCLRaw.provider.dynamic_cdecl 的 MakeDynamic 路径。
            // 后者内部调用 RuntimeInformation.IsOSPlatform，而部分 Unity Mono（如 Unity 6 在 MSAG 里的版本）
            // 不带 System.Runtime.InteropServices.RuntimeInformation 4.0.2.0，会 FileNotFoundException 让插件起不来。
            SQLitePCL.raw.SetProvider(new SQLitePCL.SQLite3Provider_e_sqlite3());
        }
    }
}

using Microsoft.Data.Sqlite;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SQLitePCL;
using HUnityAutoTranslator.Core.Configuration;

namespace HUnityAutoTranslator.Core.Caching;

public sealed class SqliteTranslationCache : ITranslationCache, IDisposable
{
    private const int SchemaVersion = 3;
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

    private readonly string _filePath;
    private readonly string _connectionString;
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

    public bool TryGet(TranslationCacheKey key, out string translatedText)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
SELECT translated_text
FROM translations
WHERE source_text = $source_text
  AND target_language = $target_language
  AND provider_kind = $provider_kind
  AND provider_base_url = $provider_base_url
  AND provider_endpoint = $provider_endpoint
  AND provider_model = $provider_model
  AND prompt_policy_version = $prompt_policy_version
  AND translated_text IS NOT NULL;
""";
        AddKeyParameters(command, key);
        var result = command.ExecuteScalar();
        if (result is string value)
        {
            translatedText = value;
            return true;
        }

        translatedText = string.Empty;
        return false;
    }

    public bool TryGetReplacementFont(TranslationCacheKey key, TranslationCacheContext context, out string replacementFont)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
SELECT replacement_font
FROM translations
WHERE source_text = $source_text
  AND target_language = $target_language
  AND provider_kind = $provider_kind
  AND provider_base_url = $provider_base_url
  AND provider_endpoint = $provider_endpoint
  AND provider_model = $provider_model
  AND prompt_policy_version = $prompt_policy_version
  AND scene_name IS $scene_name
  AND component_hierarchy IS $component_hierarchy
  AND component_type IS $component_type
  AND replacement_font IS NOT NULL
  AND replacement_font <> '';
""";
        AddKeyParameters(command, key);
        command.Parameters.AddWithValue("$scene_name", ToDbValue(context.SceneName));
        command.Parameters.AddWithValue("$component_hierarchy", ToDbValue(context.ComponentHierarchy));
        command.Parameters.AddWithValue("$component_type", ToDbValue(context.ComponentType));
        var result = command.ExecuteScalar();
        if (result is string value)
        {
            replacementFont = value;
            return true;
        }

        replacementFont = string.Empty;
        return false;
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
    $provider_kind,
    $provider_base_url,
    $provider_endpoint,
    $provider_model,
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
    provider_kind,
    provider_base_url,
    provider_endpoint,
    provider_model,
    prompt_policy_version)
DO UPDATE SET
    scene_name = CASE WHEN translations.translated_text IS NULL THEN excluded.scene_name ELSE translations.scene_name END,
    component_hierarchy = CASE WHEN translations.translated_text IS NULL THEN excluded.component_hierarchy ELSE translations.component_hierarchy END,
    component_type = CASE WHEN translations.translated_text IS NULL THEN excluded.component_type ELSE translations.component_type END,
    updated_utc = CASE WHEN translations.translated_text IS NULL THEN excluded.updated_utc ELSE translations.updated_utc END;
""";
        AddKeyParameters(command, key);
        command.Parameters.AddWithValue("$scene_name", ToDbValue(context.SceneName));
        command.Parameters.AddWithValue("$component_hierarchy", ToDbValue(context.ComponentHierarchy));
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
    provider_kind,
    provider_base_url,
    provider_endpoint,
    provider_model,
    prompt_policy_version)
DO UPDATE SET
    translated_text = excluded.translated_text,
    scene_name = excluded.scene_name,
    component_hierarchy = excluded.component_hierarchy,
    component_type = excluded.component_type,
    updated_utc = excluded.updated_utc;
""";
        AddKeyParameters(command, key);
        command.Parameters.AddWithValue("$translated_text", translatedText);
        command.Parameters.AddWithValue("$scene_name", ToDbValue(context.SceneName));
        command.Parameters.AddWithValue("$component_hierarchy", ToDbValue(context.ComponentHierarchy));
        command.Parameters.AddWithValue("$component_type", ToDbValue(context.ComponentType));
        command.Parameters.AddWithValue("$now_utc", nowUtc);
        command.ExecuteNonQuery();
    }

    public IReadOnlyList<TranslationCacheEntry> GetPendingTranslations(
        string targetLanguage,
        ProviderProfile provider,
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
  AND provider_kind = $provider_kind
  AND provider_base_url = $provider_base_url
  AND provider_endpoint = $provider_endpoint
  AND provider_model = $provider_model
  AND prompt_policy_version = $prompt_policy_version
ORDER BY created_utc ASC
LIMIT $limit;
""";
        command.Parameters.AddWithValue("$target_language", targetLanguage);
        command.Parameters.AddWithValue("$provider_kind", provider.Kind.ToString());
        command.Parameters.AddWithValue("$provider_base_url", provider.BaseUrl);
        command.Parameters.AddWithValue("$provider_endpoint", provider.Endpoint);
        command.Parameters.AddWithValue("$provider_model", provider.Model);
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

    public TranslationCachePage Query(TranslationCacheQuery query)
    {
        var sortColumn = SortColumns.TryGetValue(query.SortColumn, out var column) ? column : "updated_utc";
        var direction = query.SortDescending ? "DESC" : "ASC";
        var limit = Math.Min(500, Math.Max(1, query.Limit));
        var offset = Math.Max(0, query.Offset);
        var hasSearch = !string.IsNullOrWhiteSpace(query.Search);

        using var connection = OpenConnection();
        using var countCommand = connection.CreateCommand();
        countCommand.CommandText = "SELECT COUNT(*) FROM translations" + WhereClause(hasSearch) + ";";
        if (hasSearch)
        {
            countCommand.Parameters.AddWithValue("$search", "%" + query.Search!.Trim() + "%");
        }

        var total = Convert.ToInt32(countCommand.ExecuteScalar());

        using var command = connection.CreateCommand();
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
{WhereClause(hasSearch)}
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
    provider_kind,
    provider_base_url,
    provider_endpoint,
    provider_model,
    prompt_policy_version)
DO UPDATE SET
    translated_text = excluded.translated_text,
    scene_name = excluded.scene_name,
    component_hierarchy = excluded.component_hierarchy,
    component_type = excluded.component_type,
    replacement_font = excluded.replacement_font,
    updated_utc = excluded.updated_utc;
""";
        AddEntryParameters(command, entry, created, now);
        command.ExecuteNonQuery();
    }

    public void Delete(TranslationCacheEntry entry)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
DELETE FROM translations
WHERE source_text = $source_text
  AND target_language = $target_language
  AND provider_kind = $provider_kind
  AND provider_base_url = $provider_base_url
  AND provider_endpoint = $provider_endpoint
  AND provider_model = $provider_model
  AND prompt_policy_version = $prompt_policy_version;
""";
        AddEntryKeyParameters(command, entry);
        command.ExecuteNonQuery();
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
        if (IsCurrentSchema(columns))
        {
            if (IsColumnNotNull(connection, "translated_text"))
            {
                MigrateReadableSchemaToNullable(connection);
                return;
            }

            if (!columns.Contains("replacement_font"))
            {
                AddReplacementFontColumn(connection);
            }

            EnsureCurrentMetadata(connection);
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
CREATE INDEX IF NOT EXISTS ix_translations_updated_utc ON translations (updated_utc);
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

    private static void MigrateReadableSchemaToNullable(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"""
BEGIN IMMEDIATE;
DROP TABLE IF EXISTS translations_before_nullable;
ALTER TABLE translations RENAME TO translations_before_nullable;
{CreateCurrentSchemaSql()}
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
       NULL,
       created_utc,
       updated_utc
FROM translations_before_nullable;
DROP TABLE translations_before_nullable;
CREATE INDEX IF NOT EXISTS ix_translations_updated_utc ON translations (updated_utc);
PRAGMA user_version={SchemaVersion};
COMMIT;
""";
        command.ExecuteNonQuery();
    }

    private static void AddReplacementFontColumn(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "ALTER TABLE translations ADD COLUMN replacement_font TEXT NULL;";
        command.ExecuteNonQuery();
    }

    private static string CreateCurrentSchemaSql()
    {
        return $"""
CREATE TABLE IF NOT EXISTS translations (
    source_text TEXT NOT NULL,
    target_language TEXT NOT NULL,
    provider_kind TEXT NOT NULL,
    provider_base_url TEXT NOT NULL,
    provider_endpoint TEXT NOT NULL,
    provider_model TEXT NOT NULL,
    prompt_policy_version TEXT NOT NULL,
    translated_text TEXT NULL,
    scene_name TEXT NULL,
    component_hierarchy TEXT NULL,
    component_type TEXT NULL,
    replacement_font TEXT NULL,
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
CREATE INDEX IF NOT EXISTS ix_translations_updated_utc ON translations (updated_utc);
PRAGMA user_version={SchemaVersion};
""";
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

    private static bool IsCurrentSchema(HashSet<string> columns)
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
            && columns.Contains("created_utc")
            && columns.Contains("updated_utc");
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

    private static void AddEntryParameters(SqliteCommand command, TranslationCacheEntry entry, string createdUtc, string updatedUtc)
    {
        AddEntryKeyParameters(command, entry);
        command.Parameters.AddWithValue("$translated_text", entry.TranslatedText == null ? DBNull.Value : entry.TranslatedText);
        command.Parameters.AddWithValue("$scene_name", ToDbValue(entry.SceneName));
        command.Parameters.AddWithValue("$component_hierarchy", ToDbValue(entry.ComponentHierarchy));
        command.Parameters.AddWithValue("$component_type", ToDbValue(entry.ComponentType));
        command.Parameters.AddWithValue("$replacement_font", ToDbValue(entry.ReplacementFont));
        command.Parameters.AddWithValue("$created_utc", createdUtc);
        command.Parameters.AddWithValue("$updated_utc", updatedUtc);
    }

    private static void AddEntryKeyParameters(SqliteCommand command, TranslationCacheEntry entry)
    {
        command.Parameters.AddWithValue("$source_text", entry.SourceText);
        command.Parameters.AddWithValue("$target_language", entry.TargetLanguage);
        command.Parameters.AddWithValue("$provider_kind", entry.ProviderKind);
        command.Parameters.AddWithValue("$provider_base_url", entry.ProviderBaseUrl);
        command.Parameters.AddWithValue("$provider_endpoint", entry.ProviderEndpoint);
        command.Parameters.AddWithValue("$provider_model", entry.ProviderModel);
        command.Parameters.AddWithValue("$prompt_policy_version", entry.PromptPolicyVersion);
    }

    private static string WhereClause(bool hasSearch)
    {
        return hasSearch
            ? """
 WHERE source_text LIKE $search
    OR translated_text LIKE $search
    OR scene_name LIKE $search
    OR component_hierarchy LIKE $search
    OR component_type LIKE $search
    OR replacement_font LIKE $search
"""
            : string.Empty;
    }

    private static TranslationCacheEntry ReadEntry(SqliteDataReader reader)
    {
        return new TranslationCacheEntry(
            SourceText: reader.GetString(0),
            TargetLanguage: reader.GetString(1),
            ProviderKind: reader.GetString(2),
            ProviderBaseUrl: reader.GetString(3),
            ProviderEndpoint: reader.GetString(4),
            ProviderModel: reader.GetString(5),
            PromptPolicyVersion: reader.GetString(6),
            TranslatedText: reader.IsDBNull(7) ? null : reader.GetString(7),
            SceneName: reader.IsDBNull(8) ? null : reader.GetString(8),
            ComponentHierarchy: reader.IsDBNull(9) ? null : reader.GetString(9),
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
                Required(item, "provider_kind", "ProviderKind"),
                Required(item, "provider_base_url", "ProviderBaseUrl"),
                Required(item, "provider_endpoint", "ProviderEndpoint"),
                Required(item, "provider_model", "ProviderModel"),
                Required(item, "prompt_policy_version", "PromptPolicyVersion"),
                Optional(item, "translated_text", "TranslatedText"),
                Optional(item, "scene_name", "SceneName"),
                Optional(item, "component_hierarchy", "ComponentHierarchy"),
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

    private static void EnsureSqliteInitialized()
    {
        if (Interlocked.Exchange(ref s_sqliteInitialized, 1) == 0)
        {
            Batteries_V2.Init();
        }
    }
}

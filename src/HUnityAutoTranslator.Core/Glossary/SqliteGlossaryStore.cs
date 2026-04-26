using Microsoft.Data.Sqlite;
using SQLitePCL;

namespace HUnityAutoTranslator.Core.Glossary;

public sealed class SqliteGlossaryStore : IGlossaryStore, IDisposable
{
    private const int SchemaVersion = 1;
    private static int s_sqliteInitialized;
    private static readonly Dictionary<string, string> SortColumns = new(StringComparer.OrdinalIgnoreCase)
    {
        ["source_term"] = "source_term",
        ["target_term"] = "target_term",
        ["target_language"] = "target_language",
        ["source"] = "source_kind",
        ["usage_count"] = "usage_count",
        ["created_utc"] = "created_utc",
        ["updated_utc"] = "updated_utc"
    };

    private readonly string _filePath;
    private readonly string _connectionString;
    private bool _disposed;

    public SqliteGlossaryStore(string filePath)
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
            command.CommandText = "SELECT COUNT(*) FROM glossary_terms;";
            return Convert.ToInt32(command.ExecuteScalar());
        }
    }

    public GlossaryTermPage Query(GlossaryQuery query)
    {
        var sortColumn = SortColumns.TryGetValue(query.SortColumn, out var column) ? column : "updated_utc";
        var direction = query.SortDescending ? "DESC" : "ASC";
        var limit = Math.Min(500, Math.Max(1, query.Limit));
        var offset = Math.Max(0, query.Offset);
        var hasSearch = !string.IsNullOrWhiteSpace(query.Search);

        using var connection = OpenConnection();
        using var countCommand = connection.CreateCommand();
        countCommand.CommandText = "SELECT COUNT(*) FROM glossary_terms" + BuildWhereClause(hasSearch) + ";";
        if (hasSearch)
        {
            countCommand.Parameters.AddWithValue("$search", "%" + query.Search!.Trim() + "%");
        }

        var total = Convert.ToInt32(countCommand.ExecuteScalar());

        using var command = connection.CreateCommand();
        command.CommandText = $"""
SELECT source_term,
       target_term,
       target_language,
       normalized_source_term,
       note,
       enabled,
       source_kind,
       usage_count,
       created_utc,
       updated_utc
FROM glossary_terms
{BuildWhereClause(hasSearch)}
ORDER BY {sortColumn} {direction}
LIMIT $limit OFFSET $offset;
""";
        if (hasSearch)
        {
            command.Parameters.AddWithValue("$search", "%" + query.Search!.Trim() + "%");
        }

        command.Parameters.AddWithValue("$limit", limit);
        command.Parameters.AddWithValue("$offset", offset);

        var rows = new List<GlossaryTerm>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            rows.Add(ReadTerm(reader));
        }

        return new GlossaryTermPage(total, rows);
    }

    public IReadOnlyList<GlossaryTerm> GetEnabledTerms(string targetLanguage)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
SELECT source_term,
       target_term,
       target_language,
       normalized_source_term,
       note,
       enabled,
       source_kind,
       usage_count,
       created_utc,
       updated_utc
FROM glossary_terms
WHERE enabled = 1
  AND target_language = $target_language
ORDER BY LENGTH(source_term) DESC, source_term COLLATE NOCASE;
""";
        command.Parameters.AddWithValue("$target_language", targetLanguage);

        var rows = new List<GlossaryTerm>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            rows.Add(ReadTerm(reader));
        }

        return rows;
    }

    public GlossaryTerm UpsertManual(GlossaryTerm term)
    {
        var now = DateTimeOffset.UtcNow;
        var normalized = term.NormalizeForStorage() with
        {
            Source = GlossaryTermSource.Manual,
            UpdatedUtc = now
        };
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
INSERT INTO glossary_terms (
    source_term,
    target_term,
    target_language,
    normalized_source_term,
    note,
    enabled,
    source_kind,
    usage_count,
    created_utc,
    updated_utc)
VALUES (
    $source_term,
    $target_term,
    $target_language,
    $normalized_source_term,
    $note,
    $enabled,
    $source_kind,
    $usage_count,
    $created_utc,
    $updated_utc)
ON CONFLICT(target_language, normalized_source_term)
DO UPDATE SET
    source_term = excluded.source_term,
    target_term = excluded.target_term,
    note = excluded.note,
    enabled = excluded.enabled,
    source_kind = excluded.source_kind,
    usage_count = CASE
        WHEN glossary_terms.usage_count > excluded.usage_count THEN glossary_terms.usage_count
        ELSE excluded.usage_count
    END,
    updated_utc = excluded.updated_utc;
""";
        AddTermParameters(command, normalized, normalized.CreatedUtc == default ? now : normalized.CreatedUtc, now);
        command.ExecuteNonQuery();
        return normalized;
    }

    public GlossaryUpsertResult UpsertAutomatic(GlossaryTerm term)
    {
        var normalized = term.NormalizeForStorage() with
        {
            Source = GlossaryTermSource.Automatic,
            Enabled = true,
            UsageCount = Math.Max(1, term.UsageCount)
        };
        if (!GlossaryTerm.IsValid(normalized))
        {
            return GlossaryUpsertResult.SkippedInvalid;
        }

        using var connection = OpenConnection();
        using var select = connection.CreateCommand();
        select.CommandText = """
SELECT source_term,
       target_term,
       target_language,
       normalized_source_term,
       note,
       enabled,
       source_kind,
       usage_count,
       created_utc,
       updated_utc
FROM glossary_terms
WHERE target_language = $target_language
  AND normalized_source_term = $normalized_source_term;
""";
        select.Parameters.AddWithValue("$target_language", normalized.TargetLanguage);
        select.Parameters.AddWithValue("$normalized_source_term", normalized.NormalizedSourceTerm);

        GlossaryTerm? existing = null;
        using (var reader = select.ExecuteReader())
        {
            if (reader.Read())
            {
                existing = ReadTerm(reader);
            }
        }

        if (existing != null && existing.Source == GlossaryTermSource.Manual)
        {
            return GlossaryUpsertResult.SkippedManualConflict;
        }

        if (existing != null && !string.Equals(existing.TargetTerm, normalized.TargetTerm, StringComparison.Ordinal))
        {
            return GlossaryUpsertResult.SkippedAutomaticConflict;
        }

        var now = DateTimeOffset.UtcNow;
        using var upsert = connection.CreateCommand();
        upsert.CommandText = """
INSERT INTO glossary_terms (
    source_term,
    target_term,
    target_language,
    normalized_source_term,
    note,
    enabled,
    source_kind,
    usage_count,
    created_utc,
    updated_utc)
VALUES (
    $source_term,
    $target_term,
    $target_language,
    $normalized_source_term,
    $note,
    1,
    $source_kind,
    $usage_count,
    $created_utc,
    $updated_utc)
ON CONFLICT(target_language, normalized_source_term)
DO UPDATE SET
    source_term = excluded.source_term,
    target_term = excluded.target_term,
    note = COALESCE(excluded.note, glossary_terms.note),
    enabled = 1,
    source_kind = excluded.source_kind,
    usage_count = glossary_terms.usage_count + 1,
    updated_utc = excluded.updated_utc;
""";
        AddTermParameters(upsert, normalized, existing?.CreatedUtc ?? now, now);
        upsert.ExecuteNonQuery();
        return existing == null ? GlossaryUpsertResult.Created : GlossaryUpsertResult.Updated;
    }

    public void Delete(GlossaryTerm term)
    {
        var normalized = term.NormalizeForStorage();
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
DELETE FROM glossary_terms
WHERE target_language = $target_language
  AND normalized_source_term = $normalized_source_term;
""";
        command.Parameters.AddWithValue("$target_language", normalized.TargetLanguage);
        command.Parameters.AddWithValue("$normalized_source_term", normalized.NormalizedSourceTerm);
        command.ExecuteNonQuery();
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
        using var command = connection.CreateCommand();
        command.CommandText = $"""
PRAGMA journal_mode=WAL;
CREATE TABLE IF NOT EXISTS glossary_terms (
    source_term TEXT NOT NULL,
    target_term TEXT NOT NULL,
    target_language TEXT NOT NULL,
    normalized_source_term TEXT NOT NULL,
    note TEXT NULL,
    enabled INTEGER NOT NULL,
    source_kind TEXT NOT NULL,
    usage_count INTEGER NOT NULL,
    created_utc TEXT NOT NULL,
    updated_utc TEXT NOT NULL,
    PRIMARY KEY (target_language, normalized_source_term)
);
CREATE INDEX IF NOT EXISTS ix_glossary_terms_updated_utc ON glossary_terms (updated_utc);
PRAGMA user_version={SchemaVersion};
""";
        command.ExecuteNonQuery();
    }

    private SqliteConnection OpenConnection()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(SqliteGlossaryStore));
        }

        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        return connection;
    }

    private static string BuildWhereClause(bool hasSearch)
    {
        return hasSearch
            ? """
 WHERE source_term LIKE $search
    OR target_term LIKE $search
    OR target_language LIKE $search
    OR note LIKE $search
    OR source_kind LIKE $search
"""
            : string.Empty;
    }

    private static void AddTermParameters(SqliteCommand command, GlossaryTerm term, DateTimeOffset createdUtc, DateTimeOffset updatedUtc)
    {
        command.Parameters.AddWithValue("$source_term", term.SourceTerm);
        command.Parameters.AddWithValue("$target_term", term.TargetTerm);
        command.Parameters.AddWithValue("$target_language", term.TargetLanguage);
        command.Parameters.AddWithValue("$normalized_source_term", term.NormalizedSourceTerm);
        command.Parameters.AddWithValue("$note", string.IsNullOrWhiteSpace(term.Note) ? DBNull.Value : term.Note);
        command.Parameters.AddWithValue("$enabled", term.Enabled ? 1 : 0);
        command.Parameters.AddWithValue("$source_kind", term.Source.ToString());
        command.Parameters.AddWithValue("$usage_count", Math.Max(0, term.UsageCount));
        command.Parameters.AddWithValue("$created_utc", createdUtc.ToString("O"));
        command.Parameters.AddWithValue("$updated_utc", updatedUtc.ToString("O"));
    }

    private static GlossaryTerm ReadTerm(SqliteDataReader reader)
    {
        return new GlossaryTerm(
            SourceTerm: reader.GetString(0),
            TargetTerm: reader.GetString(1),
            TargetLanguage: reader.GetString(2),
            NormalizedSourceTerm: reader.GetString(3),
            Note: reader.IsDBNull(4) ? null : reader.GetString(4),
            Enabled: Convert.ToInt32(reader.GetValue(5)) != 0,
            Source: Enum.TryParse<GlossaryTermSource>(reader.GetString(6), out var source) ? source : GlossaryTermSource.Manual,
            UsageCount: Convert.ToInt32(reader.GetValue(7)),
            CreatedUtc: ParseDate(reader.GetString(8)),
            UpdatedUtc: ParseDate(reader.GetString(9)));
    }

    private static DateTimeOffset ParseDate(string? value)
    {
        return DateTimeOffset.TryParse(value, out var parsed) ? parsed : default;
    }

    private static void EnsureSqliteInitialized()
    {
        if (Interlocked.Exchange(ref s_sqliteInitialized, 1) == 0)
        {
            Batteries_V2.Init();
        }
    }
}

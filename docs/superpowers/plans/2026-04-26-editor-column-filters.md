# Editor Column Filters Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 给“文本编辑”表格增加类似 Excel 的列头筛选，并支持场景、组件等多列叠加筛选。

**Architecture:** 列筛选语义放在 Core 缓存查询层，`LocalHttpServer` 只负责解析查询参数和暴露筛选值接口，`ControlPanelHtml.cs` 负责列头菜单和筛选状态。SQLite、Memory、Disk 缓存共用同一组筛选模型，前端所有筛选都走服务端分页，避免只筛当前页。

**Tech Stack:** C# `netstandard2.1`、`Microsoft.Data.Sqlite`、Newtonsoft.Json、内嵌 HTML/CSS/JavaScript、xUnit + FluentAssertions。

---

## File Structure

- Modify: `src/HUnityAutoTranslator.Core/Caching/TranslationCacheQuery.cs`
  - 给现有查询对象增加可选列筛选参数，保持旧构造调用兼容。
- Create: `src/HUnityAutoTranslator.Core/Caching/TranslationCacheColumnFilter.cs`
  - 定义列筛选、筛选值选项、筛选值查询模型和可筛选列白名单。
- Modify: `src/HUnityAutoTranslator.Core/Caching/ITranslationCache.cs`
  - 增加 `GetFilterOptions`，供 HTTP 层获取当前范围内的列值。
- Modify: `src/HUnityAutoTranslator.Core/Caching/MemoryTranslationCache.cs`
  - 对内存缓存实现列筛选和筛选值聚合。
- Modify: `src/HUnityAutoTranslator.Core/Caching/DiskTranslationCache.cs`
  - 对磁盘缓存实现同样的列筛选和筛选值聚合。
- Modify: `src/HUnityAutoTranslator.Core/Caching/SqliteTranslationCache.cs`
  - 用白名单列和参数化 SQL 实现列筛选、筛选后分页和筛选值聚合。
- Modify: `src/HUnityAutoTranslator.Plugin/Web/LocalHttpServer.cs`
  - 解析 `filter.<column>` 查询参数，新增 `/api/translations/filter-options`。
- Modify: `src/HUnityAutoTranslator.Plugin/Web/ControlPanelHtml.cs`
  - 在每个列标题加入筛选按钮和筛选菜单，维护 `tableState.filters`。
- Modify: `tests/HUnityAutoTranslator.Core.Tests/Caching/TranslationCacheTests.cs`
  - 覆盖列筛选语义、空值、多选、筛选值选项和 SQLite 行为。
- Modify: `tests/HUnityAutoTranslator.Core.Tests/Control/ControlPanelHtmlSourceTests.cs`
  - 用源码断言覆盖前端入口和 HTTP 参数序列化关键点。

Before implementing, run `git status --short` and keep existing unrelated modified files intact. Stage only files changed for this feature at each commit.

---

### Task 1: Core Query Models And Memory/Disk Filtering

**Files:**
- Modify: `src/HUnityAutoTranslator.Core/Caching/TranslationCacheQuery.cs`
- Create: `src/HUnityAutoTranslator.Core/Caching/TranslationCacheColumnFilter.cs`
- Modify: `src/HUnityAutoTranslator.Core/Caching/ITranslationCache.cs`
- Modify: `src/HUnityAutoTranslator.Core/Caching/MemoryTranslationCache.cs`
- Modify: `src/HUnityAutoTranslator.Core/Caching/DiskTranslationCache.cs`
- Test: `tests/HUnityAutoTranslator.Core.Tests/Caching/TranslationCacheTests.cs`

- [ ] **Step 1: Write failing tests for in-memory filter semantics**

Add these test methods and helper near the existing cache query tests in `tests/HUnityAutoTranslator.Core.Tests/Caching/TranslationCacheTests.cs`:

```csharp
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

private static TranslationCacheEntry SampleRow(
    string sourceText,
    string sceneName,
    string componentHierarchy,
    string? componentType,
    string translatedText,
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
```

- [ ] **Step 2: Run tests to verify the new API is missing**

Run:

```powershell
dotnet test tests/HUnityAutoTranslator.Core.Tests/HUnityAutoTranslator.Core.Tests.csproj --filter "FullyQualifiedName~TranslationCacheTests"
```

Expected: FAIL with compile errors for `TranslationCacheColumnFilter`, `TranslationCacheFilterOptionsQuery`, `ColumnFilters`, or `GetFilterOptions`.

- [ ] **Step 3: Add filter query models**

Replace `src/HUnityAutoTranslator.Core/Caching/TranslationCacheQuery.cs` with:

```csharp
namespace HUnityAutoTranslator.Core.Caching;

public sealed record TranslationCacheQuery(
    string? Search,
    string SortColumn,
    bool SortDescending,
    int Offset,
    int Limit,
    IReadOnlyList<TranslationCacheColumnFilter>? ColumnFilters = null);
```

Create `src/HUnityAutoTranslator.Core/Caching/TranslationCacheColumnFilter.cs`:

```csharp
namespace HUnityAutoTranslator.Core.Caching;

public sealed record TranslationCacheColumnFilter(
    string Column,
    IReadOnlyList<string?> Values);

public sealed record TranslationCacheFilterOptionsQuery(
    string Column,
    string? Search,
    IReadOnlyList<TranslationCacheColumnFilter>? ColumnFilters = null,
    string? OptionSearch = null,
    int Limit = 100);

public sealed record TranslationCacheFilterOption(
    string? Value,
    int Count);

public sealed record TranslationCacheFilterOptionPage(
    string Column,
    IReadOnlyList<TranslationCacheFilterOption> Items);

public static class TranslationCacheColumns
{
    public const string EmptyValueMarker = "__HUNITY_EMPTY__";

    private static readonly HashSet<string> FilterableColumns = new(StringComparer.OrdinalIgnoreCase)
    {
        "source_text",
        "translated_text",
        "target_language",
        "scene_name",
        "component_hierarchy",
        "component_type",
        "replacement_font",
        "provider_kind",
        "provider_model",
        "created_utc",
        "updated_utc"
    };

    public static bool IsFilterable(string? column)
    {
        return !string.IsNullOrWhiteSpace(column) && FilterableColumns.Contains(column);
    }

    public static string NormalizeColumn(string? column)
    {
        return IsFilterable(column) ? column!.Trim().ToLowerInvariant() : string.Empty;
    }

    public static string? NormalizeFilterValue(string? value)
    {
        return string.Equals(value, EmptyValueMarker, StringComparison.Ordinal)
            ? null
            : value;
    }

    public static string? NormalizeOptionValue(string? value)
    {
        return string.IsNullOrEmpty(value) ? null : value;
    }

    public static string? ValueFor(TranslationCacheEntry row, string column)
    {
        return NormalizeColumn(column) switch
        {
            "source_text" => row.SourceText,
            "translated_text" => row.TranslatedText,
            "target_language" => row.TargetLanguage,
            "scene_name" => row.SceneName,
            "component_hierarchy" => row.ComponentHierarchy,
            "component_type" => row.ComponentType,
            "replacement_font" => row.ReplacementFont,
            "provider_kind" => row.ProviderKind,
            "provider_model" => row.ProviderModel,
            "created_utc" => row.CreatedUtc.ToString("O"),
            "updated_utc" => row.UpdatedUtc.ToString("O"),
            _ => null
        };
    }

    public static IReadOnlyList<TranslationCacheColumnFilter> NormalizeFilters(
        IReadOnlyList<TranslationCacheColumnFilter>? filters,
        string? excludedColumn = null)
    {
        var excluded = NormalizeColumn(excludedColumn);
        if (filters == null || filters.Count == 0)
        {
            return Array.Empty<TranslationCacheColumnFilter>();
        }

        return filters
            .Select(filter => new TranslationCacheColumnFilter(
                NormalizeColumn(filter.Column),
                filter.Values.Select(NormalizeFilterValue).Distinct().ToArray()))
            .Where(filter => filter.Column.Length > 0 && filter.Values.Count > 0 && !string.Equals(filter.Column, excluded, StringComparison.OrdinalIgnoreCase))
            .ToArray();
    }

    public static bool MatchesFilters(TranslationCacheEntry row, IReadOnlyList<TranslationCacheColumnFilter>? filters)
    {
        foreach (var filter in NormalizeFilters(filters))
        {
            var rowValue = NormalizeOptionValue(ValueFor(row, filter.Column));
            var matched = filter.Values.Any(value =>
                string.IsNullOrEmpty(value)
                    ? string.IsNullOrEmpty(rowValue)
                    : string.Equals(rowValue, value, StringComparison.Ordinal));
            if (!matched)
            {
                return false;
            }
        }

        return true;
    }
}
```

- [ ] **Step 4: Add the interface method**

In `src/HUnityAutoTranslator.Core/Caching/ITranslationCache.cs`, add this member after `Query`:

```csharp
TranslationCacheFilterOptionPage GetFilterOptions(TranslationCacheFilterOptionsQuery query);
```

- [ ] **Step 5: Implement Memory cache filtering**

In `MemoryTranslationCache.Query`, after the existing global search block and before sorting, add:

```csharp
rows = rows.Where(row => TranslationCacheColumns.MatchesFilters(row, query.ColumnFilters));
```

Also include replacement font in the existing global search predicate:

```csharp
Contains(row.ReplacementFont, search)
```

Add this method to `MemoryTranslationCache`:

```csharp
public TranslationCacheFilterOptionPage GetFilterOptions(TranslationCacheFilterOptionsQuery query)
{
    var column = TranslationCacheColumns.NormalizeColumn(query.Column);
    if (column.Length == 0)
    {
        return new TranslationCacheFilterOptionPage(string.Empty, Array.Empty<TranslationCacheFilterOption>());
    }

    var rows = _items.Values.AsEnumerable();
    if (!string.IsNullOrWhiteSpace(query.Search))
    {
        var search = query.Search.Trim();
        rows = rows.Where(row =>
            Contains(row.SourceText, search) ||
            Contains(row.TranslatedText, search) ||
            Contains(row.SceneName, search) ||
            Contains(row.ComponentHierarchy, search) ||
            Contains(row.ComponentType, search) ||
            Contains(row.ReplacementFont, search));
    }

    rows = rows.Where(row => TranslationCacheColumns.MatchesFilters(
        row,
        TranslationCacheColumns.NormalizeFilters(query.ColumnFilters, column)));

    if (!string.IsNullOrWhiteSpace(query.OptionSearch))
    {
        var optionSearch = query.OptionSearch.Trim();
        rows = rows.Where(row => Contains(TranslationCacheColumns.ValueFor(row, column), optionSearch));
    }

    var limit = Math.Min(500, Math.Max(1, query.Limit));
    var items = rows
        .GroupBy(row => TranslationCacheColumns.NormalizeOptionValue(TranslationCacheColumns.ValueFor(row, column)))
        .Select(group => new TranslationCacheFilterOption(group.Key, group.Count()))
        .OrderBy(item => item.Value is null ? 0 : 1)
        .ThenBy(item => item.Value, StringComparer.OrdinalIgnoreCase)
        .Take(limit)
        .ToArray();

    return new TranslationCacheFilterOptionPage(column, items);
}
```

- [ ] **Step 6: Implement Disk cache filtering**

Apply the same `Query` filter line, replacement font search term, and `GetFilterOptions` method to `DiskTranslationCache`. Use `_items.Values` exactly as in `MemoryTranslationCache`, and keep the existing `Persist()` behavior unchanged.

- [ ] **Step 7: Run cache tests**

Run:

```powershell
dotnet test tests/HUnityAutoTranslator.Core.Tests/HUnityAutoTranslator.Core.Tests.csproj --filter "FullyQualifiedName~TranslationCacheTests"
```

Expected: current new memory tests PASS; SQLite tests may still FAIL because `SqliteTranslationCache.GetFilterOptions` is not implemented yet.

- [ ] **Step 8: Commit Task 1**

Run:

```powershell
git add -- src/HUnityAutoTranslator.Core/Caching/TranslationCacheQuery.cs src/HUnityAutoTranslator.Core/Caching/TranslationCacheColumnFilter.cs src/HUnityAutoTranslator.Core/Caching/ITranslationCache.cs src/HUnityAutoTranslator.Core/Caching/MemoryTranslationCache.cs src/HUnityAutoTranslator.Core/Caching/DiskTranslationCache.cs tests/HUnityAutoTranslator.Core.Tests/Caching/TranslationCacheTests.cs
git commit -m "feat: add cache column filter model"
```

---

### Task 2: SQLite Column Filtering And Filter Options

**Files:**
- Modify: `src/HUnityAutoTranslator.Core/Caching/SqliteTranslationCache.cs`
- Test: `tests/HUnityAutoTranslator.Core.Tests/Caching/TranslationCacheTests.cs`

- [ ] **Step 1: Write failing SQLite tests**

Add these tests to `TranslationCacheTests.cs`:

```csharp
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
```

- [ ] **Step 2: Run tests to verify SQLite is missing implementation**

Run:

```powershell
dotnet test tests/HUnityAutoTranslator.Core.Tests/HUnityAutoTranslator.Core.Tests.csproj --filter "FullyQualifiedName~TranslationCacheTests"
```

Expected: FAIL with `SqliteTranslationCache` not implementing `GetFilterOptions`, or SQLite filter tests returning unfiltered rows.

- [ ] **Step 3: Add SQLite filter SQL helpers**

In `SqliteTranslationCache`, keep the existing `SortColumns` dictionary and add this dictionary beside it:

```csharp
private static readonly Dictionary<string, string> FilterColumns = new(StringComparer.OrdinalIgnoreCase)
{
    ["source_text"] = "source_text",
    ["translated_text"] = "translated_text",
    ["target_language"] = "target_language",
    ["scene_name"] = "scene_name",
    ["component_hierarchy"] = "component_hierarchy",
    ["component_type"] = "component_type",
    ["replacement_font"] = "replacement_font",
    ["provider_kind"] = "provider_kind",
    ["provider_model"] = "provider_model",
    ["created_utc"] = "created_utc",
    ["updated_utc"] = "updated_utc"
};
```

Add helper methods near the current `WhereClause` method:

```csharp
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
```

- [ ] **Step 4: Apply filters inside `Query`**

In `SqliteTranslationCache.Query`, replace both calls to `WhereClause(hasSearch)` with separate `BuildWhereClause` calls for each command, because each command owns its parameters:

```csharp
var countWhereClause = BuildWhereClause(hasSearch, query.ColumnFilters, countCommand);
countCommand.CommandText = "SELECT COUNT(*) FROM translations" + countWhereClause + ";";
if (hasSearch)
{
    countCommand.Parameters.AddWithValue("$search", "%" + query.Search!.Trim() + "%");
}
```

For the page query:

```csharp
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
```

Keep `$limit` and `$offset` unchanged.

- [ ] **Step 5: Implement SQLite filter options**

Add this method to `SqliteTranslationCache`:

```csharp
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
            reader.GetInt32(1)));
    }

    return new TranslationCacheFilterOptionPage(column, items);
}
```

- [ ] **Step 6: Run cache tests**

Run:

```powershell
dotnet test tests/HUnityAutoTranslator.Core.Tests/HUnityAutoTranslator.Core.Tests.csproj --filter "FullyQualifiedName~TranslationCacheTests"
```

Expected: PASS.

- [ ] **Step 7: Commit Task 2**

Run:

```powershell
git add -- src/HUnityAutoTranslator.Core/Caching/SqliteTranslationCache.cs tests/HUnityAutoTranslator.Core.Tests/Caching/TranslationCacheTests.cs
git commit -m "feat: filter sqlite translation cache columns"
```

---

### Task 3: HTTP Query Parsing And Filter Options Endpoint

**Files:**
- Modify: `src/HUnityAutoTranslator.Plugin/Web/LocalHttpServer.cs`
- Modify: `tests/HUnityAutoTranslator.Core.Tests/Control/ControlPanelHtmlSourceTests.cs`

- [ ] **Step 1: Write source assertions for the HTTP contract**

Add this test to `ControlPanelHtmlSourceTests.cs`:

```csharp
[Fact]
public void Local_http_server_exposes_translation_column_filter_options_endpoint()
{
    var serverSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.Plugin", "Web", "LocalHttpServer.cs"));

    serverSource.Should().Contain("path == \"/api/translations/filter-options\"");
    serverSource.Should().Contain("ParseTranslationFilterOptionsQuery");
    serverSource.Should().Contain("ParseColumnFilters");
    serverSource.Should().Contain("filter.");
    serverSource.Should().Contain("TranslationCacheColumns.EmptyValueMarker");
}
```

- [ ] **Step 2: Run the source test and verify it fails**

Run:

```powershell
dotnet test tests/HUnityAutoTranslator.Core.Tests/HUnityAutoTranslator.Core.Tests.csproj --filter "FullyQualifiedName~ControlPanelHtmlSourceTests.Local_http_server_exposes_translation_column_filter_options_endpoint"
```

Expected: FAIL because the endpoint and helper names do not exist yet.

- [ ] **Step 3: Add the filter-options route**

In `LocalHttpServer.HandleAsync`, add this branch immediately after the existing `GET /api/translations` branch:

```csharp
else if (context.Request.HttpMethod == "GET" && path == "/api/translations/filter-options")
{
    await WriteJsonAsync(context.Response, _cache.GetFilterOptions(ParseTranslationFilterOptionsQuery(context.Request))).ConfigureAwait(false);
}
```

- [ ] **Step 4: Extend translation query parsing**

Replace `ParseTranslationQuery` with:

```csharp
private static TranslationCacheQuery ParseTranslationQuery(HttpListenerRequest request)
{
    var parameters = request.QueryString;
    return new TranslationCacheQuery(
        Search: parameters["search"],
        SortColumn: parameters["sort"] ?? "updated_utc",
        SortDescending: string.Equals(parameters["direction"], "desc", StringComparison.OrdinalIgnoreCase),
        Offset: int.TryParse(parameters["offset"], out var offset) ? offset : 0,
        Limit: int.TryParse(parameters["limit"], out var limit) ? limit : 100,
        ColumnFilters: ParseColumnFilters(parameters));
}

private static TranslationCacheFilterOptionsQuery ParseTranslationFilterOptionsQuery(HttpListenerRequest request)
{
    var parameters = request.QueryString;
    var column = TranslationCacheColumns.NormalizeColumn(parameters["column"]);
    return new TranslationCacheFilterOptionsQuery(
        Column: column,
        Search: parameters["search"],
        ColumnFilters: ParseColumnFilters(parameters).Where(filter => !string.Equals(filter.Column, column, StringComparison.OrdinalIgnoreCase)).ToArray(),
        OptionSearch: parameters["optionSearch"],
        Limit: int.TryParse(parameters["limit"], out var limit) ? limit : 100);
}
```

Add this helper below the parse methods:

```csharp
private static IReadOnlyList<TranslationCacheColumnFilter> ParseColumnFilters(System.Collections.Specialized.NameValueCollection parameters)
{
    var filters = new List<TranslationCacheColumnFilter>();
    foreach (var key in parameters.AllKeys)
    {
        if (key == null || !key.StartsWith("filter.", StringComparison.Ordinal))
        {
            continue;
        }

        var column = TranslationCacheColumns.NormalizeColumn(key.Substring("filter.".Length));
        if (column.Length == 0)
        {
            continue;
        }

        var values = parameters.GetValues(key)?
            .Select(TranslationCacheColumns.NormalizeFilterValue)
            .ToArray() ?? Array.Empty<string?>();
        if (values.Length == 0)
        {
            continue;
        }

        filters.Add(new TranslationCacheColumnFilter(column, values));
    }

    return filters;
}
```

- [ ] **Step 5: Run source test**

Run:

```powershell
dotnet test tests/HUnityAutoTranslator.Core.Tests/HUnityAutoTranslator.Core.Tests.csproj --filter "FullyQualifiedName~ControlPanelHtmlSourceTests.Local_http_server_exposes_translation_column_filter_options_endpoint"
```

Expected: PASS.

- [ ] **Step 6: Commit Task 3**

Run:

```powershell
git add -- src/HUnityAutoTranslator.Plugin/Web/LocalHttpServer.cs tests/HUnityAutoTranslator.Core.Tests/Control/ControlPanelHtmlSourceTests.cs
git commit -m "feat: expose translation filter options api"
```

---

### Task 4: Frontend Column Header Filter Menus

**Files:**
- Modify: `src/HUnityAutoTranslator.Plugin/Web/ControlPanelHtml.cs`
- Modify: `tests/HUnityAutoTranslator.Core.Tests/Control/ControlPanelHtmlSourceTests.cs`

- [ ] **Step 1: Write source assertions for the frontend filter UI**

Add this test to `ControlPanelHtmlSourceTests.cs`:

```csharp
[Fact]
public void Translation_editor_exposes_excel_style_column_filters()
{
    var htmlSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.Plugin", "Web", "ControlPanelHtml.cs"));

    htmlSource.Should().Contain("id=\"clearTableFilters\"");
    htmlSource.Should().Contain("id=\"columnFilterMenu\"");
    htmlSource.Should().Contain("hunity.editor.columnFilters");
    htmlSource.Should().Contain("function openColumnFilterMenu(");
    htmlSource.Should().Contain("function loadColumnFilterOptions(");
    htmlSource.Should().Contain("function appendColumnFilters(");
    htmlSource.Should().Contain("/api/translations/filter-options");
    htmlSource.Should().Contain("filter-active");
    htmlSource.Should().Contain("data-filter-column");
}
```

- [ ] **Step 2: Run the source test and verify it fails**

Run:

```powershell
dotnet test tests/HUnityAutoTranslator.Core.Tests/HUnityAutoTranslator.Core.Tests.csproj --filter "FullyQualifiedName~ControlPanelHtmlSourceTests.Translation_editor_exposes_excel_style_column_filters"
```

Expected: FAIL because the UI hooks do not exist yet.

- [ ] **Step 3: Add CSS for header filter controls**

In the table CSS area of `ControlPanelHtml.cs`, add:

```css
.header-inner {
  display: grid;
  grid-template-columns: minmax(0, 1fr) auto auto;
  gap: 6px;
  align-items: center;
}
.header-title {
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.header-filter {
  width: 24px;
  min-height: 24px;
  padding: 0;
  justify-content: center;
  border-color: transparent;
  background: transparent;
  color: var(--muted);
}
.header-filter:hover,
.header-filter.filter-active {
  background: var(--surface);
  color: var(--accent);
  border-color: var(--line);
}
.column-filter-menu {
  position: fixed;
  z-index: 60;
  display: none;
  width: min(320px, calc(100vw - 24px));
  max-height: min(460px, calc(100vh - 24px));
  padding: 10px;
  border: 1px solid var(--line);
  border-radius: 8px;
  background: var(--surface);
  box-shadow: var(--shadow);
}
.column-filter-menu.open {
  display: grid;
  gap: 8px;
}
.filter-option-list {
  display: grid;
  gap: 4px;
  max-height: 250px;
  overflow: auto;
}
.filter-option-row {
  display: grid;
  grid-template-columns: auto minmax(0, 1fr) auto;
  gap: 8px;
  align-items: center;
  min-height: 30px;
  color: var(--ink);
}
.filter-option-row span {
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.filter-menu-actions {
  display: flex;
  justify-content: flex-end;
  gap: 8px;
  flex-wrap: wrap;
}
```

- [ ] **Step 4: Add HTML containers and clear button**

In the editor action area, add a secondary button near the existing column button:

```html
<button id="clearTableFilters" class="secondary" type="button">清空筛选</button>
```

After the existing `tableContextMenu`, add:

```html
<div class="column-filter-menu" id="columnFilterMenu" role="dialog" aria-label="列筛选"></div>
```

- [ ] **Step 5: Add table filter state**

Near existing `columnVisibilityStorageKey` and `columnOrderStorageKey`, add:

```javascript
const columnFilterStorageKey = "hunity.editor.columnFilters";
const emptyFilterValue = "__HUNITY_EMPTY__";
```

Add properties to `tableState`:

```javascript
filters: {},
openFilterColumn: null,
filterOptionDraft: new Set()
```

- [ ] **Step 6: Add filter persistence and query serialization helpers**

Add these functions near the column layout helpers:

```javascript
function loadColumnFilters() {
  try {
    const saved = JSON.parse(localStorage.getItem(columnFilterStorageKey) || "{}");
    tableState.filters = saved && typeof saved === "object" ? saved : {};
  } catch (_) {
    localStorage.removeItem(columnFilterStorageKey);
    tableState.filters = {};
  }
}

function persistColumnFilters() {
  localStorage.setItem(columnFilterStorageKey, JSON.stringify(tableState.filters));
}

function activeFilterCount() {
  return Object.values(tableState.filters).filter(values => Array.isArray(values) && values.length > 0).length;
}

function appendColumnFilters(params, excludedColumn) {
  Object.entries(tableState.filters).forEach(([column, values]) => {
    if (column === excludedColumn || !Array.isArray(values)) return;
    values.forEach(value => params.append("filter." + column, value === null || value === "" ? emptyFilterValue : value));
  });
}

function filterValuesFor(column) {
  return new Set((tableState.filters[column] || []).map(value => value === "" ? null : value));
}

function displayFilterValue(value, column) {
  if (value === null || value === undefined || value === "") return "空值";
  const definition = tableColumns.find(item => item.sort === column);
  return definition && definition.time ? formatDateTime(value) : value;
}
```

Call `loadColumnFilters();` during initialization, next to `loadColumnLayout();`.

- [ ] **Step 7: Render filter buttons in each column header**

Change `renderTableHead()` header HTML to include a filter button:

```javascript
const active = tableState.filters[column.sort] && tableState.filters[column.sort].length ? "filter-active" : "";
return `<th data-sort="${column.sort}" data-col="${column.index}">
  <div class="header-inner">
    <span class="header-title">${column.title}</span>
    <span>${mark}</span>
    <button class="header-filter ${active}" type="button" data-filter-column="${column.sort}" title="筛选 ${column.title}" aria-label="筛选 ${column.title}">⌄</button>
  </div>
  <span class="col-resizer" data-col="${column.index}"></span>
</th>`;
```

Update the click handler so the filter button does not sort:

```javascript
if (event.target.closest("[data-filter-column]")) return;
```

After binding header sort handlers, add:

```javascript
$$("[data-filter-column]").forEach(button => {
  button.addEventListener("click", event => {
    event.stopPropagation();
    openColumnFilterMenu(button.dataset.filterColumn, button);
  });
});
```

- [ ] **Step 8: Load and render filter options**

Add these functions near table rendering functions:

```javascript
async function loadColumnFilterOptions(column, optionSearch) {
  const params = new URLSearchParams({
    column,
    search: tableState.search,
    optionSearch: optionSearch || "",
    limit: "200"
  });
  appendColumnFilters(params, column);
  return api("/api/translations/filter-options?" + params.toString());
}

async function openColumnFilterMenu(column, anchor) {
  const menu = $("columnFilterMenu");
  tableState.openFilterColumn = column;
  tableState.filterOptionDraft = filterValuesFor(column);
  menu.innerHTML = `<div class="message">正在加载筛选值...</div>`;
  menu.classList.add("open");
  positionColumnFilterMenu(menu, anchor);
  const page = await loadColumnFilterOptions(column, "");
  renderColumnFilterMenu(column, anchor, page.Items || []);
}

function positionColumnFilterMenu(menu, anchor) {
  const rect = anchor.getBoundingClientRect();
  const left = Math.min(rect.left, window.innerWidth - menu.offsetWidth - 12);
  menu.style.left = Math.max(12, left) + "px";
  menu.style.top = Math.min(rect.bottom + 6, window.innerHeight - menu.offsetHeight - 12) + "px";
}

function renderColumnFilterMenu(column, anchor, items) {
  const menu = $("columnFilterMenu");
  menu.innerHTML = `
    <input id="columnFilterSearch" placeholder="搜索筛选值">
    <div class="filter-option-list">
      ${items.map(item => {
        const encoded = item.Value === null || item.Value === undefined ? "" : String(item.Value);
        const checked = tableState.filterOptionDraft.has(item.Value) ? "checked" : "";
        return `<label class="filter-option-row" title="${escapeHtml(displayFilterValue(item.Value, column))}">
          <input type="checkbox" data-filter-value="${escapeHtml(encoded)}" data-filter-empty="${item.Value === null || item.Value === undefined ? "true" : "false"}" ${checked}>
          <span>${escapeHtml(displayFilterValue(item.Value, column))}</span>
          <small>${item.Count}</small>
        </label>`;
      }).join("")}
    </div>
    <div class="filter-menu-actions">
      <button id="selectAllFilterOptions" type="button" class="secondary">全选</button>
      <button id="clearColumnFilter" type="button" class="secondary">清空</button>
      <button id="applyColumnFilter" type="button">应用</button>
    </div>`;
  $("columnFilterSearch").addEventListener("input", async event => {
    const nextPage = await loadColumnFilterOptions(column, event.target.value);
    renderColumnFilterMenu(column, anchor, nextPage.Items || []);
    $("columnFilterSearch").value = event.target.value;
    $("columnFilterSearch").focus();
  });
  $$(".filter-option-row input").forEach(input => {
    input.addEventListener("change", () => {
      const value = input.dataset.filterEmpty === "true" ? null : input.dataset.filterValue;
      if (input.checked) tableState.filterOptionDraft.add(value);
      else tableState.filterOptionDraft.delete(value);
    });
  });
  $("selectAllFilterOptions").addEventListener("click", () => {
    $$(".filter-option-row input").forEach(input => {
      input.checked = true;
      tableState.filterOptionDraft.add(input.dataset.filterEmpty === "true" ? null : input.dataset.filterValue);
    });
  });
  $("clearColumnFilter").addEventListener("click", () => applyColumnFilter(column, []));
  $("applyColumnFilter").addEventListener("click", () => applyColumnFilter(column, Array.from(tableState.filterOptionDraft)));
  positionColumnFilterMenu(menu, anchor);
}
```

- [ ] **Step 9: Apply and clear filters**

Add:

```javascript
function applyColumnFilter(column, values) {
  const cleanValues = values.filter((value, index, array) => array.findIndex(item => item === value) === index);
  if (cleanValues.length) tableState.filters[column] = cleanValues;
  else delete tableState.filters[column];
  tableState.offset = 0;
  tableState.selected.clear();
  tableState.anchor = null;
  persistColumnFilters();
  hideColumnFilterMenu();
  loadTranslations();
}

function clearAllColumnFilters() {
  tableState.filters = {};
  tableState.offset = 0;
  persistColumnFilters();
  hideColumnFilterMenu();
  loadTranslations();
}

function hideColumnFilterMenu() {
  $("columnFilterMenu").classList.remove("open");
  tableState.openFilterColumn = null;
}
```

Update `loadTranslations()` before the fetch:

```javascript
appendColumnFilters(params);
```

Update the table message:

```javascript
const filters = activeFilterCount();
$("tableMessage").textContent = `共 ${tableState.totalCount || 0} 条，当前显示 ${tableState.rows.length} 条，${columns.length}/${tableColumns.length} 列${filters ? `，已筛选 ${filters} 列` : ""}。`;
```

Bind clear and outside-click behavior:

```javascript
$("clearTableFilters").addEventListener("click", clearAllColumnFilters);
document.addEventListener("click", event => {
  if (!event.target.closest("#columnFilterMenu") && !event.target.closest("[data-filter-column]")) hideColumnFilterMenu();
});
```

- [ ] **Step 10: Run frontend source tests**

Run:

```powershell
dotnet test tests/HUnityAutoTranslator.Core.Tests/HUnityAutoTranslator.Core.Tests.csproj --filter "FullyQualifiedName~ControlPanelHtmlSourceTests"
```

Expected: PASS.

- [ ] **Step 11: Commit Task 4**

Run:

```powershell
git add -- src/HUnityAutoTranslator.Plugin/Web/ControlPanelHtml.cs tests/HUnityAutoTranslator.Core.Tests/Control/ControlPanelHtmlSourceTests.cs
git commit -m "feat: add editor column filter menus"
```

---

### Task 5: Full Verification And Packaging Check

**Files:**
- Verify only unless a previous task exposed a defect.

- [ ] **Step 1: Run the full test suite**

Run:

```powershell
dotnet test HUnityAutoTranslator.sln
```

Expected: PASS.

- [ ] **Step 2: Build the plugin**

Run:

```powershell
dotnet build HUnityAutoTranslator.sln
```

Expected: PASS with no C# compile errors.

- [ ] **Step 3: Verify no unrelated files are staged**

Run:

```powershell
git status --short
git diff --cached --name-only
```

Expected: only files from this feature are staged or committed. Existing unrelated dirty files from before the implementation may still appear unstaged.

- [ ] **Step 4: Final commit if verification required a fix**

Only if Step 1 or Step 2 required a follow-up fix, run:

```powershell
git add -- src/HUnityAutoTranslator.Core/Caching src/HUnityAutoTranslator.Plugin/Web tests/HUnityAutoTranslator.Core.Tests
git commit -m "fix: stabilize editor column filters"
```

Expected: commit succeeds, and `git status --short` still does not include unrelated staged files.

---

## Self-Review Notes

- Spec coverage: column header entry is Task 4; multi-layer `AND` filtering is Tasks 1 and 2; current-scope filter values are Tasks 1, 2, and 3; existing search/sort/pagination compatibility is covered in Tasks 1, 2, and 4; no storage migration is introduced.
- Placeholder scan: no placeholder markers were found, and every code-changing task includes concrete code or a concrete insertion point.
- Type consistency: `TranslationCacheColumnFilter`, `TranslationCacheFilterOptionsQuery`, `TranslationCacheFilterOption`, `TranslationCacheFilterOptionPage`, `TranslationCacheColumns.EmptyValueMarker`, and `ColumnFilters` are introduced before they are used by HTTP and frontend tasks.

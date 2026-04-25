# Control Panel Redesign Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Rebuild the localhost control panel into a sidebar-driven five-page operations console with live status metrics, expanded plugin and AI settings, provider utilities, an Excel-like translation database editor, and theme selection.

**Architecture:** Keep the browser UI as a static single-page app embedded in `ControlPanelHtml.cs`, served by the existing loopback-only `LocalHttpServer`. Move state, settings, cache table editing, provider utility parsing, and metrics into the core project so behavior is testable without Unity. Keep API keys server-side only and return structured JSON DTOs to the browser.

**Tech Stack:** C# `netstandard2.1`; Newtonsoft.Json; Microsoft.Data.Sqlite; xUnit; FluentAssertions; static HTML/CSS/JavaScript served by `HttpListener`.

---

## File Structure

- Modify `src/HUnityAutoTranslator.Core/Control/ControlPanelState.cs`: add status counters, provider state, recent translation preview, and new settings fields.
- Modify `src/HUnityAutoTranslator.Core/Control/UpdateConfigRequest.cs`: add plugin behavior settings, AI tuning settings, prompt customization, and provider utility options.
- Modify `src/HUnityAutoTranslator.Core/Control/ControlPanelSettings.cs`: persist expanded settings through the existing JSON store.
- Modify `src/HUnityAutoTranslator.Core/Control/ControlPanelService.cs`: clamp and save expanded settings; record provider status and metrics.
- Create `src/HUnityAutoTranslator.Core/Control/ControlPanelMetrics.cs`: thread-safe runtime metrics and recent completed translation previews.
- Create `src/HUnityAutoTranslator.Core/Control/ProviderStatus.cs`: provider check state DTO.
- Create `src/HUnityAutoTranslator.Core/Control/RecentTranslationPreview.cs`: source/translation/context preview DTO for the status page.
- Modify `src/HUnityAutoTranslator.Core/Configuration/RuntimeConfig.cs`: carry new runtime behavior and AI option values.
- Modify `src/HUnityAutoTranslator.Core/Providers/OpenAiResponsesProvider.cs`: send Responses-specific reasoning and verbosity fields.
- Modify `src/HUnityAutoTranslator.Core/Providers/ChatCompletionsProvider.cs`: send compatible provider tuning fields and DeepSeek thinking fields when configured.
- Create `src/HUnityAutoTranslator.Core/Providers/ProviderUtilityClient.cs`: fetch model lists, DeepSeek balance, OpenAI costs, and connection status.
- Create `src/HUnityAutoTranslator.Core/Providers/ProviderUtilityResult.cs`: DTOs for provider utility responses.
- Modify `src/HUnityAutoTranslator.Core/Caching/ITranslationCache.cs`: add query, update, import, and export operations.
- Create `src/HUnityAutoTranslator.Core/Caching/TranslationCacheEntry.cs`: row DTO for the table editor and import/export.
- Create `src/HUnityAutoTranslator.Core/Caching/TranslationCacheQuery.cs`: sort/filter/pagination request model.
- Create `src/HUnityAutoTranslator.Core/Caching/TranslationCachePage.cs`: paged query response.
- Modify `src/HUnityAutoTranslator.Core/Caching/SqliteTranslationCache.cs`: implement table query, update, import, and export.
- Modify `src/HUnityAutoTranslator.Core/Caching/MemoryTranslationCache.cs`: implement the extended cache contract for tests.
- Modify `src/HUnityAutoTranslator.Core/Caching/DiskTranslationCache.cs`: implement the extended cache contract with simple in-memory row reconstruction.
- Modify `src/HUnityAutoTranslator.Core/Pipeline/TextPipeline.cs`: update metrics for captured, queued, cache-hit completed, and ignored text.
- Modify `src/HUnityAutoTranslator.Core/Queueing/TranslationJobQueue.cs`: expose pending and in-flight counts.
- Modify `src/HUnityAutoTranslator.Core/Queueing/TranslationWorkerPool.cs`: update metrics for in-flight and completed provider translations.
- Modify `src/HUnityAutoTranslator.Plugin/TranslationWorkerHost.cs`: pass metrics into worker pools.
- Modify `src/HUnityAutoTranslator.Plugin/Plugin.cs`: create one metrics instance and pass queue/cache/provider dependencies to the HTTP server.
- Modify `src/HUnityAutoTranslator.Plugin/Web/LocalHttpServer.cs`: add JSON endpoints for translations and provider utilities.
- Modify `src/HUnityAutoTranslator.Plugin/Web/ControlPanelHtml.cs`: replace the single page with the sidebar single-page app.
- Modify tests under `tests/HUnityAutoTranslator.Core.Tests/Control/`, `Caching/`, `Providers/`, `Pipeline/`, and `Queueing/`: cover new behavior.

## Task 1: Runtime Metrics And Status DTOs

**Files:**
- Create: `src/HUnityAutoTranslator.Core/Control/ControlPanelMetrics.cs`
- Create: `src/HUnityAutoTranslator.Core/Control/RecentTranslationPreview.cs`
- Create: `src/HUnityAutoTranslator.Core/Control/ProviderStatus.cs`
- Modify: `src/HUnityAutoTranslator.Core/Control/ControlPanelState.cs`
- Modify: `src/HUnityAutoTranslator.Core/Control/ControlPanelService.cs`
- Test: `tests/HUnityAutoTranslator.Core.Tests/Control/ControlPanelServiceTests.cs`

- [ ] **Step 1: Write the failing metrics snapshot test**

Add this test to `ControlPanelServiceTests.cs`:

```csharp
[Fact]
public void Snapshot_reports_pipeline_metrics_and_recent_translations()
{
    var metrics = new ControlPanelMetrics();
    var service = ControlPanelService.CreateDefault(metrics);

    metrics.RecordCaptured();
    metrics.RecordQueued();
    metrics.RecordTranslationStarted();
    metrics.RecordTranslationCompleted(new RecentTranslationPreview(
        SourceText: "Start Game",
        TranslatedText: "开始游戏",
        TargetLanguage: "zh-Hans",
        Provider: "OpenAI",
        Model: "gpt-5.5",
        Context: "MainMenu/Button",
        CompletedUtc: DateTimeOffset.Parse("2026-04-26T00:00:00Z")));

    var state = service.GetState(queueCount: 1, cacheCount: 5, writebackQueueCount: 2);

    state.CapturedTextCount.Should().Be(1);
    state.QueuedTextCount.Should().Be(1);
    state.InFlightTranslationCount.Should().Be(0);
    state.CompletedTranslationCount.Should().Be(1);
    state.WritebackQueueCount.Should().Be(2);
    state.RecentTranslations.Should().ContainSingle();
    state.RecentTranslations[0].TranslatedText.Should().Be("开始游戏");
}
```

- [ ] **Step 2: Run the test and verify it fails**

Run:

```powershell
dotnet test tests/HUnityAutoTranslator.Core.Tests/HUnityAutoTranslator.Core.Tests.csproj --filter ControlPanelServiceTests.Snapshot_reports_pipeline_metrics_and_recent_translations
```

Expected: FAIL because `ControlPanelMetrics`, new `CreateDefault` overload, and new state fields do not exist.

- [ ] **Step 3: Add the metrics and status DTOs**

Create `ControlPanelMetrics.cs`:

```csharp
namespace HUnityAutoTranslator.Core.Control;

public sealed class ControlPanelMetrics
{
    private readonly object _gate = new();
    private readonly Queue<RecentTranslationPreview> _recentTranslations = new();
    private long _capturedTextCount;
    private long _queuedTextCount;
    private long _inFlightTranslationCount;
    private long _completedTranslationCount;

    public void RecordCaptured() => Interlocked.Increment(ref _capturedTextCount);

    public void RecordQueued() => Interlocked.Increment(ref _queuedTextCount);

    public void RecordTranslationStarted() => Interlocked.Increment(ref _inFlightTranslationCount);

    public void RecordTranslationCompleted(RecentTranslationPreview preview)
    {
        Interlocked.Increment(ref _completedTranslationCount);
        var current = Interlocked.Decrement(ref _inFlightTranslationCount);
        if (current < 0)
        {
            Interlocked.Exchange(ref _inFlightTranslationCount, 0);
        }

        lock (_gate)
        {
            _recentTranslations.Enqueue(preview);
            while (_recentTranslations.Count > 12)
            {
                _recentTranslations.Dequeue();
            }
        }
    }

    public void RecordTranslationFinishedWithoutResult()
    {
        var current = Interlocked.Decrement(ref _inFlightTranslationCount);
        if (current < 0)
        {
            Interlocked.Exchange(ref _inFlightTranslationCount, 0);
        }
    }

    public ControlPanelMetricsSnapshot Snapshot()
    {
        lock (_gate)
        {
            return new ControlPanelMetricsSnapshot(
                CapturedTextCount: Interlocked.Read(ref _capturedTextCount),
                QueuedTextCount: Interlocked.Read(ref _queuedTextCount),
                InFlightTranslationCount: Interlocked.Read(ref _inFlightTranslationCount),
                CompletedTranslationCount: Interlocked.Read(ref _completedTranslationCount),
                RecentTranslations: _recentTranslations.Reverse().ToArray());
        }
    }
}

public sealed record ControlPanelMetricsSnapshot(
    long CapturedTextCount,
    long QueuedTextCount,
    long InFlightTranslationCount,
    long CompletedTranslationCount,
    IReadOnlyList<RecentTranslationPreview> RecentTranslations);
```

Create `RecentTranslationPreview.cs`:

```csharp
namespace HUnityAutoTranslator.Core.Control;

public sealed record RecentTranslationPreview(
    string SourceText,
    string TranslatedText,
    string TargetLanguage,
    string Provider,
    string Model,
    string? Context,
    DateTimeOffset CompletedUtc);
```

Create `ProviderStatus.cs`:

```csharp
namespace HUnityAutoTranslator.Core.Control;

public sealed record ProviderStatus(
    string State,
    string Message,
    DateTimeOffset? CheckedUtc);
```

- [ ] **Step 4: Expand `ControlPanelState` and `ControlPanelService.GetState`**

Add fields to `ControlPanelState` after `CacheCount`:

```csharp
long CapturedTextCount,
long QueuedTextCount,
long InFlightTranslationCount,
long CompletedTranslationCount,
int WritebackQueueCount,
ProviderStatus ProviderStatus,
IReadOnlyList<RecentTranslationPreview> RecentTranslations,
```

Change `ControlPanelService` to accept a `ControlPanelMetrics` dependency:

```csharp
private readonly ControlPanelMetrics _metrics;
private ProviderStatus _providerStatus = new("unchecked", "尚未检测", null);

private ControlPanelService(RuntimeConfig config, IControlPanelSettingsStore? settingsStore, ControlPanelMetrics metrics)
{
    _config = config;
    _settingsStore = settingsStore;
    _metrics = metrics;
}
```

Add overloads:

```csharp
public static ControlPanelService CreateDefault(ControlPanelMetrics? metrics = null)
{
    return CreateDefault(settingsStore: null, metrics);
}

public static ControlPanelService CreateDefault(IControlPanelSettingsStore? settingsStore, ControlPanelMetrics? metrics = null)
{
    var service = new ControlPanelService(RuntimeConfig.CreateDefault(), settingsStore, metrics ?? new ControlPanelMetrics());
    if (settingsStore != null)
    {
        service.Load(settingsStore.Load());
    }

    return service;
}
```

Change `GetState` signature:

```csharp
public ControlPanelState GetState(int queueCount = 0, int cacheCount = 0, int writebackQueueCount = 0)
```

Inside `GetState`, read `var metrics = _metrics.Snapshot();` and pass the new state fields from `metrics`.

- [ ] **Step 5: Run the focused test**

Run:

```powershell
dotnet test tests/HUnityAutoTranslator.Core.Tests/HUnityAutoTranslator.Core.Tests.csproj --filter ControlPanelServiceTests
```

Expected: PASS for existing and new control panel service tests.

- [ ] **Step 6: Commit**

Run:

```powershell
git add src/HUnityAutoTranslator.Core/Control tests/HUnityAutoTranslator.Core.Tests/Control/ControlPanelServiceTests.cs
git commit -m "feat: add control panel runtime metrics"
```

## Task 2: Expanded Runtime And AI Settings

**Files:**
- Modify: `src/HUnityAutoTranslator.Core/Configuration/RuntimeConfig.cs`
- Modify: `src/HUnityAutoTranslator.Core/Control/UpdateConfigRequest.cs`
- Modify: `src/HUnityAutoTranslator.Core/Control/ControlPanelSettings.cs`
- Modify: `src/HUnityAutoTranslator.Core/Control/ControlPanelService.cs`
- Modify: `src/HUnityAutoTranslator.Core/Prompts/PromptOptions.cs`
- Modify: `src/HUnityAutoTranslator.Core/Prompts/PromptBuilder.cs`
- Modify: `src/HUnityAutoTranslator.Core/Providers/OpenAiResponsesProvider.cs`
- Modify: `src/HUnityAutoTranslator.Core/Providers/ChatCompletionsProvider.cs`
- Test: `tests/HUnityAutoTranslator.Core.Tests/Control/ControlPanelServiceTests.cs`
- Test: `tests/HUnityAutoTranslator.Core.Tests/Providers/ProviderRequestOptionsTests.cs`

- [ ] **Step 1: Write the failing persisted settings test**

Add this test to `ControlPanelServiceTests.cs`:

```csharp
[Fact]
public void CreateDefault_loads_expanded_plugin_and_ai_settings()
{
    var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "settings.json");
    var first = ControlPanelService.CreateDefault(new JsonControlPanelSettingsStore(path));

    first.UpdateConfig(new UpdateConfigRequest(
        TargetLanguage: "zh-Hant",
        RequestTimeoutSeconds: 45,
        ReasoningEffort: "low",
        OutputVerbosity: "low",
        DeepSeekThinkingMode: "disabled",
        Temperature: 0.2,
        CustomInstruction: "保持游戏术语一致",
        MaxSourceTextLength: 800,
        IgnoreInvisibleText: true,
        SkipNumericSymbolText: true,
        EnableCacheLookup: true,
        ManualEditsOverrideAi: true,
        ReapplyRememberedTranslations: true,
        CacheRetentionDays: 180));

    var second = ControlPanelService.CreateDefault(new JsonControlPanelSettingsStore(path));
    var state = second.GetState();

    state.RequestTimeoutSeconds.Should().Be(45);
    state.ReasoningEffort.Should().Be("low");
    state.OutputVerbosity.Should().Be("low");
    state.DeepSeekThinkingMode.Should().Be("disabled");
    state.Temperature.Should().Be(0.2);
    state.CustomInstruction.Should().Be("保持游戏术语一致");
    state.MaxSourceTextLength.Should().Be(800);
    state.IgnoreInvisibleText.Should().BeTrue();
    state.SkipNumericSymbolText.Should().BeTrue();
    state.EnableCacheLookup.Should().BeTrue();
    state.ManualEditsOverrideAi.Should().BeTrue();
    state.ReapplyRememberedTranslations.Should().BeTrue();
    state.CacheRetentionDays.Should().Be(180);
}
```

- [ ] **Step 2: Run the test and verify it fails**

Run:

```powershell
dotnet test tests/HUnityAutoTranslator.Core.Tests/HUnityAutoTranslator.Core.Tests.csproj --filter ControlPanelServiceTests.CreateDefault_loads_expanded_plugin_and_ai_settings
```

Expected: FAIL because request, config, and state fields do not exist.

- [ ] **Step 3: Expand config/request/state models**

Add these fields to `RuntimeConfig`:

```csharp
int RequestTimeoutSeconds,
string ReasoningEffort,
string OutputVerbosity,
string DeepSeekThinkingMode,
double? Temperature,
string? CustomInstruction,
int MaxSourceTextLength,
bool IgnoreInvisibleText,
bool SkipNumericSymbolText,
bool EnableCacheLookup,
bool ManualEditsOverrideAi,
bool ReapplyRememberedTranslations,
int CacheRetentionDays
```

Use these defaults in `CreateDefault()`:

```csharp
RequestTimeoutSeconds: 30,
ReasoningEffort: "low",
OutputVerbosity: "low",
DeepSeekThinkingMode: "enabled",
Temperature: null,
CustomInstruction: null,
MaxSourceTextLength: 2000,
IgnoreInvisibleText: true,
SkipNumericSymbolText: true,
EnableCacheLookup: true,
ManualEditsOverrideAi: true,
ReapplyRememberedTranslations: true,
CacheRetentionDays: 365
```

Add nullable matching fields to `UpdateConfigRequest` and non-null state fields to `ControlPanelState`.

- [ ] **Step 4: Clamp and save expanded settings**

In `ControlPanelService.ApplyConfig`, add:

```csharp
var requestTimeoutSeconds = request.RequestTimeoutSeconds.HasValue
    ? Clamp(request.RequestTimeoutSeconds.Value, 5, 180)
    : _config.RequestTimeoutSeconds;
var maxSourceTextLength = request.MaxSourceTextLength.HasValue
    ? Clamp(request.MaxSourceTextLength.Value, 20, 10000)
    : _config.MaxSourceTextLength;
var cacheRetentionDays = request.CacheRetentionDays.HasValue
    ? Clamp(request.CacheRetentionDays.Value, 1, 3650)
    : _config.CacheRetentionDays;
var temperature = request.Temperature.HasValue
    ? Math.Min(2.0, Math.Max(0.0, request.Temperature.Value))
    : _config.Temperature;
```

Add helper normalization:

```csharp
private static string SelectKnown(string? value, string fallback, params string[] allowed)
{
    if (string.IsNullOrWhiteSpace(value))
    {
        return fallback;
    }

    var trimmed = value.Trim();
    return allowed.Any(item => string.Equals(item, trimmed, StringComparison.OrdinalIgnoreCase))
        ? allowed.First(item => string.Equals(item, trimmed, StringComparison.OrdinalIgnoreCase))
        : fallback;
}
```

Use it for `ReasoningEffort`, `OutputVerbosity`, and `DeepSeekThinkingMode`.

- [ ] **Step 5: Wire prompt custom instruction**

Change worker prompt creation later in Task 6 to pass `_config.CustomInstruction`. Prepare `PromptOptions` and `PromptBuilder` by keeping the existing third `CustomInstruction` field and ensuring `BuildSystemPrompt` already appends it.

- [ ] **Step 6: Write provider request option tests**

Create `tests/HUnityAutoTranslator.Core.Tests/Providers/ProviderRequestOptionsTests.cs`:

```csharp
using System.Net;
using System.Text;
using FluentAssertions;
using HUnityAutoTranslator.Core.Configuration;
using HUnityAutoTranslator.Core.Providers;
using Newtonsoft.Json.Linq;

namespace HUnityAutoTranslator.Core.Tests.Providers;

public sealed class ProviderRequestOptionsTests
{
    [Fact]
    public async Task OpenAiResponsesProvider_sends_reasoning_and_verbosity()
    {
        var handler = new CaptureHandler("""{"output":[{"content":[{"text":"[\"开始\"]"}]}]}""");
        var client = new HttpClient(handler);
        var profile = ProviderProfile.DefaultOpenAi() with { ApiKeyConfigured = true };
        var provider = new OpenAiResponsesProvider(client, profile, () => "key", "low", "low", TimeSpan.FromSeconds(30));

        await provider.TranslateAsync(new TranslationRequest(
            new[] { "Start" },
            "zh-Hans",
            "system",
            "user"), CancellationToken.None);

        var body = JObject.Parse(handler.Body);
        body["reasoning"]!["effort"]!.Value<string>().Should().Be("low");
        body["text"]!["verbosity"]!.Value<string>().Should().Be("low");
    }

    [Fact]
    public async Task ChatCompletionsProvider_sends_deepseek_thinking_options()
    {
        var handler = new CaptureHandler("""{"choices":[{"message":{"content":"[\"开始\"]"}}]}""");
        var client = new HttpClient(handler);
        var profile = ProviderProfile.DefaultDeepSeek() with { ApiKeyConfigured = true };
        var provider = new ChatCompletionsProvider(client, profile, () => "key", "high", "enabled", 0.2, TimeSpan.FromSeconds(30));

        await provider.TranslateAsync(new TranslationRequest(
            new[] { "Start" },
            "zh-Hans",
            "system",
            "user"), CancellationToken.None);

        var body = JObject.Parse(handler.Body);
        body["reasoning_effort"]!.Value<string>().Should().Be("high");
        body["thinking"]!["type"]!.Value<string>().Should().Be("enabled");
        body["temperature"]!.Value<double>().Should().Be(0.2);
    }

    private sealed class CaptureHandler : HttpMessageHandler
    {
        private readonly string _json;

        public CaptureHandler(string json)
        {
            _json = json;
        }

        public string Body { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Body = request.Content == null ? string.Empty : await request.Content.ReadAsStringAsync();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_json, Encoding.UTF8, "application/json")
            };
        }
    }
}
```

- [ ] **Step 7: Update provider constructors and request bodies**

Add constructor parameters to `OpenAiResponsesProvider`:

```csharp
string reasoningEffort = "low",
string outputVerbosity = "low",
TimeSpan? timeout = null
```

Add to the Responses body:

```csharp
["reasoning"] = new JObject { ["effort"] = _reasoningEffort },
["text"] = new JObject { ["verbosity"] = _outputVerbosity }
```

Add constructor parameters to `ChatCompletionsProvider`:

```csharp
string reasoningEffort = "high",
string deepSeekThinkingMode = "enabled",
double? temperature = null,
TimeSpan? timeout = null
```

For DeepSeek, add:

```csharp
body["reasoning_effort"] = _reasoningEffort;
body["thinking"] = new JObject { ["type"] = _deepSeekThinkingMode };
```

For non-thinking or compatible providers, add `temperature` only when it has a value.

- [ ] **Step 8: Run focused tests**

Run:

```powershell
dotnet test tests/HUnityAutoTranslator.Core.Tests/HUnityAutoTranslator.Core.Tests.csproj --filter "ControlPanelServiceTests|ProviderRequestOptionsTests"
```

Expected: PASS.

- [ ] **Step 9: Commit**

Run:

```powershell
git add src/HUnityAutoTranslator.Core/Configuration src/HUnityAutoTranslator.Core/Control src/HUnityAutoTranslator.Core/Providers tests/HUnityAutoTranslator.Core.Tests/Control tests/HUnityAutoTranslator.Core.Tests/Providers
git commit -m "feat: expand control panel runtime settings"
```

## Task 3: Translation Cache Table Operations

**Files:**
- Create: `src/HUnityAutoTranslator.Core/Caching/TranslationCacheEntry.cs`
- Create: `src/HUnityAutoTranslator.Core/Caching/TranslationCacheQuery.cs`
- Create: `src/HUnityAutoTranslator.Core/Caching/TranslationCachePage.cs`
- Modify: `src/HUnityAutoTranslator.Core/Caching/ITranslationCache.cs`
- Modify: `src/HUnityAutoTranslator.Core/Caching/SqliteTranslationCache.cs`
- Modify: `src/HUnityAutoTranslator.Core/Caching/MemoryTranslationCache.cs`
- Modify: `src/HUnityAutoTranslator.Core/Caching/DiskTranslationCache.cs`
- Test: `tests/HUnityAutoTranslator.Core.Tests/Caching/TranslationCacheTests.cs`

- [ ] **Step 1: Write failing query/update/import/export tests**

Add tests to `TranslationCacheTests.cs`:

```csharp
[Fact]
public void Sqlite_cache_queries_updates_and_exports_translation_rows()
{
    var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "translation-cache.sqlite");
    using var cache = new SqliteTranslationCache(path);
    var key = TranslationCacheKey.Create("Start Game", "zh-Hans", ProviderProfile.DefaultOpenAi(), "prompt-v1");
    cache.Set(key, "开始游戏", new TranslationCacheContext("Menu", "Canvas/Button", "Text"));

    var page = cache.Query(new TranslationCacheQuery(Search: "Start", SortColumn: "source_text", SortDescending: false, Offset: 0, Limit: 20));
    page.TotalCount.Should().Be(1);
    page.Items[0].SourceText.Should().Be("Start Game");
    page.Items[0].TranslatedText.Should().Be("开始游戏");

    cache.Update(new TranslationCacheEntry(
        SourceText: "Start Game",
        TargetLanguage: "zh-Hans",
        ProviderKind: "OpenAI",
        ProviderBaseUrl: "https://api.openai.com",
        ProviderEndpoint: "/v1/responses",
        ProviderModel: "gpt-5.5",
        PromptPolicyVersion: "prompt-v1",
        TranslatedText: "开始",
        SceneName: "Menu",
        ComponentHierarchy: "Canvas/Button",
        ComponentType: "Text",
        CreatedUtc: page.Items[0].CreatedUtc,
        UpdatedUtc: DateTimeOffset.Parse("2026-04-26T00:00:00Z")));

    cache.Query(new TranslationCacheQuery(Search: "Start", SortColumn: "updated_utc", SortDescending: true, Offset: 0, Limit: 20))
        .Items[0].TranslatedText.Should().Be("开始");
    cache.Export("json").Should().Contain("Start Game");
    cache.Export("csv").Should().Contain("source_text");
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
    "translated_text": "背包已满",
    "scene_name": "Hud",
    "component_hierarchy": "Canvas/Toast",
    "component_type": "Text"
  }
]
""";

    var result = cache.Import(json, "json");

    result.ImportedCount.Should().Be(1);
    cache.Query(new TranslationCacheQuery(Search: "Inventory", SortColumn: "source_text", SortDescending: false, Offset: 0, Limit: 20))
        .Items[0].TranslatedText.Should().Be("背包已满");
}
```

- [ ] **Step 2: Run tests and verify failure**

Run:

```powershell
dotnet test tests/HUnityAutoTranslator.Core.Tests/HUnityAutoTranslator.Core.Tests.csproj --filter "Sqlite_cache_queries_updates_and_exports_translation_rows|Sqlite_cache_imports_valid_json_rows"
```

Expected: FAIL because cache query DTOs and methods do not exist.

- [ ] **Step 3: Add cache DTOs and interface methods**

Create `TranslationCacheEntry.cs`:

```csharp
namespace HUnityAutoTranslator.Core.Caching;

public sealed record TranslationCacheEntry(
    string SourceText,
    string TargetLanguage,
    string ProviderKind,
    string ProviderBaseUrl,
    string ProviderEndpoint,
    string ProviderModel,
    string PromptPolicyVersion,
    string TranslatedText,
    string? SceneName,
    string? ComponentHierarchy,
    string? ComponentType,
    DateTimeOffset CreatedUtc,
    DateTimeOffset UpdatedUtc);
```

Create `TranslationCacheQuery.cs`:

```csharp
namespace HUnityAutoTranslator.Core.Caching;

public sealed record TranslationCacheQuery(
    string? Search,
    string SortColumn,
    bool SortDescending,
    int Offset,
    int Limit);
```

Create `TranslationCachePage.cs`:

```csharp
namespace HUnityAutoTranslator.Core.Caching;

public sealed record TranslationCachePage(
    int TotalCount,
    IReadOnlyList<TranslationCacheEntry> Items);

public sealed record TranslationCacheImportResult(
    int ImportedCount,
    IReadOnlyList<string> Errors);
```

Add to `ITranslationCache`:

```csharp
TranslationCachePage Query(TranslationCacheQuery query);

void Update(TranslationCacheEntry entry);

string Export(string format);

TranslationCacheImportResult Import(string content, string format);
```

- [ ] **Step 4: Implement SQLite query/update/export/import**

In `SqliteTranslationCache`, add a whitelist:

```csharp
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
    ["created_utc"] = "created_utc",
    ["updated_utc"] = "updated_utc"
};
```

Implement `Query` with parameterized search:

```csharp
var sortColumn = SortColumns.TryGetValue(query.SortColumn, out var value) ? value : "updated_utc";
var direction = query.SortDescending ? "DESC" : "ASC";
var limit = Math.Min(500, Math.Max(1, query.Limit));
var offset = Math.Max(0, query.Offset);
```

Search should match `source_text`, `translated_text`, `scene_name`, `component_hierarchy`, and `component_type`.

Implement `Update` with the existing primary key columns and update `translated_text`, context columns, and `updated_utc`.

Implement `Export("json")` using `JsonConvert.SerializeObject(Query(allRows).Items, Formatting.Indented)`.

Implement `Export("csv")` with an invariant header:

```text
source_text,target_language,provider_kind,provider_base_url,provider_endpoint,provider_model,prompt_policy_version,translated_text,scene_name,component_hierarchy,component_type,created_utc,updated_utc
```

Implement `Import("json")` using `JArray.Parse`, validate required string fields, and call `Update` for each valid row.

- [ ] **Step 5: Implement memory and disk compatibility**

For `MemoryTranslationCache`, replace the `ConcurrentDictionary<TranslationCacheKey,string>` value with a small row object containing key, translated text, context, created UTC, and updated UTC. Implement query by LINQ over the dictionary values.

For `DiskTranslationCache`, keep existing persistence behavior and implement query/export/update/import against its in-memory dictionary so tests and runtime code can call the interface consistently.

- [ ] **Step 6: Run cache tests**

Run:

```powershell
dotnet test tests/HUnityAutoTranslator.Core.Tests/HUnityAutoTranslator.Core.Tests.csproj --filter TranslationCacheTests
```

Expected: PASS.

- [ ] **Step 7: Commit**

Run:

```powershell
git add src/HUnityAutoTranslator.Core/Caching tests/HUnityAutoTranslator.Core.Tests/Caching/TranslationCacheTests.cs
git commit -m "feat: add translation cache table operations"
```

## Task 4: Provider Utility Client

**Files:**
- Create: `src/HUnityAutoTranslator.Core/Providers/ProviderUtilityClient.cs`
- Create: `src/HUnityAutoTranslator.Core/Providers/ProviderUtilityResult.cs`
- Test: `tests/HUnityAutoTranslator.Core.Tests/Providers/ProviderUtilityClientTests.cs`

- [ ] **Step 1: Write failing provider utility tests**

Create `ProviderUtilityClientTests.cs`:

```csharp
using System.Net;
using System.Text;
using FluentAssertions;
using HUnityAutoTranslator.Core.Configuration;
using HUnityAutoTranslator.Core.Providers;

namespace HUnityAutoTranslator.Core.Tests.Providers;

public sealed class ProviderUtilityClientTests
{
    [Fact]
    public async Task FetchModelsAsync_parses_openai_compatible_model_list()
    {
        var handler = new CaptureHandler("""{"object":"list","data":[{"id":"gpt-5.5","object":"model","owned_by":"openai"}]}""");
        var client = new ProviderUtilityClient(new HttpClient(handler), () => "key");

        var result = await client.FetchModelsAsync(ProviderProfile.DefaultOpenAi(), CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        result.Models.Should().ContainSingle(model => model.Id == "gpt-5.5" && model.OwnedBy == "openai");
        handler.RequestPath.Should().Be("/v1/models");
    }

    [Fact]
    public async Task FetchBalanceAsync_parses_deepseek_balance()
    {
        var handler = new CaptureHandler("""{"is_available":true,"balance_infos":[{"currency":"CNY","total_balance":"110.00","granted_balance":"10.00","topped_up_balance":"100.00"}]}""");
        var client = new ProviderUtilityClient(new HttpClient(handler), () => "key");

        var result = await client.FetchBalanceAsync(ProviderProfile.DefaultDeepSeek(), CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        result.Balances.Should().ContainSingle(balance => balance.Currency == "CNY" && balance.TotalBalance == "110.00");
        handler.RequestPath.Should().Be("/user/balance");
    }

    private sealed class CaptureHandler : HttpMessageHandler
    {
        private readonly string _json;

        public CaptureHandler(string json)
        {
            _json = json;
        }

        public string RequestPath { get; private set; } = string.Empty;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestPath = request.RequestUri?.AbsolutePath ?? string.Empty;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_json, Encoding.UTF8, "application/json")
            });
        }
    }
}
```

- [ ] **Step 2: Run tests and verify failure**

Run:

```powershell
dotnet test tests/HUnityAutoTranslator.Core.Tests/HUnityAutoTranslator.Core.Tests.csproj --filter ProviderUtilityClientTests
```

Expected: FAIL because utility client types do not exist.

- [ ] **Step 3: Add utility result DTOs**

Create `ProviderUtilityResult.cs`:

```csharp
namespace HUnityAutoTranslator.Core.Providers;

public sealed record ProviderModelInfo(string Id, string? OwnedBy);

public sealed record ProviderBalanceInfo(
    string Currency,
    string TotalBalance,
    string? GrantedBalance,
    string? ToppedUpBalance);

public sealed record ProviderModelsResult(
    bool Succeeded,
    string Message,
    IReadOnlyList<ProviderModelInfo> Models);

public sealed record ProviderBalanceResult(
    bool Succeeded,
    string Message,
    IReadOnlyList<ProviderBalanceInfo> Balances);
```

- [ ] **Step 4: Implement `ProviderUtilityClient`**

Create `ProviderUtilityClient.cs`:

```csharp
using System.Net.Http.Headers;
using HUnityAutoTranslator.Core.Configuration;
using Newtonsoft.Json.Linq;

namespace HUnityAutoTranslator.Core.Providers;

public sealed class ProviderUtilityClient
{
    private readonly HttpClient _httpClient;
    private readonly Func<string?> _apiKeyProvider;

    public ProviderUtilityClient(HttpClient httpClient, Func<string?> apiKeyProvider)
    {
        _httpClient = httpClient;
        _apiKeyProvider = apiKeyProvider;
    }

    public async Task<ProviderModelsResult> FetchModelsAsync(ProviderProfile profile, CancellationToken cancellationToken)
    {
        var path = profile.Kind == ProviderKind.OpenAICompatible ? "/v1/models" : "/models";
        using var request = CreateGet(profile, path);
        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return new ProviderModelsResult(false, $"模型列表请求失败：{(int)response.StatusCode}", Array.Empty<ProviderModelInfo>());
        }

        var data = JObject.Parse(json)["data"] as JArray ?? new JArray();
        var models = data
            .OfType<JObject>()
            .Select(item => new ProviderModelInfo(
                item.Value<string>("id") ?? string.Empty,
                item.Value<string>("owned_by")))
            .Where(item => !string.IsNullOrWhiteSpace(item.Id))
            .ToArray();

        return new ProviderModelsResult(true, $"已获取 {models.Length} 个模型", models);
    }

    public async Task<ProviderBalanceResult> FetchBalanceAsync(ProviderProfile profile, CancellationToken cancellationToken)
    {
        var path = profile.Kind == ProviderKind.DeepSeek ? "/user/balance" : "/organization/costs";
        using var request = CreateGet(profile, path);
        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return new ProviderBalanceResult(false, $"账户信息请求失败：{(int)response.StatusCode}", Array.Empty<ProviderBalanceInfo>());
        }

        if (profile.Kind == ProviderKind.DeepSeek)
        {
            var data = JObject.Parse(json)["balance_infos"] as JArray ?? new JArray();
            var balances = data
                .OfType<JObject>()
                .Select(item => new ProviderBalanceInfo(
                    item.Value<string>("currency") ?? string.Empty,
                    item.Value<string>("total_balance") ?? string.Empty,
                    item.Value<string>("granted_balance"),
                    item.Value<string>("topped_up_balance")))
                .Where(item => !string.IsNullOrWhiteSpace(item.Currency))
                .ToArray();
            return new ProviderBalanceResult(true, $"已获取 {balances.Length} 条余额信息", balances);
        }

        return new ProviderBalanceResult(true, "OpenAI 返回成本信息；控制面板将显示原始摘要", Array.Empty<ProviderBalanceInfo>());
    }

    private HttpRequestMessage CreateGet(ProviderProfile profile, string path)
    {
        var baseUri = new Uri(profile.BaseUrl.TrimEnd('/') + "/");
        var request = new HttpRequestMessage(HttpMethod.Get, new Uri(baseUri, path.TrimStart('/')));
        var apiKey = _apiKeyProvider();
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        }

        return request;
    }
}
```

- [ ] **Step 5: Run provider utility tests**

Run:

```powershell
dotnet test tests/HUnityAutoTranslator.Core.Tests/HUnityAutoTranslator.Core.Tests.csproj --filter ProviderUtilityClientTests
```

Expected: PASS.

- [ ] **Step 6: Commit**

Run:

```powershell
git add src/HUnityAutoTranslator.Core/Providers tests/HUnityAutoTranslator.Core.Tests/Providers/ProviderUtilityClientTests.cs
git commit -m "feat: add provider utility client"
```

## Task 5: Wire Metrics Into Pipeline And Workers

**Files:**
- Modify: `src/HUnityAutoTranslator.Core/Pipeline/TextPipeline.cs`
- Modify: `src/HUnityAutoTranslator.Core/Queueing/TranslationJobQueue.cs`
- Modify: `src/HUnityAutoTranslator.Core/Queueing/TranslationWorkerPool.cs`
- Modify: `src/HUnityAutoTranslator.Plugin/TranslationWorkerHost.cs`
- Modify: `src/HUnityAutoTranslator.Plugin/Plugin.cs`
- Test: `tests/HUnityAutoTranslator.Core.Tests/Pipeline/TextPipelineTests.cs`
- Test: `tests/HUnityAutoTranslator.Core.Tests/Queueing/TranslationQueueTests.cs`
- Test: `tests/HUnityAutoTranslator.Core.Tests/Queueing/WorkerPoolTests.cs`

- [ ] **Step 1: Write failing in-flight queue test**

Add to `TranslationQueueTests.cs`:

```csharp
[Fact]
public void TryDequeueBatch_updates_pending_and_inflight_counts()
{
    var queue = new TranslationJobQueue();
    queue.Enqueue(TranslationJob.Create("target", "Start Game", TranslationPriority.Normal, TranslationCacheContext.Empty));

    queue.TryDequeueBatch(1, 2000, out var batch).Should().BeTrue();

    queue.PendingCount.Should().Be(0);
    queue.InFlightCount.Should().Be(1);

    queue.MarkCompleted(batch);

    queue.InFlightCount.Should().Be(0);
}
```

- [ ] **Step 2: Write failing pipeline metrics test**

Add to `TextPipelineTests.cs`:

```csharp
[Fact]
public void Process_records_captured_and_queued_metrics()
{
    var cache = new MemoryTranslationCache();
    var queue = new TranslationJobQueue();
    var metrics = new ControlPanelMetrics();
    var pipeline = new TextPipeline(cache, queue, RuntimeConfig.CreateDefault(), metrics);

    pipeline.Process(new CapturedText("target", "Start Game", true, TranslationCacheContext.Empty));

    var snapshot = metrics.Snapshot();
    snapshot.CapturedTextCount.Should().Be(1);
    snapshot.QueuedTextCount.Should().Be(1);
}
```

- [ ] **Step 3: Write failing worker completion preview test**

Add to `WorkerPoolTests.cs`:

```csharp
[Fact]
public async Task WorkerPool_records_recent_completed_translation()
{
    var queue = new TranslationJobQueue();
    var dispatcher = new ResultDispatcher();
    var metrics = new ControlPanelMetrics();
    var config = RuntimeConfig.CreateDefault() with
    {
        Provider = ProviderProfile.DefaultOpenAi() with { ApiKeyConfigured = true }
    };
    var cache = new MemoryTranslationCache();
    queue.Enqueue(TranslationJob.Create("target", "Start Game", TranslationPriority.Normal, TranslationCacheContext.Empty));
    var provider = new FakeProvider(new[] { "开始游戏" });
    var pool = new TranslationWorkerPool(queue, dispatcher, provider, new ProviderRateLimiter(600), config, cache, metrics);

    await pool.RunUntilIdleAsync(CancellationToken.None);

    var snapshot = metrics.Snapshot();
    snapshot.CompletedTranslationCount.Should().Be(1);
    snapshot.RecentTranslations.Should().ContainSingle(item => item.SourceText == "Start Game" && item.TranslatedText == "开始游戏");
}
```

- [ ] **Step 4: Run tests and verify failure**

Run:

```powershell
dotnet test tests/HUnityAutoTranslator.Core.Tests/HUnityAutoTranslator.Core.Tests.csproj --filter "TryDequeueBatch_updates_pending_and_inflight_counts|Process_records_captured_and_queued_metrics|WorkerPool_records_recent_completed_translation"
```

Expected: FAIL because metrics are not wired and `InFlightCount` is missing.

- [ ] **Step 5: Implement queue and pipeline metrics**

Add `InFlightCount` to `TranslationJobQueue` by returning `_inFlightSources.Count` under lock.

Add optional `ControlPanelMetrics? metrics = null` constructor arguments to `TextPipeline`. In `Process`, call:

```csharp
_metrics?.RecordCaptured();
```

before filtering. Call `_metrics?.RecordQueued();` only after `_queue.Enqueue(...)` is called for a translatable cache miss.

- [ ] **Step 6: Implement worker metrics**

Add optional `ControlPanelMetrics? metrics = null` to `TranslationWorkerPool`.

After a batch is dequeued, call `RecordTranslationStarted()` once for each job in the batch.

When publishing a successful result, call `RecordTranslationCompleted` with source text, translated text, target language, provider kind, model, context hierarchy, and `DateTimeOffset.UtcNow`.

In the `finally` block, if a job was started and not completed successfully, call `RecordTranslationFinishedWithoutResult()` for each unfinished job before `MarkCompleted`.

- [ ] **Step 7: Wire plugin dependencies**

In `Plugin.Awake`, create:

```csharp
var metrics = new ControlPanelMetrics();
_controlPanel = ControlPanelService.CreateDefault(new JsonControlPanelSettingsStore(settingsPath), metrics);
```

Pass `metrics` into `TextPipeline` and `TranslationWorkerHost`.

In `TranslationWorkerHost`, store the metrics and pass it into each `TranslationWorkerPool`. Also pass new provider constructor options from `config`.

- [ ] **Step 8: Run focused tests**

Run:

```powershell
dotnet test tests/HUnityAutoTranslator.Core.Tests/HUnityAutoTranslator.Core.Tests.csproj --filter "TextPipelineTests|TranslationQueueTests|WorkerPoolTests"
```

Expected: PASS.

- [ ] **Step 9: Commit**

Run:

```powershell
git add src/HUnityAutoTranslator.Core/Pipeline src/HUnityAutoTranslator.Core/Queueing src/HUnityAutoTranslator.Plugin tests/HUnityAutoTranslator.Core.Tests/Pipeline tests/HUnityAutoTranslator.Core.Tests/Queueing
git commit -m "feat: track translation pipeline status metrics"
```

## Task 6: Local HTTP API Endpoints

**Files:**
- Modify: `src/HUnityAutoTranslator.Plugin/Web/LocalHttpServer.cs`
- Modify: `src/HUnityAutoTranslator.Plugin/Plugin.cs`
- Test: no direct test project for plugin HTTP; verify with build and manual `Invoke-WebRequest` against a running game/plugin when available.

- [ ] **Step 1: Update server dependencies**

Change `LocalHttpServer` constructor to accept:

```csharp
ITranslationCache cache,
TranslationJobQueue queue,
ResultDispatcher dispatcher,
ProviderUtilityClient providerUtilityClient
```

Keep existing `Func<int>` parameters only if needed for compatibility during the edit; remove them once `WriteStateAsync` can read `queue.PendingCount`, `queue.InFlightCount`, `cache.Count`, and `dispatcher.PendingCount`.

- [ ] **Step 2: Update `/api/state`**

Change `WriteStateAsync` to:

```csharp
await WriteJsonAsync(response, _controlPanel.GetState(
    queueCount: _queue.PendingCount,
    cacheCount: _cache.Count,
    writebackQueueCount: _dispatcher.PendingCount)).ConfigureAwait(false);
```

- [ ] **Step 3: Add translations query endpoint**

In `HandleAsync`, add before the root HTML route:

```csharp
else if (context.Request.HttpMethod == "GET" && path == "/api/translations")
{
    var query = ParseTranslationQuery(context.Request);
    await WriteJsonAsync(context.Response, _cache.Query(query)).ConfigureAwait(false);
}
```

Implement query parsing:

```csharp
private static TranslationCacheQuery ParseTranslationQuery(HttpListenerRequest request)
{
    var parameters = request.QueryString;
    return new TranslationCacheQuery(
        Search: parameters["search"],
        SortColumn: parameters["sort"] ?? "updated_utc",
        SortDescending: string.Equals(parameters["direction"], "desc", StringComparison.OrdinalIgnoreCase),
        Offset: int.TryParse(parameters["offset"], out var offset) ? offset : 0,
        Limit: int.TryParse(parameters["limit"], out var limit) ? limit : 100);
}
```

- [ ] **Step 4: Add translation update/import/export endpoints**

Add:

```csharp
else if (context.Request.HttpMethod == "PATCH" && path == "/api/translations")
{
    var entry = await ReadJsonAsync<TranslationCacheEntry>(context.Request).ConfigureAwait(false);
    if (entry == null)
    {
        context.Response.StatusCode = 400;
        await WriteTextAsync(context.Response, "缺少翻译行数据").ConfigureAwait(false);
        return;
    }

    _cache.Update(entry);
    await WriteJsonAsync(context.Response, _cache.Query(new TranslationCacheQuery(null, "updated_utc", true, 0, 100))).ConfigureAwait(false);
}
else if (context.Request.HttpMethod == "GET" && path == "/api/translations/export")
{
    var format = context.Request.QueryString["format"] ?? "json";
    context.Response.ContentType = string.Equals(format, "csv", StringComparison.OrdinalIgnoreCase)
        ? "text/csv; charset=utf-8"
        : "application/json; charset=utf-8";
    await WriteTextAsync(context.Response, _cache.Export(format)).ConfigureAwait(false);
}
else if (context.Request.HttpMethod == "POST" && path == "/api/translations/import")
{
    var format = context.Request.QueryString["format"] ?? "json";
    var body = await ReadBodyAsync(context.Request).ConfigureAwait(false);
    await WriteJsonAsync(context.Response, _cache.Import(body, format)).ConfigureAwait(false);
}
```

- [ ] **Step 5: Add provider utility endpoints**

Add:

```csharp
else if (context.Request.HttpMethod == "GET" && path == "/api/provider/models")
{
    await WriteJsonAsync(context.Response, await _providerUtilityClient.FetchModelsAsync(_controlPanel.GetConfig().Provider, CancellationToken.None)).ConfigureAwait(false);
}
else if (context.Request.HttpMethod == "GET" && path == "/api/provider/balance")
{
    await WriteJsonAsync(context.Response, await _providerUtilityClient.FetchBalanceAsync(_controlPanel.GetConfig().Provider, CancellationToken.None)).ConfigureAwait(false);
}
else if (context.Request.HttpMethod == "POST" && path == "/api/provider/test")
{
    var result = await _providerUtilityClient.FetchModelsAsync(_controlPanel.GetConfig().Provider, CancellationToken.None).ConfigureAwait(false);
    _controlPanel.SetProviderStatus(new ProviderStatus(result.Succeeded ? "ok" : "error", result.Message, DateTimeOffset.UtcNow));
    await WriteJsonAsync(context.Response, result).ConfigureAwait(false);
}
```

- [ ] **Step 6: Update `Plugin.Awake` construction**

Create a `ProviderUtilityClient` using the worker host or server `HttpClient`. If it is owned by `LocalHttpServer`, instantiate it in the server constructor with a private `HttpClient` and dispose it in `LocalHttpServer.Dispose()`.

Update server creation:

```csharp
_httpServer = new LocalHttpServer(
    _controlPanel,
    _cache,
    _queue,
    _dispatcher,
    Logger);
```

- [ ] **Step 7: Build**

Run:

```powershell
dotnet build HUnityAutoTranslator.sln
```

Expected: build succeeds.

- [ ] **Step 8: Commit**

Run:

```powershell
git add src/HUnityAutoTranslator.Plugin/Web/LocalHttpServer.cs src/HUnityAutoTranslator.Plugin/Plugin.cs
git commit -m "feat: add control panel data endpoints"
```

## Task 7: Sidebar Single-Page Control Panel UI

**Files:**
- Modify: `src/HUnityAutoTranslator.Plugin/Web/ControlPanelHtml.cs`
- Verify: browser visual check with local static extraction or running plugin server.

- [ ] **Step 1: Replace the HTML document structure**

Use this top-level structure inside `ControlPanelHtml.Document`:

```html
<body>
  <div class="app-shell">
    <aside class="sidebar">
      <div class="brand">
        <strong>HUnityAutoTranslator</strong>
        <span>Local Control Panel</span>
      </div>
      <nav class="nav-list" aria-label="功能导航">
        <button class="nav-item active" data-page="status">状态</button>
        <button class="nav-item" data-page="plugin">插件设置</button>
        <button class="nav-item" data-page="ai">AI 翻译设置</button>
        <button class="nav-item" data-page="editor">文本编辑</button>
        <button class="nav-item" data-page="about">版本信息</button>
      </nav>
      <div class="sidebar-footer">
        <span id="status">正在连接...</span>
        <div class="theme-switch" role="group" aria-label="主题">
          <button data-theme-choice="system">系统</button>
          <button data-theme-choice="dark">深色</button>
          <button data-theme-choice="light">浅色</button>
        </div>
      </div>
    </aside>
    <main class="workspace">
      <section class="page active" id="page-status"></section>
      <section class="page" id="page-plugin"></section>
      <section class="page" id="page-ai"></section>
      <section class="page" id="page-editor"></section>
      <section class="page" id="page-about"></section>
    </main>
  </div>
</body>
```

Fill each section with the fields listed in the design spec. Keep all labels visible and compact.

- [ ] **Step 2: Add restrained app CSS**

Use:

```css
:root {
  color-scheme: dark;
  --bg: #0d0f12;
  --surface: #151a21;
  --surface-2: #1b222b;
  --field: #10151b;
  --line: #2b3440;
  --ink: #eef3f7;
  --muted: #9aa6b2;
  --accent: #0d9488;
  --accent-hover: #14b8a6;
  --warn: #f97316;
  --ok: #22c55e;
}
[data-theme="light"] {
  color-scheme: light;
  --bg: #f4f6f8;
  --surface: #ffffff;
  --surface-2: #f8fafc;
  --field: #ffffff;
  --line: #d8dee6;
  --ink: #17202a;
  --muted: #667085;
}
@media (prefers-color-scheme: light) {
  :root:not([data-theme="dark"]) {
    color-scheme: light;
    --bg: #f4f6f8;
    --surface: #ffffff;
    --surface-2: #f8fafc;
    --field: #ffffff;
    --line: #d8dee6;
    --ink: #17202a;
    --muted: #667085;
  }
}
```

Keep page sections as layout surfaces, not nested card stacks. Use 8px or smaller radii.

- [ ] **Step 3: Implement navigation and theme JavaScript**

Add:

```javascript
const $ = id => document.getElementById(id);
const $$ = selector => Array.from(document.querySelectorAll(selector));

function showPage(page) {
  $$(".nav-item").forEach(button => button.classList.toggle("active", button.dataset.page === page));
  $$(".page").forEach(section => section.classList.toggle("active", section.id === "page-" + page));
}

function applyTheme(choice) {
  localStorage.setItem("hunity.theme", choice);
  document.documentElement.dataset.theme = choice === "system" ? "" : choice;
  $$("[data-theme-choice]").forEach(button => button.classList.toggle("active", button.dataset.themeChoice === choice));
}

$$(".nav-item").forEach(button => button.addEventListener("click", () => showPage(button.dataset.page)));
$$("[data-theme-choice]").forEach(button => button.addEventListener("click", () => applyTheme(button.dataset.themeChoice)));
applyTheme(localStorage.getItem("hunity.theme") || "system");
```

- [ ] **Step 4: Implement state binding**

Update `applyState(state)` to write:

```javascript
setText("capturedTextCount", state.CapturedTextCount);
setText("queuedTextCount", state.QueueCount);
setText("inFlightTranslationCount", state.InFlightTranslationCount);
setText("completedTranslationCount", state.CompletedTranslationCount);
setText("writebackQueueCount", state.WritebackQueueCount);
setText("cacheCount", state.CacheCount);
setText("providerStatusText", state.ProviderStatus?.Message || "尚未检测");
renderRecentTranslations(state.RecentTranslations || []);
```

Only update form fields when the active element is not an input, select, or textarea.

- [ ] **Step 5: Implement plugin and AI forms**

Use one `readConfig()` that includes every expanded field. Numeric fields are parsed with:

```javascript
function numberValue(id) {
  const value = $(id).value.trim();
  return value === "" ? null : Number(value);
}
```

POST to `/api/config` with the same JSON property names as `UpdateConfigRequest`. Keep API key save on `/api/key`.

- [ ] **Step 6: Implement provider utility buttons**

Add click handlers:

```javascript
$("fetchModels").addEventListener("click", async () => {
  renderModels(await api("/api/provider/models"));
});
$("fetchBalance").addEventListener("click", async () => {
  renderBalance(await api("/api/provider/balance"));
});
$("testProvider").addEventListener("click", async () => {
  renderProviderTest(await api("/api/provider/test", { method: "POST", body: "{}" }));
  await refresh();
});
```

- [ ] **Step 7: Implement table load, sorting, filtering, editing, copy, and paste**

Maintain client state:

```javascript
const tableState = {
  search: "",
  sort: "updated_utc",
  direction: "desc",
  offset: 0,
  limit: 100,
  rows: [],
  selected: new Set(),
  dirty: new Map()
};
```

Fetch rows:

```javascript
async function loadTranslations() {
  const params = new URLSearchParams({
    search: tableState.search,
    sort: tableState.sort,
    direction: tableState.direction,
    offset: String(tableState.offset),
    limit: String(tableState.limit)
  });
  const page = await api("/api/translations?" + params.toString());
  tableState.rows = page.Items || [];
  renderTranslationTable(page);
}
```

Copy selected cells as TSV using `navigator.clipboard.writeText`. Paste TSV by splitting `\r?\n` and `\t`, writing into editable cells, and marking rows dirty. Editable columns are `TranslatedText`, `SceneName`, `ComponentHierarchy`, and `ComponentType`.

- [ ] **Step 8: Implement import/export UI**

For export buttons, navigate to:

```javascript
window.location.href = "/api/translations/export?format=json";
window.location.href = "/api/translations/export?format=csv";
```

For import, read a file with `FileReader`, POST raw text to `/api/translations/import?format=json` or `/api/translations/import?format=csv`, show imported count and errors, then reload the table.

- [ ] **Step 9: Implement about page placeholders**

Show static placeholder values:

```text
Plugin: HUnityAutoTranslator
Version: 0.1.0
Build channel: local
Runtime target: BepInEx 6 Unity Mono
Panel access: loopback only
```

- [ ] **Step 10: Build**

Run:

```powershell
dotnet build HUnityAutoTranslator.sln
```

Expected: build succeeds.

- [ ] **Step 11: Commit**

Run:

```powershell
git add src/HUnityAutoTranslator.Plugin/Web/ControlPanelHtml.cs
git commit -m "feat: redesign control panel interface"
```

## Task 8: Full Verification And Manual Browser Check

**Files:**
- Modify: `docs/manual-validation.md`

- [ ] **Step 1: Update manual validation checklist**

Add a section to `docs/manual-validation.md`:

```markdown
## Control Panel Redesign

- Open the localhost control panel URL from the plugin log.
- Confirm the sidebar contains 状态, 插件设置, AI 翻译设置, 文本编辑, 版本信息.
- Confirm 系统, 深色, 浅色 theme buttons switch without reloading.
- Confirm 状态 page refreshes counters without clearing focused input fields on another page.
- Confirm 插件设置 saves target language, capture switches, scan interval, and writeback limits.
- Confirm AI 翻译设置 saves provider, endpoint, model, reasoning effort, thinking mode, and custom instruction.
- Confirm 获取模型 and 查询余额 use the local backend and do not expose API keys in browser text.
- Confirm 文本编辑 can sort, filter, edit translated text, copy selected cells as TSV, and paste TSV into editable cells.
- Confirm JSON and CSV export downloads translation data without API keys.
- Confirm import rejects malformed data and imports a valid one-row JSON file.
```

- [ ] **Step 2: Run full tests**

Run:

```powershell
dotnet test HUnityAutoTranslator.sln
```

Expected: all tests pass.

- [ ] **Step 3: Build release artifacts**

Run:

```powershell
dotnet build HUnityAutoTranslator.sln -c Release
```

Expected: build succeeds.

- [ ] **Step 4: Inspect changed files**

Run:

```powershell
git diff --stat
git diff --check
```

Expected: no whitespace errors from `git diff --check`.

- [ ] **Step 5: Commit checklist update**

Run:

```powershell
git add docs/manual-validation.md
git commit -m "docs: add control panel validation checklist"
```

## Self-Review

- Spec coverage: every requested page is covered in Tasks 6 and 7; runtime status and recent translations are covered in Tasks 1 and 5; plugin settings and AI settings are covered in Task 2; database editing and import/export are covered in Task 3; provider model and balance utilities are covered in Task 4; theme selection is covered in Task 7; version placeholders are covered in Task 7.
- Placeholder scan: the plan does not use open-ended placeholders in code or validation steps.
- Type consistency: state fields, metrics types, cache DTO names, and provider utility DTO names are introduced before use in later tasks.

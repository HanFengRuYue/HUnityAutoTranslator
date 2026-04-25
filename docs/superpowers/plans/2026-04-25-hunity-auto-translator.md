# HUnityAutoTranslator Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a BepInEx 6 Bleeding Edge Unity Mono plugin that captures UGUI, IMGUI, and TextMeshPro text, translates it through OpenAI/DeepSeek/OpenAI-compatible AI providers, and controls runtime behavior through a localhost HTTP panel.

**Architecture:** Keep Unity-facing work thin and defensive. Put text normalization, prompt policy, caching, provider calls, worker pool scheduling, rate limiting, and result validation into testable pure C# projects; keep BepInEx, Harmony, Unity reflection, and main-thread writeback in the plugin shell.

**Tech Stack:** C#; plugin target `netstandard2.1`; tests target `net10.0`; xUnit; FluentAssertions; Newtonsoft.Json; BepInEx 6 Unity Mono template from official BE template package; HarmonyX from the BepInEx environment.

---

## File Structure

- `HUnityAutoTranslator.sln`: solution containing plugin, core library, and tests.
- `global.json`: pins SDK roll-forward behavior for repeatable local builds.
- `NuGet.config`: includes NuGet.org and BepInEx package sources.
- `Directory.Build.props`: shared nullable, language version, deterministic build, and warning settings.
- `src/HUnityAutoTranslator.Core/HUnityAutoTranslator.Core.csproj`: pure C# library with no Unity dependency.
- `src/HUnityAutoTranslator.Core/Text/`: normalization, filters, placeholder protection, rich text guards.
- `src/HUnityAutoTranslator.Core/Prompts/`: prompt construction and output validation.
- `src/HUnityAutoTranslator.Core/Configuration/`: runtime config models and immutable snapshots.
- `src/HUnityAutoTranslator.Core/Caching/`: memory and disk cache abstractions.
- `src/HUnityAutoTranslator.Core/Providers/`: provider contracts and OpenAI/DeepSeek/OpenAI-compatible request/response handling.
- `src/HUnityAutoTranslator.Core/Queueing/`: priority queue, batching, worker pool, provider rate limiter.
- `src/HUnityAutoTranslator.Core/Dispatching/`: translation result dispatcher contracts that the Unity plugin consumes.
- `src/HUnityAutoTranslator.Plugin/HUnityAutoTranslator.Plugin.csproj`: BepInEx Unity Mono plugin assembly.
- `src/HUnityAutoTranslator.Plugin/Plugin.cs`: BepInEx entry point.
- `src/HUnityAutoTranslator.Plugin/Capture/`: UGUI scanner, TMP reflection scanner, IMGUI Harmony hook installer.
- `src/HUnityAutoTranslator.Plugin/Unity/`: Unity main-thread adapters and text target wrappers.
- `src/HUnityAutoTranslator.Plugin/Web/`: embedded HTTP control panel static assets and local JSON API.
- `tests/HUnityAutoTranslator.Core.Tests/HUnityAutoTranslator.Core.Tests.csproj`: automated tests for all pure C# behavior.
- `tests/HUnityAutoTranslator.Core.Tests/`: unit tests grouped by module.
- `docs/manual-validation.md`: manual validation checklist for real Unity Mono games.

## Task 1: Bootstrap Solution And First Failing Test

**Files:**
- Create: `global.json`
- Create: `NuGet.config`
- Create: `Directory.Build.props`
- Create: `HUnityAutoTranslator.sln`
- Create: `src/HUnityAutoTranslator.Core/HUnityAutoTranslator.Core.csproj`
- Create: `tests/HUnityAutoTranslator.Core.Tests/HUnityAutoTranslator.Core.Tests.csproj`
- Test: `tests/HUnityAutoTranslator.Core.Tests/Text/TextNormalizerTests.cs`

- [ ] **Step 1: Create the solution and project scaffolding**

Run:

```powershell
dotnet new sln -n HUnityAutoTranslator
dotnet new classlib -n HUnityAutoTranslator.Core -o src/HUnityAutoTranslator.Core -f netstandard2.1
dotnet new xunit -n HUnityAutoTranslator.Core.Tests -o tests/HUnityAutoTranslator.Core.Tests -f net10.0
dotnet sln HUnityAutoTranslator.sln add src/HUnityAutoTranslator.Core/HUnityAutoTranslator.Core.csproj
dotnet sln HUnityAutoTranslator.sln add tests/HUnityAutoTranslator.Core.Tests/HUnityAutoTranslator.Core.Tests.csproj
dotnet add tests/HUnityAutoTranslator.Core.Tests/HUnityAutoTranslator.Core.Tests.csproj reference src/HUnityAutoTranslator.Core/HUnityAutoTranslator.Core.csproj
dotnet add tests/HUnityAutoTranslator.Core.Tests/HUnityAutoTranslator.Core.Tests.csproj package FluentAssertions
```

Expected: solution and two projects are created successfully.

- [ ] **Step 2: Add repository build settings**

Create `global.json`:

```json
{
  "sdk": {
    "version": "10.0.202",
    "rollForward": "latestFeature"
  }
}
```

Create `NuGet.config`:

```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
    <add key="BepInEx" value="https://nuget.bepinex.dev/v3/index.json" />
  </packageSources>
</configuration>
```

Create `Directory.Build.props`:

```xml
<Project>
  <PropertyGroup>
    <LangVersion>latest</LangVersion>
    <Nullable>enable</Nullable>
    <Deterministic>true</Deterministic>
    <TreatWarningsAsErrors>false</TreatWarningsAsErrors>
    <WarningsAsErrors>CS8600;CS8602;CS8603;CS8604</WarningsAsErrors>
  </PropertyGroup>
</Project>
```

- [ ] **Step 3: Write the failing normalization test**

Create `tests/HUnityAutoTranslator.Core.Tests/Text/TextNormalizerTests.cs`:

```csharp
using FluentAssertions;
using HUnityAutoTranslator.Core.Text;

namespace HUnityAutoTranslator.Core.Tests.Text;

public sealed class TextNormalizerTests
{
    [Theory]
    [InlineData("  Start\\r\\nGame  ", "Start\nGame")]
    [InlineData("Start\u00A0Game", "Start Game")]
    [InlineData("\tLevel   Up\t", "Level Up")]
    public void NormalizeForCache_collapses_nonsemantic_whitespace(string input, string expected)
    {
        TextNormalizer.NormalizeForCache(input).Should().Be(expected);
    }
}
```

- [ ] **Step 4: Run the test to verify it fails**

Run:

```powershell
dotnet test tests/HUnityAutoTranslator.Core.Tests/HUnityAutoTranslator.Core.Tests.csproj --filter TextNormalizerTests
```

Expected: FAIL because `HUnityAutoTranslator.Core.Text.TextNormalizer` does not exist.

- [ ] **Step 5: Implement minimal normalizer**

Create `src/HUnityAutoTranslator.Core/Text/TextNormalizer.cs`:

```csharp
using System.Text;

namespace HUnityAutoTranslator.Core.Text;

public static class TextNormalizer
{
    public static string NormalizeForCache(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = value.Replace("\r\n", "\n").Replace('\r', '\n').Replace('\u00A0', ' ');
        var builder = new StringBuilder(normalized.Length);
        var previousWasHorizontalSpace = false;

        foreach (var ch in normalized.Trim())
        {
            if (ch == '\n')
            {
                builder.Append('\n');
                previousWasHorizontalSpace = false;
                continue;
            }

            if (ch == ' ' || ch == '\t')
            {
                if (!previousWasHorizontalSpace)
                {
                    builder.Append(' ');
                    previousWasHorizontalSpace = true;
                }

                continue;
            }

            builder.Append(ch);
            previousWasHorizontalSpace = false;
        }

        return builder.ToString().Trim();
    }
}
```

- [ ] **Step 6: Run test to verify it passes**

Run:

```powershell
dotnet test tests/HUnityAutoTranslator.Core.Tests/HUnityAutoTranslator.Core.Tests.csproj --filter TextNormalizerTests
```

Expected: PASS.

- [ ] **Step 7: Commit**

Run:

```powershell
git add global.json NuGet.config Directory.Build.props HUnityAutoTranslator.sln src/HUnityAutoTranslator.Core tests/HUnityAutoTranslator.Core.Tests
git commit -m "build: scaffold core library and tests"
```

## Task 2: Text Safety Guards And Prompt Policy

**Files:**
- Create: `src/HUnityAutoTranslator.Core/Text/TextFilter.cs`
- Create: `src/HUnityAutoTranslator.Core/Text/ProtectedText.cs`
- Create: `src/HUnityAutoTranslator.Core/Text/PlaceholderProtector.cs`
- Create: `src/HUnityAutoTranslator.Core/Text/RichTextGuard.cs`
- Create: `src/HUnityAutoTranslator.Core/Prompts/TranslationStyle.cs`
- Create: `src/HUnityAutoTranslator.Core/Prompts/PromptOptions.cs`
- Create: `src/HUnityAutoTranslator.Core/Prompts/PromptBuilder.cs`
- Create: `src/HUnityAutoTranslator.Core/Prompts/TranslationOutputValidator.cs`
- Test: `tests/HUnityAutoTranslator.Core.Tests/Text/TextSafetyTests.cs`
- Test: `tests/HUnityAutoTranslator.Core.Tests/Prompts/PromptPolicyTests.cs`

- [ ] **Step 1: Write failing safety tests**

Create `tests/HUnityAutoTranslator.Core.Tests/Text/TextSafetyTests.cs`:

```csharp
using FluentAssertions;
using HUnityAutoTranslator.Core.Text;

namespace HUnityAutoTranslator.Core.Tests.Text;

public sealed class TextSafetyTests
{
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("12345")]
    [InlineData("!!!")]
    public void ShouldTranslate_rejects_nonsemantic_text(string value)
    {
        TextFilter.ShouldTranslate(value).Should().BeFalse();
    }

    [Fact]
    public void PlaceholderProtector_preserves_format_tokens()
    {
        var protectedText = PlaceholderProtector.Protect("Hello {playerName}, you have {0} coins and %s gems.");

        protectedText.Text.Should().Contain("__HUT_TOKEN_0__");
        protectedText.Text.Should().Contain("__HUT_TOKEN_1__");
        protectedText.Text.Should().Contain("__HUT_TOKEN_2__");
        protectedText.Restore("你好 __HUT_TOKEN_0__，你有 __HUT_TOKEN_1__ 枚金币和 __HUT_TOKEN_2__ 颗宝石。")
            .Should().Be("你好 {playerName}，你有 {0} 枚金币和 %s 颗宝石。");
    }

    [Fact]
    public void RichTextGuard_detects_broken_tags()
    {
        RichTextGuard.HasSameTags("<color=red>Start</color>", "<color=red>开始</color>").Should().BeTrue();
        RichTextGuard.HasSameTags("<color=red>Start</color>", "<color=red>开始").Should().BeFalse();
    }
}
```

- [ ] **Step 2: Write failing prompt policy tests**

Create `tests/HUnityAutoTranslator.Core.Tests/Prompts/PromptPolicyTests.cs`:

```csharp
using FluentAssertions;
using HUnityAutoTranslator.Core.Prompts;

namespace HUnityAutoTranslator.Core.Tests.Prompts;

public sealed class PromptPolicyTests
{
    [Fact]
    public void BuildSystemPrompt_contains_hard_rules_and_style()
    {
        var prompt = PromptBuilder.BuildSystemPrompt(new PromptOptions("zh-Hans", TranslationStyle.Localized, "保持角色口吻"));

        prompt.Should().Contain("只输出译文");
        prompt.Should().Contain("不要解释");
        prompt.Should().Contain("不要改变占位符");
        prompt.Should().Contain("允许自然本地化");
        prompt.Should().Contain("保持角色口吻");
    }

    [Fact]
    public void Validator_rejects_explanatory_prefix_and_broken_placeholders()
    {
        var result = TranslationOutputValidator.ValidateSingle(
            "You have {0} coins.",
            "翻译如下：你有 0 枚金币。",
            requireSameRichTextTags: true);

        result.IsValid.Should().BeFalse();
        result.Reason.Should().Contain("解释性前缀");
    }
}
```

- [ ] **Step 3: Run tests to verify they fail**

Run:

```powershell
dotnet test tests/HUnityAutoTranslator.Core.Tests/HUnityAutoTranslator.Core.Tests.csproj --filter "TextSafetyTests|PromptPolicyTests"
```

Expected: FAIL because safety and prompt classes do not exist.

- [ ] **Step 4: Implement text safety classes**

Implement the following public API:

```csharp
namespace HUnityAutoTranslator.Core.Text;

public static class TextFilter
{
    public static bool ShouldTranslate(string? value);
}

public sealed record ProtectedText(string Text, IReadOnlyDictionary<string, string> Tokens)
{
    public string Restore(string translatedText);
}

public static class PlaceholderProtector
{
    public static ProtectedText Protect(string value);
    public static IReadOnlyList<string> ExtractPlaceholders(string value);
}

public static class RichTextGuard
{
    public static bool HasSameTags(string source, string translated);
    public static IReadOnlyList<string> ExtractTags(string value);
}
```

Rules:

- `TextFilter.ShouldTranslate` returns false for null, whitespace, pure numbers, pure punctuation, and strings shorter than two non-symbol characters.
- `PlaceholderProtector` protects `{name}`, `{0}`, `%s`, `%d`, `%0.2f`, `\n`, `\t`, and literal `\\n`.
- `RichTextGuard` recognizes simple `<tag>`, `</tag>`, and `<tag=value>` sequences and compares normalized tag name order.

- [ ] **Step 5: Implement prompt policy classes**

Implement the following public API:

```csharp
namespace HUnityAutoTranslator.Core.Prompts;

public enum TranslationStyle
{
    Faithful,
    Natural,
    Localized,
    UiConcise
}

public sealed record PromptOptions(string TargetLanguage, TranslationStyle Style, string? CustomInstruction);

public sealed record ValidationResult(bool IsValid, string Reason)
{
    public static ValidationResult Valid();
    public static ValidationResult Invalid(string reason);
}

public static class PromptBuilder
{
    public static string BuildSystemPrompt(PromptOptions options);
    public static string BuildSingleUserPrompt(string protectedText);
    public static string BuildBatchUserPrompt(IReadOnlyList<string> protectedTexts);
    public static string BuildRepairPrompt(string sourceText, string invalidTranslation, string reason);
}

public static class TranslationOutputValidator
{
    public static ValidationResult ValidateSingle(string sourceText, string translatedText, bool requireSameRichTextTags);
    public static ValidationResult ValidateBatch(IReadOnlyList<string> sourceTexts, IReadOnlyList<string> translatedTexts);
}
```

Prompt content must include hard rules in Chinese:

```text
你是游戏本地化翻译引擎。只输出译文，不要解释，不要寒暄，不要添加引号、Markdown 或“翻译如下”等前缀。
不要改变占位符、控制符、换行符、Unity 富文本标签或 TextMeshPro 标签。
允许自然本地化，避免机器翻译腔；菜单和按钮要短，对话要符合角色口吻。
```

- [ ] **Step 6: Run tests to verify they pass**

Run:

```powershell
dotnet test tests/HUnityAutoTranslator.Core.Tests/HUnityAutoTranslator.Core.Tests.csproj --filter "TextSafetyTests|PromptPolicyTests"
```

Expected: PASS.

- [ ] **Step 7: Commit**

Run:

```powershell
git add src/HUnityAutoTranslator.Core tests/HUnityAutoTranslator.Core.Tests
git commit -m "feat: add text safety and prompt policy"
```

## Task 3: Runtime Configuration And Cache Keys

**Files:**
- Create: `src/HUnityAutoTranslator.Core/Configuration/ProviderKind.cs`
- Create: `src/HUnityAutoTranslator.Core/Configuration/ProviderProfile.cs`
- Create: `src/HUnityAutoTranslator.Core/Configuration/RuntimeConfig.cs`
- Create: `src/HUnityAutoTranslator.Core/Caching/TranslationCacheKey.cs`
- Create: `src/HUnityAutoTranslator.Core/Caching/ITranslationCache.cs`
- Create: `src/HUnityAutoTranslator.Core/Caching/MemoryTranslationCache.cs`
- Create: `src/HUnityAutoTranslator.Core/Caching/DiskTranslationCache.cs`
- Test: `tests/HUnityAutoTranslator.Core.Tests/Configuration/RuntimeConfigTests.cs`
- Test: `tests/HUnityAutoTranslator.Core.Tests/Caching/TranslationCacheTests.cs`

- [ ] **Step 1: Write failing config and cache tests**

Create `tests/HUnityAutoTranslator.Core.Tests/Configuration/RuntimeConfigTests.cs`:

```csharp
using FluentAssertions;
using HUnityAutoTranslator.Core.Configuration;
using HUnityAutoTranslator.Core.Prompts;

namespace HUnityAutoTranslator.Core.Tests.Configuration;

public sealed class RuntimeConfigTests
{
    [Fact]
    public void DefaultConfig_is_localhost_and_zhHans()
    {
        var config = RuntimeConfig.CreateDefault();

        config.TargetLanguage.Should().Be("zh-Hans");
        config.HttpHost.Should().Be("127.0.0.1");
        config.Provider.Kind.Should().Be(ProviderKind.OpenAI);
        config.Style.Should().Be(TranslationStyle.Localized);
        config.MaxConcurrentRequests.Should().BeGreaterThan(1);
    }
}
```

Create `tests/HUnityAutoTranslator.Core.Tests/Caching/TranslationCacheTests.cs`:

```csharp
using FluentAssertions;
using HUnityAutoTranslator.Core.Caching;
using HUnityAutoTranslator.Core.Configuration;

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
    public void MemoryCache_roundtrips_translation()
    {
        var cache = new MemoryTranslationCache();
        var key = TranslationCacheKey.Create("Start Game", "zh-Hans", ProviderProfile.DefaultOpenAi(), "prompt-v1");

        cache.TryGet(key, out _).Should().BeFalse();
        cache.Set(key, "开始游戏");
        cache.TryGet(key, out var translated).Should().BeTrue();
        translated.Should().Be("开始游戏");
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run:

```powershell
dotnet test tests/HUnityAutoTranslator.Core.Tests/HUnityAutoTranslator.Core.Tests.csproj --filter "RuntimeConfigTests|TranslationCacheTests"
```

Expected: FAIL because configuration and cache classes do not exist.

- [ ] **Step 3: Implement configuration and cache contracts**

Implement public API:

```csharp
namespace HUnityAutoTranslator.Core.Configuration;

public enum ProviderKind { OpenAI, DeepSeek, OpenAICompatible }

public sealed record ProviderProfile(
    ProviderKind Kind,
    string BaseUrl,
    string Endpoint,
    string Model,
    bool ApiKeyConfigured)
{
    public static ProviderProfile DefaultOpenAi();
    public static ProviderProfile DefaultDeepSeek();
}

public sealed record RuntimeConfig(
    bool Enabled,
    string TargetLanguage,
    TranslationStyle Style,
    ProviderProfile Provider,
    string HttpHost,
    int HttpPort,
    int MaxConcurrentRequests,
    int RequestsPerMinute,
    int MaxBatchCharacters,
    TimeSpan ScanInterval,
    int MaxScanTargetsPerTick,
    int MaxWritebacksPerFrame,
    bool EnableUgui,
    bool EnableTmp,
    bool EnableImgui)
{
    public static RuntimeConfig CreateDefault();
}
```

```csharp
namespace HUnityAutoTranslator.Core.Caching;

public sealed record TranslationCacheKey(string Value)
{
    public static TranslationCacheKey Create(string sourceText, string targetLanguage, ProviderProfile provider, string promptPolicyVersion);
}

public interface ITranslationCache
{
    bool TryGet(TranslationCacheKey key, out string translatedText);
    void Set(TranslationCacheKey key, string translatedText);
    int Count { get; }
}

public sealed class MemoryTranslationCache : ITranslationCache { }
public sealed class DiskTranslationCache : ITranslationCache, IDisposable { }
```

Defaults:

- OpenAI provider: `https://api.openai.com`, `/v1/responses`, `gpt-5.5`.
- DeepSeek provider: `https://api.deepseek.com`, `/chat/completions`, `deepseek-v4-flash`.
- `MaxConcurrentRequests`: 4.
- `RequestsPerMinute`: 60.
- `MaxBatchCharacters`: 1800.
- `MaxWritebacksPerFrame`: 32.

- [ ] **Step 4: Run tests to verify they pass**

Run:

```powershell
dotnet test tests/HUnityAutoTranslator.Core.Tests/HUnityAutoTranslator.Core.Tests.csproj --filter "RuntimeConfigTests|TranslationCacheTests"
```

Expected: PASS.

- [ ] **Step 5: Commit**

Run:

```powershell
git add src/HUnityAutoTranslator.Core tests/HUnityAutoTranslator.Core.Tests
git commit -m "feat: add runtime config and translation cache"
```

## Task 4: Provider Request Builders And Response Parsers

**Files:**
- Modify: `src/HUnityAutoTranslator.Core/HUnityAutoTranslator.Core.csproj`
- Create: `src/HUnityAutoTranslator.Core/Providers/TranslationRequest.cs`
- Create: `src/HUnityAutoTranslator.Core/Providers/TranslationResponse.cs`
- Create: `src/HUnityAutoTranslator.Core/Providers/ITranslationProvider.cs`
- Create: `src/HUnityAutoTranslator.Core/Providers/OpenAiResponsesProvider.cs`
- Create: `src/HUnityAutoTranslator.Core/Providers/ChatCompletionsProvider.cs`
- Create: `src/HUnityAutoTranslator.Core/Providers/ProviderJsonParsers.cs`
- Test: `tests/HUnityAutoTranslator.Core.Tests/Providers/ProviderParserTests.cs`

- [ ] **Step 1: Add JSON dependency**

Run:

```powershell
dotnet add src/HUnityAutoTranslator.Core/HUnityAutoTranslator.Core.csproj package Newtonsoft.Json
```

Expected: package reference is added.

- [ ] **Step 2: Write failing provider parser tests**

Create `tests/HUnityAutoTranslator.Core.Tests/Providers/ProviderParserTests.cs`:

```csharp
using FluentAssertions;
using HUnityAutoTranslator.Core.Providers;

namespace HUnityAutoTranslator.Core.Tests.Providers;

public sealed class ProviderParserTests
{
    [Fact]
    public void OpenAiResponsesParser_reads_output_text_items()
    {
        const string json = """
        {
          "output": [
            {
              "type": "message",
              "content": [
                { "type": "output_text", "text": "开始游戏" }
              ]
            }
          ]
        }
        """;

        ProviderJsonParsers.ParseOpenAiResponsesText(json).Should().Be("开始游戏");
    }

    [Fact]
    public void ChatCompletionsParser_reads_choice_message_content()
    {
        const string json = """
        {
          "choices": [
            { "message": { "role": "assistant", "content": "开始游戏" } }
          ]
        }
        """;

        ProviderJsonParsers.ParseChatCompletionsText(json).Should().Be("开始游戏");
    }
}
```

- [ ] **Step 3: Run tests to verify they fail**

Run:

```powershell
dotnet test tests/HUnityAutoTranslator.Core.Tests/HUnityAutoTranslator.Core.Tests.csproj --filter ProviderParserTests
```

Expected: FAIL because provider classes do not exist.

- [ ] **Step 4: Implement provider contracts and parsers**

Implement public API:

```csharp
namespace HUnityAutoTranslator.Core.Providers;

public sealed record TranslationRequest(
    IReadOnlyList<string> ProtectedTexts,
    string TargetLanguage,
    string SystemPrompt,
    string UserPrompt);

public sealed record TranslationResponse(
    bool Succeeded,
    IReadOnlyList<string> TranslatedTexts,
    string? ErrorMessage)
{
    public static TranslationResponse Success(IReadOnlyList<string> translatedTexts);
    public static TranslationResponse Failure(string errorMessage);
}

public interface ITranslationProvider
{
    ProviderKind Kind { get; }
    Task<TranslationResponse> TranslateAsync(TranslationRequest request, CancellationToken cancellationToken);
}

public static class ProviderJsonParsers
{
    public static string ParseOpenAiResponsesText(string json);
    public static string ParseChatCompletionsText(string json);
}
```

`OpenAiResponsesProvider` must POST to `{BaseUrl}{Endpoint}` with `model`, `instructions`, and `input`. `ChatCompletionsProvider` must POST with `model` and `messages`. Both classes must accept an injected `HttpClient`, `ProviderProfile`, and API key provider delegate so tests can later use fake handlers.

- [ ] **Step 5: Run tests to verify they pass**

Run:

```powershell
dotnet test tests/HUnityAutoTranslator.Core.Tests/HUnityAutoTranslator.Core.Tests.csproj --filter ProviderParserTests
```

Expected: PASS.

- [ ] **Step 6: Commit**

Run:

```powershell
git add src/HUnityAutoTranslator.Core tests/HUnityAutoTranslator.Core.Tests
git commit -m "feat: add translation provider parsers"
```

## Task 5: Priority Queue, Rate Limiter, Worker Pool, And Result Dispatcher

**Files:**
- Create: `src/HUnityAutoTranslator.Core/Queueing/TranslationJob.cs`
- Create: `src/HUnityAutoTranslator.Core/Queueing/TranslationPriority.cs`
- Create: `src/HUnityAutoTranslator.Core/Queueing/TranslationJobQueue.cs`
- Create: `src/HUnityAutoTranslator.Core/Queueing/ProviderRateLimiter.cs`
- Create: `src/HUnityAutoTranslator.Core/Queueing/TranslationWorkerPool.cs`
- Create: `src/HUnityAutoTranslator.Core/Dispatching/TranslationResult.cs`
- Create: `src/HUnityAutoTranslator.Core/Dispatching/ResultDispatcher.cs`
- Test: `tests/HUnityAutoTranslator.Core.Tests/Queueing/TranslationQueueTests.cs`
- Test: `tests/HUnityAutoTranslator.Core.Tests/Queueing/WorkerPoolTests.cs`
- Test: `tests/HUnityAutoTranslator.Core.Tests/Dispatching/ResultDispatcherTests.cs`

- [ ] **Step 1: Write failing queue and dispatcher tests**

Create `tests/HUnityAutoTranslator.Core.Tests/Queueing/TranslationQueueTests.cs`:

```csharp
using FluentAssertions;
using HUnityAutoTranslator.Core.Queueing;

namespace HUnityAutoTranslator.Core.Tests.Queueing;

public sealed class TranslationQueueTests
{
    [Fact]
    public void Queue_prioritizes_visible_short_ui_text()
    {
        var queue = new TranslationJobQueue();
        queue.Enqueue(TranslationJob.Create("lore", "Long lore text", TranslationPriority.Normal));
        queue.Enqueue(TranslationJob.Create("button", "Start", TranslationPriority.VisibleUi));

        queue.TryDequeueBatch(10, 2000, out var batch).Should().BeTrue();
        batch[0].Id.Should().Be("button");
    }

    [Fact]
    public void Queue_deduplicates_inflight_source_text()
    {
        var queue = new TranslationJobQueue();
        queue.Enqueue(TranslationJob.Create("a", "Start", TranslationPriority.Normal));
        queue.Enqueue(TranslationJob.Create("b", "Start", TranslationPriority.VisibleUi));

        queue.PendingCount.Should().Be(1);
    }
}
```

Create `tests/HUnityAutoTranslator.Core.Tests/Dispatching/ResultDispatcherTests.cs`:

```csharp
using FluentAssertions;
using HUnityAutoTranslator.Core.Dispatching;

namespace HUnityAutoTranslator.Core.Tests.Dispatching;

public sealed class ResultDispatcherTests
{
    [Fact]
    public void Drain_returns_high_priority_results_first_with_budget()
    {
        var dispatcher = new ResultDispatcher();
        dispatcher.Publish(new TranslationResult("normal", "原文", "译文", priority: 0));
        dispatcher.Publish(new TranslationResult("visible", "Start", "开始", priority: 100));

        var drained = dispatcher.Drain(maxCount: 1);

        drained.Should().ContainSingle();
        drained[0].TargetId.Should().Be("visible");
        dispatcher.PendingCount.Should().Be(1);
    }
}
```

- [ ] **Step 2: Write failing worker pool test**

Create `tests/HUnityAutoTranslator.Core.Tests/Queueing/WorkerPoolTests.cs`:

```csharp
using FluentAssertions;
using HUnityAutoTranslator.Core.Configuration;
using HUnityAutoTranslator.Core.Dispatching;
using HUnityAutoTranslator.Core.Providers;
using HUnityAutoTranslator.Core.Queueing;

namespace HUnityAutoTranslator.Core.Tests.Queueing;

public sealed class WorkerPoolTests
{
    [Fact]
    public async Task WorkerPool_runs_multiple_batches_concurrently()
    {
        var queue = new TranslationJobQueue();
        var dispatcher = new ResultDispatcher();
        var provider = new DelayedProvider(TimeSpan.FromMilliseconds(150));
        var limiter = new ProviderRateLimiter(requestsPerMinute: 120);
        var pool = new TranslationWorkerPool(queue, dispatcher, provider, limiter, RuntimeConfig.CreateDefault() with { MaxConcurrentRequests = 3 });

        queue.Enqueue(TranslationJob.Create("a", "A", TranslationPriority.Normal));
        queue.Enqueue(TranslationJob.Create("b", "B", TranslationPriority.Normal));
        queue.Enqueue(TranslationJob.Create("c", "C", TranslationPriority.Normal));

        await pool.RunUntilIdleAsync(CancellationToken.None);

        dispatcher.PendingCount.Should().Be(3);
        provider.MaxObservedConcurrency.Should().BeGreaterThan(1);
    }

    private sealed class DelayedProvider : ITranslationProvider
    {
        private int _current;
        private readonly TimeSpan _delay;
        public int MaxObservedConcurrency { get; private set; }
        public ProviderKind Kind => ProviderKind.OpenAI;

        public DelayedProvider(TimeSpan delay) => _delay = delay;

        public async Task<TranslationResponse> TranslateAsync(TranslationRequest request, CancellationToken cancellationToken)
        {
            var current = Interlocked.Increment(ref _current);
            MaxObservedConcurrency = Math.Max(MaxObservedConcurrency, current);
            await Task.Delay(_delay, cancellationToken);
            Interlocked.Decrement(ref _current);
            return TranslationResponse.Success(request.ProtectedTexts.Select(x => "T:" + x).ToArray());
        }
    }
}
```

- [ ] **Step 3: Run tests to verify they fail**

Run:

```powershell
dotnet test tests/HUnityAutoTranslator.Core.Tests/HUnityAutoTranslator.Core.Tests.csproj --filter "TranslationQueueTests|WorkerPoolTests|ResultDispatcherTests"
```

Expected: FAIL because queueing and dispatcher classes do not exist.

- [ ] **Step 4: Implement queue, limiter, worker pool, dispatcher**

Implement public API:

```csharp
namespace HUnityAutoTranslator.Core.Queueing;

public enum TranslationPriority { Normal = 0, VisibleUi = 100 }

public sealed record TranslationJob(string Id, string SourceText, TranslationPriority Priority)
{
    public static TranslationJob Create(string id, string sourceText, TranslationPriority priority);
}

public sealed class TranslationJobQueue
{
    public int PendingCount { get; }
    public void Enqueue(TranslationJob job);
    public bool TryDequeueBatch(int maxItems, int maxCharacters, out IReadOnlyList<TranslationJob> batch);
    public void MarkCompleted(IEnumerable<TranslationJob> jobs);
}

public sealed class ProviderRateLimiter
{
    public ProviderRateLimiter(int requestsPerMinute);
    public Task WaitAsync(CancellationToken cancellationToken);
}

public sealed class TranslationWorkerPool
{
    public TranslationWorkerPool(
        TranslationJobQueue queue,
        ResultDispatcher dispatcher,
        ITranslationProvider provider,
        ProviderRateLimiter limiter,
        RuntimeConfig config);

    public Task RunUntilIdleAsync(CancellationToken cancellationToken);
}
```

```csharp
namespace HUnityAutoTranslator.Core.Dispatching;

public sealed record TranslationResult(string TargetId, string SourceText, string TranslatedText, int Priority);

public sealed class ResultDispatcher
{
    public int PendingCount { get; }
    public void Publish(TranslationResult result);
    public IReadOnlyList<TranslationResult> Drain(int maxCount);
}
```

Implementation notes:

- Queue methods must be thread-safe.
- Worker pool must start up to `RuntimeConfig.MaxConcurrentRequests` tasks.
- Worker pool must call the limiter before each provider request.
- Dispatcher `Drain` must return highest priority first.
- Worker pool must publish results immediately after each provider response.

- [ ] **Step 5: Run tests to verify they pass**

Run:

```powershell
dotnet test tests/HUnityAutoTranslator.Core.Tests/HUnityAutoTranslator.Core.Tests.csproj --filter "TranslationQueueTests|WorkerPoolTests|ResultDispatcherTests"
```

Expected: PASS.

- [ ] **Step 6: Commit**

Run:

```powershell
git add src/HUnityAutoTranslator.Core tests/HUnityAutoTranslator.Core.Tests
git commit -m "feat: add concurrent translation queue"
```

## Task 6: Text Pipeline Integration

**Files:**
- Create: `src/HUnityAutoTranslator.Core/Pipeline/CapturedText.cs`
- Create: `src/HUnityAutoTranslator.Core/Pipeline/TextPipeline.cs`
- Create: `src/HUnityAutoTranslator.Core/Pipeline/PipelineDecision.cs`
- Test: `tests/HUnityAutoTranslator.Core.Tests/Pipeline/TextPipelineTests.cs`

- [ ] **Step 1: Write failing pipeline tests**

Create `tests/HUnityAutoTranslator.Core.Tests/Pipeline/TextPipelineTests.cs`:

```csharp
using FluentAssertions;
using HUnityAutoTranslator.Core.Caching;
using HUnityAutoTranslator.Core.Configuration;
using HUnityAutoTranslator.Core.Pipeline;
using HUnityAutoTranslator.Core.Queueing;

namespace HUnityAutoTranslator.Core.Tests.Pipeline;

public sealed class TextPipelineTests
{
    [Fact]
    public void Process_returns_cached_translation_without_queueing()
    {
        var cache = new MemoryTranslationCache();
        var queue = new TranslationJobQueue();
        var config = RuntimeConfig.CreateDefault();
        var key = TranslationCacheKey.Create("Start Game", config.TargetLanguage, config.Provider, TextPipeline.PromptPolicyVersion);
        cache.Set(key, "开始游戏");
        var pipeline = new TextPipeline(cache, queue, config);

        var decision = pipeline.Process(new CapturedText("ui-1", "Start Game", isVisible: true));

        decision.Kind.Should().Be(PipelineDecisionKind.UseCachedTranslation);
        decision.TranslatedText.Should().Be("开始游戏");
        queue.PendingCount.Should().Be(0);
    }

    [Fact]
    public void Process_queues_uncached_visible_text_with_high_priority()
    {
        var queue = new TranslationJobQueue();
        var pipeline = new TextPipeline(new MemoryTranslationCache(), queue, RuntimeConfig.CreateDefault());

        var decision = pipeline.Process(new CapturedText("ui-2", "Options", isVisible: true));

        decision.Kind.Should().Be(PipelineDecisionKind.Queued);
        queue.TryDequeueBatch(1, 100, out var batch).Should().BeTrue();
        batch[0].Priority.Should().Be(TranslationPriority.VisibleUi);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run:

```powershell
dotnet test tests/HUnityAutoTranslator.Core.Tests/HUnityAutoTranslator.Core.Tests.csproj --filter TextPipelineTests
```

Expected: FAIL because pipeline classes do not exist.

- [ ] **Step 3: Implement text pipeline**

Implement public API:

```csharp
namespace HUnityAutoTranslator.Core.Pipeline;

public sealed record CapturedText(string TargetId, string SourceText, bool IsVisible);

public enum PipelineDecisionKind
{
    Ignored,
    UseCachedTranslation,
    Queued
}

public sealed record PipelineDecision(PipelineDecisionKind Kind, string? TranslatedText)
{
    public static PipelineDecision Ignored();
    public static PipelineDecision UseCachedTranslation(string translatedText);
    public static PipelineDecision Queued();
}

public sealed class TextPipeline
{
    public const string PromptPolicyVersion = "prompt-v1";
    public TextPipeline(ITranslationCache cache, TranslationJobQueue queue, RuntimeConfig config);
    public PipelineDecision Process(CapturedText capturedText);
}
```

Behavior:

- Use `TextFilter.ShouldTranslate`.
- Normalize source text before cache key creation.
- Use `RuntimeConfig.TargetLanguage`, provider profile, and prompt policy version in the cache key.
- If cache hits, return `UseCachedTranslation`.
- If cache misses, enqueue with `VisibleUi` priority when `CapturedText.IsVisible` is true.

- [ ] **Step 4: Run tests to verify they pass**

Run:

```powershell
dotnet test tests/HUnityAutoTranslator.Core.Tests/HUnityAutoTranslator.Core.Tests.csproj --filter TextPipelineTests
```

Expected: PASS.

- [ ] **Step 5: Commit**

Run:

```powershell
git add src/HUnityAutoTranslator.Core tests/HUnityAutoTranslator.Core.Tests
git commit -m "feat: integrate text pipeline"
```

## Task 7: HTTP Control Panel Core API

**Files:**
- Create: `src/HUnityAutoTranslator.Core/Control/ControlPanelState.cs`
- Create: `src/HUnityAutoTranslator.Core/Control/ControlPanelService.cs`
- Create: `src/HUnityAutoTranslator.Core/Control/UpdateConfigRequest.cs`
- Test: `tests/HUnityAutoTranslator.Core.Tests/Control/ControlPanelServiceTests.cs`

- [ ] **Step 1: Write failing control service tests**

Create `tests/HUnityAutoTranslator.Core.Tests/Control/ControlPanelServiceTests.cs`:

```csharp
using FluentAssertions;
using HUnityAutoTranslator.Core.Control;

namespace HUnityAutoTranslator.Core.Tests.Control;

public sealed class ControlPanelServiceTests
{
    [Fact]
    public void Snapshot_masks_api_key_and_reports_runtime_status()
    {
        var service = ControlPanelService.CreateDefault();
        service.SetApiKey("secret-value");

        var state = service.GetState();

        state.ApiKeyConfigured.Should().BeTrue();
        state.ApiKeyPreview.Should().BeNull();
        state.TargetLanguage.Should().Be("zh-Hans");
    }

    [Fact]
    public void UpdateConfig_changes_target_language_and_concurrency()
    {
        var service = ControlPanelService.CreateDefault();

        service.UpdateConfig(new UpdateConfigRequest(TargetLanguage: "ja", MaxConcurrentRequests: 8, RequestsPerMinute: 90));

        var state = service.GetState();
        state.TargetLanguage.Should().Be("ja");
        state.MaxConcurrentRequests.Should().Be(8);
        state.RequestsPerMinute.Should().Be(90);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run:

```powershell
dotnet test tests/HUnityAutoTranslator.Core.Tests/HUnityAutoTranslator.Core.Tests.csproj --filter ControlPanelServiceTests
```

Expected: FAIL because control service classes do not exist.

- [ ] **Step 3: Implement control service**

Implement public API:

```csharp
namespace HUnityAutoTranslator.Core.Control;

public sealed record ControlPanelState(
    bool Enabled,
    string TargetLanguage,
    ProviderKind ProviderKind,
    string Model,
    bool ApiKeyConfigured,
    string? ApiKeyPreview,
    int QueueCount,
    int CacheCount,
    int MaxConcurrentRequests,
    int RequestsPerMinute,
    string? LastError);

public sealed record UpdateConfigRequest(
    string? TargetLanguage,
    int? MaxConcurrentRequests,
    int? RequestsPerMinute);

public sealed class ControlPanelService
{
    public static ControlPanelService CreateDefault();
    public ControlPanelState GetState();
    public RuntimeConfig GetConfig();
    public void UpdateConfig(UpdateConfigRequest request);
    public void SetApiKey(string apiKey);
}
```

Rules:

- Never expose API key text in `ControlPanelState`.
- Clamp concurrency to 1 through 16.
- Clamp requests per minute to 1 through 600.
- Reject blank target language by leaving the current value unchanged.

- [ ] **Step 4: Run tests to verify they pass**

Run:

```powershell
dotnet test tests/HUnityAutoTranslator.Core.Tests/HUnityAutoTranslator.Core.Tests.csproj --filter ControlPanelServiceTests
```

Expected: PASS.

- [ ] **Step 5: Commit**

Run:

```powershell
git add src/HUnityAutoTranslator.Core tests/HUnityAutoTranslator.Core.Tests
git commit -m "feat: add control panel state service"
```

## Task 8: BepInEx Plugin Shell And Unity Adapters

**Files:**
- Create: `src/HUnityAutoTranslator.Plugin/HUnityAutoTranslator.Plugin.csproj`
- Create: `src/HUnityAutoTranslator.Plugin/Plugin.cs`
- Create: `src/HUnityAutoTranslator.Plugin/MyPluginInfo.cs`
- Create: `src/HUnityAutoTranslator.Plugin/Unity/IUnityTextTarget.cs`
- Create: `src/HUnityAutoTranslator.Plugin/Unity/UnityMainThreadResultApplier.cs`
- Create: `src/HUnityAutoTranslator.Plugin/Web/LocalHttpServer.cs`
- Modify: `HUnityAutoTranslator.sln`
- Test: build-level verification only in this task.

- [ ] **Step 1: Install official BepInEx BE templates if missing**

Run:

```powershell
dotnet new install BepInEx.Templates::2.0.0-be.4 --nuget-source https://nuget.bepinex.dev/v3/index.json
```

Expected: `bep6plugin_unity_mono` template is installed or already installed.

- [ ] **Step 2: Create plugin project**

Run:

```powershell
dotnet new bep6plugin_unity_mono -n HUnityAutoTranslator.Plugin -o src/HUnityAutoTranslator.Plugin -T netstandard2.1 -U 2021.3.0
dotnet sln HUnityAutoTranslator.sln add src/HUnityAutoTranslator.Plugin/HUnityAutoTranslator.Plugin.csproj
dotnet add src/HUnityAutoTranslator.Plugin/HUnityAutoTranslator.Plugin.csproj reference src/HUnityAutoTranslator.Core/HUnityAutoTranslator.Core.csproj
dotnet add src/HUnityAutoTranslator.Plugin/HUnityAutoTranslator.Plugin.csproj package Newtonsoft.Json
```

Expected: plugin project is created and references core project.

- [ ] **Step 3: Replace plugin metadata**

Create `src/HUnityAutoTranslator.Plugin/MyPluginInfo.cs`:

```csharp
namespace HUnityAutoTranslator.Plugin;

internal static class MyPluginInfo
{
    public const string PLUGIN_GUID = "com.hanfeng.hunityautotranslator";
    public const string PLUGIN_NAME = "HUnityAutoTranslator";
    public const string PLUGIN_VERSION = "0.1.0";
}
```

- [ ] **Step 4: Implement defensive plugin startup**

Modify `src/HUnityAutoTranslator.Plugin/Plugin.cs` so it contains:

```csharp
using BepInEx;
using BepInEx.Unity.Mono;
using HUnityAutoTranslator.Core.Control;

namespace HUnityAutoTranslator.Plugin;

[BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
public sealed class Plugin : BaseUnityPlugin
{
    private ControlPanelService? _controlPanel;
    private LocalHttpServer? _httpServer;

    private void Awake()
    {
        try
        {
            _controlPanel = ControlPanelService.CreateDefault();
            _httpServer = new LocalHttpServer(_controlPanel, Logger);
            _httpServer.Start("127.0.0.1", 0);
            Logger.LogInfo($"{MyPluginInfo.PLUGIN_NAME} loaded. Control panel: {_httpServer.Url}");
        }
        catch (Exception ex)
        {
            Logger.LogError($"Startup failed, plugin will stay inactive: {ex}");
        }
    }

    private void OnDestroy()
    {
        _httpServer?.Dispose();
    }
}
```

- [ ] **Step 5: Implement Unity text target and result applier**

Create `src/HUnityAutoTranslator.Plugin/Unity/IUnityTextTarget.cs`:

```csharp
namespace HUnityAutoTranslator.Plugin.Unity;

internal interface IUnityTextTarget
{
    string Id { get; }
    bool IsAlive { get; }
    bool IsVisible { get; }
    string? GetText();
    void SetText(string value);
}
```

Create `src/HUnityAutoTranslator.Plugin/Unity/UnityMainThreadResultApplier.cs`:

```csharp
using HUnityAutoTranslator.Core.Dispatching;

namespace HUnityAutoTranslator.Plugin.Unity;

internal sealed class UnityMainThreadResultApplier
{
    private readonly Dictionary<string, IUnityTextTarget> _targets = new();

    public void Register(IUnityTextTarget target) => _targets[target.Id] = target;

    public int Apply(IReadOnlyList<TranslationResult> results)
    {
        var applied = 0;
        foreach (var result in results)
        {
            if (!_targets.TryGetValue(result.TargetId, out var target) || !target.IsAlive)
            {
                continue;
            }

            if (target.GetText() == result.SourceText)
            {
                target.SetText(result.TranslatedText);
                applied++;
            }
        }

        return applied;
    }
}
```

- [ ] **Step 6: Implement minimal localhost HTTP server**

Create `src/HUnityAutoTranslator.Plugin/Web/LocalHttpServer.cs`:

```csharp
using System.Net;
using BepInEx.Logging;
using HUnityAutoTranslator.Core.Control;
using Newtonsoft.Json;

namespace HUnityAutoTranslator.Plugin;

internal sealed class LocalHttpServer : IDisposable
{
    private readonly ControlPanelService _controlPanel;
    private readonly ManualLogSource _logger;
    private HttpListener? _listener;
    private CancellationTokenSource? _cts;

    public string Url { get; private set; } = "";

    public LocalHttpServer(ControlPanelService controlPanel, ManualLogSource logger)
    {
        _controlPanel = controlPanel;
        _logger = logger;
    }

    public void Start(string host, int port)
    {
        var selectedPort = port == 0 ? 48110 : port;
        _listener = new HttpListener();
        Url = $"http://{host}:{selectedPort}/";
        _listener.Prefixes.Add(Url);
        _listener.Start();
        _cts = new CancellationTokenSource();
        _ = Task.Run(() => ServeAsync(_cts.Token));
    }

    private async Task ServeAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested && _listener?.IsListening == true)
        {
            try
            {
                var context = await _listener.GetContextAsync();
                var path = context.Request.Url?.AbsolutePath ?? "/";
                if (path == "/api/state")
                {
                    await WriteJsonAsync(context.Response, _controlPanel.GetState());
                }
                else
                {
                    await WriteHtmlAsync(context.Response);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"HTTP panel request failed: {ex.Message}");
            }
        }
    }

    private static async Task WriteJsonAsync(HttpListenerResponse response, object value)
    {
        response.ContentType = "application/json; charset=utf-8";
        using var writer = new StreamWriter(response.OutputStream);
        await writer.WriteAsync(JsonConvert.SerializeObject(value));
    }

    private static async Task WriteHtmlAsync(HttpListenerResponse response)
    {
        response.ContentType = "text/html; charset=utf-8";
        using var writer = new StreamWriter(response.OutputStream);
        await writer.WriteAsync("<!doctype html><meta charset=\"utf-8\"><title>HUnityAutoTranslator</title><h1>HUnityAutoTranslator</h1><div id=\"app\">控制面板运行中</div>");
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _listener?.Close();
    }
}
```

- [ ] **Step 7: Build plugin**

Run:

```powershell
dotnet build HUnityAutoTranslator.sln
```

Expected: build succeeds.

- [ ] **Step 8: Commit**

Run:

```powershell
git add HUnityAutoTranslator.sln src/HUnityAutoTranslator.Plugin
git commit -m "feat: add BepInEx plugin shell"
```

## Task 9: UGUI, TMP, And IMGUI Capture Modules

**Files:**
- Create: `src/HUnityAutoTranslator.Plugin/Capture/ITextCaptureModule.cs`
- Create: `src/HUnityAutoTranslator.Plugin/Capture/UguiTextScanner.cs`
- Create: `src/HUnityAutoTranslator.Plugin/Capture/TmpTextScanner.cs`
- Create: `src/HUnityAutoTranslator.Plugin/Capture/ImguiHookInstaller.cs`
- Create: `src/HUnityAutoTranslator.Plugin/Capture/TextCaptureCoordinator.cs`
- Modify: `src/HUnityAutoTranslator.Plugin/Plugin.cs`
- Test: manual validation in `docs/manual-validation.md`.

- [ ] **Step 1: Define capture module contract**

Create `src/HUnityAutoTranslator.Plugin/Capture/ITextCaptureModule.cs`:

```csharp
namespace HUnityAutoTranslator.Plugin.Capture;

internal interface ITextCaptureModule : IDisposable
{
    string Name { get; }
    bool IsEnabled { get; }
    void Start();
    void Tick();
}
```

- [ ] **Step 2: Implement UGUI scanner**

Create `src/HUnityAutoTranslator.Plugin/Capture/UguiTextScanner.cs` with these behaviors:

- Resolve type by `Type.GetType("UnityEngine.UI.Text, UnityEngine.UI")`.
- Use `UnityEngine.Object.FindObjectsOfType(type)` through reflection.
- Read and write `text` property through reflection.
- Register targets with `UnityMainThreadResultApplier`.
- Submit captured text to `TextPipeline`.
- Catch exceptions per object and log warnings with rate limiting.

Key constructor:

```csharp
internal UguiTextScanner(TextPipeline pipeline, UnityMainThreadResultApplier applier, ManualLogSource logger, RuntimeConfig config)
```

- [ ] **Step 3: Implement TMP scanner**

Create `src/HUnityAutoTranslator.Plugin/Capture/TmpTextScanner.cs` with these behaviors:

- Resolve type by trying `TMPro.TMP_Text, Unity.TextMeshPro` and `TMPro.TMP_Text, Assembly-CSharp`.
- If type cannot be resolved, mark module disabled and log once.
- Use the same reflection target wrapper pattern as UGUI.
- Disable only TMP scanner on repeated reflection failure.

Key constructor:

```csharp
internal TmpTextScanner(TextPipeline pipeline, UnityMainThreadResultApplier applier, ManualLogSource logger, RuntimeConfig config)
```

- [ ] **Step 4: Implement IMGUI hook installer**

Create `src/HUnityAutoTranslator.Plugin/Capture/ImguiHookInstaller.cs` with these behaviors:

- Create a Harmony instance with ID `com.hanfeng.hunityautotranslator.imgui`.
- Patch overloads of `UnityEngine.GUI.Label`, `Button`, `Toggle`, `TextField`.
- Patch overloads of `UnityEngine.GUILayout.Label`, `Button`, `Toggle`, `TextField`.
- Prefix only string/GUIContent text arguments.
- If cached translation exists, replace before draw.
- If no translation exists, submit source text and leave original.
- Catch patch failure per method and continue.

Key constructor:

```csharp
internal ImguiHookInstaller(TextPipeline pipeline, ITranslationCache cache, ManualLogSource logger, RuntimeConfig config)
```

- [ ] **Step 5: Implement coordinator and wire plugin**

Create `src/HUnityAutoTranslator.Plugin/Capture/TextCaptureCoordinator.cs`:

```csharp
namespace HUnityAutoTranslator.Plugin.Capture;

internal sealed class TextCaptureCoordinator : IDisposable
{
    private readonly IReadOnlyList<ITextCaptureModule> _modules;

    public TextCaptureCoordinator(IEnumerable<ITextCaptureModule> modules)
    {
        _modules = modules.ToArray();
    }

    public void Start()
    {
        foreach (var module in _modules)
        {
            module.Start();
        }
    }

    public void Tick()
    {
        foreach (var module in _modules)
        {
            if (module.IsEnabled)
            {
                module.Tick();
            }
        }
    }

    public void Dispose()
    {
        foreach (var module in _modules)
        {
            module.Dispose();
        }
    }
}
```

Modify `Plugin.Update()` to:

- Drain `ResultDispatcher` with `RuntimeConfig.MaxWritebacksPerFrame`.
- Apply results through `UnityMainThreadResultApplier`.
- Tick capture coordinator on configured interval.

- [ ] **Step 6: Build plugin**

Run:

```powershell
dotnet build HUnityAutoTranslator.sln
```

Expected: build succeeds.

- [ ] **Step 7: Commit**

Run:

```powershell
git add src/HUnityAutoTranslator.Plugin
git commit -m "feat: add Unity text capture modules"
```

## Task 10: Full HTTP Panel, Packaging, And Manual Validation

**Files:**
- Modify: `src/HUnityAutoTranslator.Plugin/Web/LocalHttpServer.cs`
- Create: `src/HUnityAutoTranslator.Plugin/Web/ControlPanelHtml.cs`
- Create: `docs/manual-validation.md`
- Create: `README.md`
- Create: `build/package.ps1`

- [ ] **Step 1: Replace minimal HTML with functional Chinese control panel**

Create `src/HUnityAutoTranslator.Plugin/Web/ControlPanelHtml.cs`:

```csharp
namespace HUnityAutoTranslator.Plugin.Web;

internal static class ControlPanelHtml
{
    public const string Html = """
<!doctype html>
<html lang="zh-CN">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width,initial-scale=1">
  <title>HUnityAutoTranslator</title>
  <style>
    body{font-family:system-ui,Segoe UI,Arial,sans-serif;margin:0;background:#101418;color:#e9eef3}
    header{padding:16px 20px;border-bottom:1px solid #2a333d}
    main{display:grid;grid-template-columns:220px 1fr;min-height:calc(100vh - 58px)}
    nav{padding:16px;border-right:1px solid #2a333d;background:#151b21}
    section{padding:18px 22px}
    label{display:block;margin:12px 0 6px;color:#b8c4cf}
    input,select,button{font:inherit;padding:8px;border-radius:6px;border:1px solid #34404c;background:#0f1419;color:#e9eef3}
    button{cursor:pointer;background:#2363eb;border-color:#2363eb}
    .grid{display:grid;grid-template-columns:repeat(3,minmax(0,1fr));gap:12px}
    .card{border:1px solid #2a333d;border-radius:8px;padding:14px;background:#151b21}
    .muted{color:#95a3af}
  </style>
</head>
<body>
  <header><strong>HUnityAutoTranslator</strong> <span class="muted">本机控制面板</span></header>
  <main>
    <nav>状态总览<br>翻译设置<br>服务商配置<br>缓存管理<br>日志与错误</nav>
    <section>
      <div class="grid">
        <div class="card">翻译状态<br><strong id="enabled">读取中</strong></div>
        <div class="card">队列数量<br><strong id="queue">0</strong></div>
        <div class="card">API Key<br><strong id="key">未设置</strong></div>
      </div>
      <label>目标语言</label><input id="targetLanguage" value="zh-Hans">
      <label>并发请求数</label><input id="maxConcurrentRequests" type="number" min="1" max="16">
      <label>每分钟请求上限</label><input id="requestsPerMinute" type="number" min="1" max="600">
      <p><button onclick="save()">保存设置</button></p>
      <pre id="error" class="muted"></pre>
    </section>
  </main>
  <script>
    async function refresh(){
      const r=await fetch('/api/state'); const s=await r.json();
      enabled.textContent=s.Enabled?'已启用':'已暂停';
      queue.textContent=s.QueueCount;
      key.textContent=s.ApiKeyConfigured?'已设置':'未设置';
      targetLanguage.value=s.TargetLanguage;
      maxConcurrentRequests.value=s.MaxConcurrentRequests;
      requestsPerMinute.value=s.RequestsPerMinute;
      error.textContent=s.LastError||'';
    }
    async function save(){
      await fetch('/api/config',{method:'POST',headers:{'content-type':'application/json'},body:JSON.stringify({
        TargetLanguage:targetLanguage.value,
        MaxConcurrentRequests:Number(maxConcurrentRequests.value),
        RequestsPerMinute:Number(requestsPerMinute.value)
      })});
      refresh();
    }
    refresh(); setInterval(refresh,2000);
  </script>
</body>
</html>
""";
}
```

Modify `LocalHttpServer`:

- Serve `ControlPanelHtml.Html` for `/`.
- Return `ControlPanelService.GetState()` for `GET /api/state`.
- Accept `UpdateConfigRequest` JSON for `POST /api/config`.
- Reject non-local requests by checking `context.Request.RemoteEndPoint.Address`.

- [ ] **Step 2: Add package script**

Create `build/package.ps1`:

```powershell
$ErrorActionPreference = "Stop"
dotnet build HUnityAutoTranslator.sln -c Release
$out = Join-Path $PSScriptRoot "..\artifacts\HUnityAutoTranslator"
New-Item -ItemType Directory -Force $out | Out-Null
Copy-Item "..\src\HUnityAutoTranslator.Plugin\bin\Release\netstandard2.1\HUnityAutoTranslator.Plugin.dll" $out -Force
Copy-Item "..\src\HUnityAutoTranslator.Core\bin\Release\netstandard2.1\HUnityAutoTranslator.Core.dll" $out -Force
Get-ChildItem "..\src\HUnityAutoTranslator.Plugin\bin\Release\netstandard2.1" -Filter "Newtonsoft.Json.dll" | Copy-Item -Destination $out -Force
Write-Host "Package ready: $out"
```

- [ ] **Step 3: Add manual validation checklist**

Create `docs/manual-validation.md`:

```markdown
# Manual Validation

1. Install BepInEx Unity Mono BE build into a Mono Unity game.
2. Copy packaged DLLs into `BepInEx/plugins/HUnityAutoTranslator/`.
3. Launch the game and confirm `BepInEx/LogOutput.log` contains `HUnityAutoTranslator loaded`.
4. Open the logged localhost control panel URL.
5. Confirm API Key displays only `已设置` or `未设置`.
6. Set target language to `zh-Hans`, provider, model, API key, concurrency, and request limit.
7. Confirm UGUI static text translates without blocking UI.
8. Confirm UGUI dynamic text updates after game changes it.
9. Confirm TMP text translates when TMP exists.
10. Confirm plugin logs one warning and continues when TMP does not exist.
11. Confirm IMGUI Label/Button/Toggle/TextField use cached translations when available.
12. Confirm first-time uncached text shows original, then updates after translation returns.
13. Confirm changing target language causes new translations to use the new language.
14. Confirm provider failure leaves original text visible.
15. Confirm malformed AI output with broken placeholders is not written back.
```

- [ ] **Step 4: Add README**

Create `README.md`:

```markdown
# HUnityAutoTranslator

BepInEx 6 Bleeding Edge Unity Mono plugin for automatic in-game text translation.

## Scope

- Unity backend: Mono only.
- Text: UGUI, IMGUI, TextMeshPro.
- Providers: OpenAI native Responses API, DeepSeek native Chat Completions, OpenAI-compatible Chat Completions.
- Control panel: localhost HTTP UI, default `127.0.0.1`.

## Build

```powershell
dotnet test HUnityAutoTranslator.sln
.\build\package.ps1
```

## Install

Copy packaged DLLs from `artifacts/HUnityAutoTranslator` into `BepInEx/plugins/HUnityAutoTranslator/`.
```

- [ ] **Step 5: Run full verification**

Run:

```powershell
dotnet test HUnityAutoTranslator.sln
dotnet build HUnityAutoTranslator.sln -c Release
.\build\package.ps1
```

Expected: all tests pass, Release build succeeds, package folder is created.

- [ ] **Step 6: Commit**

Run:

```powershell
git add src/HUnityAutoTranslator.Plugin docs build README.md
git commit -m "feat: add control panel and package docs"
```

## Self-Review Checklist

- Spec coverage:
  - BepInEx 6 Unity Mono plugin shell: Tasks 8-10.
  - UGUI/TMP/IMGUI capture: Task 9.
  - Async AI translation: Tasks 4-6.
  - OpenAI/DeepSeek/OpenAI-compatible providers: Task 4.
  - Strong prompt constraints and flexible style: Task 2.
  - Cache and language/provider-scoped keys: Task 3.
  - Multi-threaded concurrent translation: Task 5.
  - Low-latency result writeback: Tasks 5 and 9.
  - HTTP localhost panel: Tasks 7 and 10.
  - Performance and compatibility guardrails: Tasks 5, 8, 9, 10.
- Placeholder scan:
  - No unresolved placeholders or deferred sections are intentional in this plan.
- Type consistency:
  - `RuntimeConfig`, `ProviderProfile`, `TranslationJobQueue`, `ResultDispatcher`, `TextPipeline`, and `ControlPanelService` are introduced before later tasks consume them.

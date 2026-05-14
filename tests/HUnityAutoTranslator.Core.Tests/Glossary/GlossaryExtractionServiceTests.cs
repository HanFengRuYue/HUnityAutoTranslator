using FluentAssertions;
using HUnityAutoTranslator.Core.Caching;
using HUnityAutoTranslator.Core.Configuration;
using HUnityAutoTranslator.Core.Glossary;
using HUnityAutoTranslator.Core.Providers;

namespace HUnityAutoTranslator.Core.Tests.Glossary;

public sealed class GlossaryExtractionServiceTests
{
    private static readonly DateTimeOffset BaseTime = new(2026, 5, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ExtractOnceAsync_imports_terms_with_consistent_translations_and_normalizes_note()
    {
        var cache = new MemoryTranslationCache();
        var glossary = new MemoryGlossaryStore();
        var config = RuntimeConfig.CreateDefault() with { EnableAutoTermExtraction = true };
        AddRow(cache, "Find Freddy in Pirate Cove", "在海盗湾找到弗雷迪", 1);
        AddRow(cache, "Pirate Cove is locked", "海盗湾被锁住了", 2);
        AddRow(cache, "Go to Pirate Cove", "前往海盗湾", 3);
        AddRow(cache, "Pirate Cove music box", "海盗湾八音盒", 4);
        var provider = new RecordingProvider("""[{"source":"Pirate Cove","target":"海盗湾","note":"地点"}]""");

        var result = await GlossaryExtractionService.ExtractOnceAsync(cache, glossary, provider, config, CancellationToken.None);

        result.ImportedCount.Should().Be(1);
        var term = glossary.GetEnabledTerms("zh-Hans").Should().ContainSingle().Subject;
        term.SourceTerm.Should().Be("Pirate Cove");
        term.TargetTerm.Should().Be("海盗湾");
        term.Note.Should().Be(GlossaryTermCategory.PlaceName);
    }

    [Fact]
    public async Task ExtractOnceAsync_rejects_terms_whose_translation_varies_by_context()
    {
        var cache = new MemoryTranslationCache();
        var glossary = new MemoryGlossaryStore();
        var config = RuntimeConfig.CreateDefault() with { EnableAutoTermExtraction = true };
        AddRow(cache, "Pirate Cove", "海盗湾", 1);
        AddRow(cache, "Hidden Cove", "隐秘洞窟", 2);
        AddRow(cache, "Cove path", "海岸小径", 3);
        AddRow(cache, "Lost Cove", "迷失港湾", 4);
        var provider = new RecordingProvider("""[{"source":"Cove","target":"湾","note":"地点"}]""");

        var result = await GlossaryExtractionService.ExtractOnceAsync(cache, glossary, provider, config, CancellationToken.None);

        result.ImportedCount.Should().Be(0);
        glossary.GetEnabledTerms("zh-Hans").Should().BeEmpty();
    }

    [Fact]
    public async Task ExtractOnceAsync_rejects_short_high_frequency_function_words()
    {
        var cache = new MemoryTranslationCache();
        var glossary = new MemoryGlossaryStore();
        var config = RuntimeConfig.CreateDefault() with { EnableAutoTermExtraction = true };
        AddRow(cache, "はい、わかりました", "好的，我明白了", 1);
        AddRow(cache, "はい？", "什么？", 2);
        AddRow(cache, "はい、そうです", "是的，没错", 3);
        AddRow(cache, "はい！", "好！", 4);
        var provider = new RecordingProvider("""[{"source":"はい","target":"是的","note":"其他"}]""");

        var result = await GlossaryExtractionService.ExtractOnceAsync(cache, glossary, provider, config, CancellationToken.None);

        result.ImportedCount.Should().Be(0);
        glossary.GetEnabledTerms("zh-Hans").Should().BeEmpty();
    }

    [Fact]
    public async Task ExtractOnceAsync_rejects_terms_with_too_few_occurrences()
    {
        var cache = new MemoryTranslationCache();
        var glossary = new MemoryGlossaryStore();
        var config = RuntimeConfig.CreateDefault() with { EnableAutoTermExtraction = true };
        AddRow(cache, "Excalibur shines", "圣剑闪耀", 1);
        AddRow(cache, "Use Excalibur now", "现在使用圣剑", 2);
        AddRow(cache, "The castle gate", "城堡大门", 3);
        AddRow(cache, "A dark night", "黑暗的夜晚", 4);
        var provider = new RecordingProvider("""[{"source":"Excalibur","target":"圣剑","note":"物品"}]""");

        var result = await GlossaryExtractionService.ExtractOnceAsync(cache, glossary, provider, config, CancellationToken.None);

        result.ImportedCount.Should().Be(0);
    }

    [Fact]
    public async Task ExtractOnceAsync_does_nothing_when_disabled()
    {
        var cache = new MemoryTranslationCache();
        var glossary = new MemoryGlossaryStore();
        var provider = new RecordingProvider("""[{"source":"Freddy","target":"弗雷迪","note":"角色名"}]""");

        var result = await GlossaryExtractionService.ExtractOnceAsync(
            cache,
            glossary,
            provider,
            RuntimeConfig.CreateDefault() with { EnableAutoTermExtraction = false },
            CancellationToken.None);

        result.ImportedCount.Should().Be(0);
        provider.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task ExtractOnceAsync_advances_watermark_and_skips_already_extracted_rows()
    {
        var cache = new MemoryTranslationCache();
        var glossary = new MemoryGlossaryStore();
        var config = RuntimeConfig.CreateDefault() with { EnableAutoTermExtraction = true };
        AddRow(cache, "Find Freddy in Pirate Cove", "在海盗湾找到弗雷迪", 1);
        AddRow(cache, "Pirate Cove is locked", "海盗湾被锁住了", 2);
        AddRow(cache, "Go to Pirate Cove", "前往海盗湾", 3);
        AddRow(cache, "Pirate Cove music box", "海盗湾八音盒", 4);

        var firstRun = new RecordingProvider("""[{"source":"Pirate Cove","target":"海盗湾","note":"地点"}]""");
        await GlossaryExtractionService.ExtractOnceAsync(cache, glossary, firstRun, config, CancellationToken.None);
        firstRun.Requests.Should().ContainSingle();

        var secondRun = new RecordingProvider("[]");
        var result = await GlossaryExtractionService.ExtractOnceAsync(cache, glossary, secondRun, config, CancellationToken.None);

        secondRun.Requests.Should().BeEmpty();
        result.SourcePairCount.Should().Be(0);
    }

    [Fact]
    public async Task ExtractOnceAsync_groups_by_component_and_escalates_single_ui_text_to_parent()
    {
        var cache = new MemoryTranslationCache();
        var glossary = new MemoryGlossaryStore();
        var config = RuntimeConfig.CreateDefault() with { EnableAutoTermExtraction = true };
        AddRow(cache, "dialogue alpha", "对话甲", 1, "Game", "Game/DialogueBox");
        AddRow(cache, "dialogue beta", "对话乙", 2, "Game", "Game/DialogueBox");
        AddRow(cache, "dialogue gamma", "对话丙", 3, "Game", "Game/DialogueBox");
        AddRow(cache, "dialogue delta", "对话丁", 4, "Game", "Game/DialogueBox");
        AddRow(cache, "menu start", "开始", 5, "Game", "Game/Menu/Start");
        AddRow(cache, "menu options", "选项", 6, "Game", "Game/Menu/Options");
        AddRow(cache, "menu quit", "退出", 7, "Game", "Game/Menu/Quit");
        var provider = new RecordingProvider("[]");

        await GlossaryExtractionService.ExtractOnceAsync(cache, glossary, provider, config, CancellationToken.None);

        provider.Requests.Should().HaveCount(2);
        provider.Requests.Should().ContainSingle(request =>
            request.UserPrompt.Contains("dialogue alpha") &&
            request.UserPrompt.Contains("dialogue beta") &&
            request.UserPrompt.Contains("dialogue gamma") &&
            request.UserPrompt.Contains("dialogue delta"));
        provider.Requests.Should().ContainSingle(request =>
            request.UserPrompt.Contains("menu start") &&
            request.UserPrompt.Contains("menu options") &&
            request.UserPrompt.Contains("menu quit"));
    }

    private static void AddRow(
        MemoryTranslationCache cache,
        string source,
        string translated,
        int minuteOffset,
        string scene = "",
        string hierarchy = "")
    {
        var stamp = BaseTime.AddMinutes(minuteOffset);
        cache.Update(new TranslationCacheEntry(
            SourceText: source,
            TargetLanguage: "zh-Hans",
            ProviderKind: "OpenAI",
            ProviderBaseUrl: string.Empty,
            ProviderEndpoint: string.Empty,
            ProviderModel: string.Empty,
            PromptPolicyVersion: "p",
            TranslatedText: translated,
            SceneName: scene,
            ComponentHierarchy: hierarchy,
            ComponentType: null,
            ReplacementFont: null,
            CreatedUtc: stamp,
            UpdatedUtc: stamp));
    }

    private sealed class RecordingProvider : ITranslationProvider
    {
        private readonly string _content;

        public RecordingProvider(string content = "[]") => _content = content;

        public List<TranslationRequest> Requests { get; } = new();

        public ProviderKind Kind => ProviderKind.OpenAI;

        public Task<TranslationResponse> TranslateAsync(TranslationRequest request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(TranslationResponse.Success(new[] { _content }));
        }
    }
}

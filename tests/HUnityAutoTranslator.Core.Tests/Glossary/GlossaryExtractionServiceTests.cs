using FluentAssertions;
using HUnityAutoTranslator.Core.Caching;
using HUnityAutoTranslator.Core.Configuration;
using HUnityAutoTranslator.Core.Glossary;
using HUnityAutoTranslator.Core.Pipeline;
using HUnityAutoTranslator.Core.Providers;

namespace HUnityAutoTranslator.Core.Tests.Glossary;

public sealed class GlossaryExtractionServiceTests
{
    [Fact]
    public async Task ExtractOnceAsync_writes_valid_terms_and_keeps_manual_terms()
    {
        var cache = new MemoryTranslationCache();
        var glossary = new MemoryGlossaryStore();
        var config = RuntimeConfig.CreateDefault() with { EnableAutoTermExtraction = true };
        var key = TranslationCacheKey.Create("Find Freddy in Pirate Cove", "zh-Hans", config.Provider, TextPipeline.PromptPolicyVersion);
        cache.Set(key, "在海盗湾找到弗雷迪", TranslationCacheContext.Empty);
        glossary.UpsertManual(GlossaryTerm.CreateManual("Freddy", "佛莱迪", "zh-Hans", null));
        var provider = new StaticProvider("""
            [
              {"source":"Freddy","target":"弗雷迪","note":"角色名"},
              {"source":"Pirate Cove","target":"海盗湾","note":"地点"},
              {"source":"NotInSource","target":"不存在","note":"invalid"}
            ]
            """);

        var result = await GlossaryExtractionService.ExtractOnceAsync(cache, glossary, provider, config, CancellationToken.None);

        result.ImportedCount.Should().Be(1);
        result.SkippedCount.Should().BeGreaterThanOrEqualTo(2);
        glossary.GetEnabledTerms("zh-Hans").Should().Contain(term => term.SourceTerm == "Pirate Cove" && term.TargetTerm == "海盗湾");
        glossary.GetEnabledTerms("zh-Hans").Should().Contain(term => term.SourceTerm == "Freddy" && term.TargetTerm == "佛莱迪" && term.Source == GlossaryTermSource.Manual);
    }

    [Fact]
    public async Task ExtractOnceAsync_does_nothing_when_disabled()
    {
        var cache = new MemoryTranslationCache();
        var glossary = new MemoryGlossaryStore();
        var provider = new StaticProvider("""[{"source":"Freddy","target":"弗雷迪"}]""");

        var result = await GlossaryExtractionService.ExtractOnceAsync(
            cache,
            glossary,
            provider,
            RuntimeConfig.CreateDefault() with { EnableAutoTermExtraction = false },
            CancellationToken.None);

        result.ImportedCount.Should().Be(0);
        provider.Called.Should().BeFalse();
    }

    private sealed class StaticProvider : ITranslationProvider
    {
        private readonly string _content;

        public StaticProvider(string content) => _content = content;

        public bool Called { get; private set; }

        public ProviderKind Kind => ProviderKind.OpenAI;

        public Task<TranslationResponse> TranslateAsync(TranslationRequest request, CancellationToken cancellationToken)
        {
            Called = true;
            return Task.FromResult(TranslationResponse.Success(new[] { _content }));
        }
    }
}

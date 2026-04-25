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

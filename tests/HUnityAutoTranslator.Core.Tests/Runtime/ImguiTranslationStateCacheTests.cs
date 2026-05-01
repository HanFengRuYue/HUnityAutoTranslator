using FluentAssertions;
using HUnityAutoTranslator.Core.Runtime;

namespace HUnityAutoTranslator.Core.Tests.Runtime;

public sealed class ImguiTranslationStateCacheTests
{
    [Fact]
    public void ResolveForDraw_registers_text_without_running_pipeline_work()
    {
        var cache = new ImguiTranslationStateCache(
            maxEntries: 128,
            pendingRefreshSeconds: 1,
            entryTtlSeconds: 60);

        var firstDraw = cache.ResolveForDraw(
            "God Mode",
            "zh-Hans",
            "prompt-v5",
            "title_01",
            nowSeconds: 0,
            frameId: 1);
        var secondDraw = cache.ResolveForDraw(
            "God Mode",
            "zh-Hans",
            "prompt-v5",
            "title_01",
            nowSeconds: 0.01,
            frameId: 1);

        firstDraw.DisplayText.Should().Be("God Mode");
        firstDraw.IsTranslated.Should().BeFalse();
        secondDraw.DisplayText.Should().Be("God Mode");
        cache.TakePendingBatch(maxCount: 16, nowSeconds: 0.02)
            .Should().ContainSingle(item =>
                item.SourceText == "God Mode" &&
                item.TargetLanguage == "zh-Hans" &&
                item.PromptPolicyVersion == "prompt-v5" &&
                item.SceneName == "title_01" &&
                item.ShouldProcessSource);
    }

    [Fact]
    public void MarkCached_makes_later_draws_use_translated_text_from_memory()
    {
        var cache = new ImguiTranslationStateCache(
            maxEntries: 128,
            pendingRefreshSeconds: 1,
            entryTtlSeconds: 60);
        cache.ResolveForDraw("God Mode", "zh-Hans", "prompt-v5", "title_01", 0, 1);
        var pending = cache.TakePendingBatch(maxCount: 16, nowSeconds: 0.05).Should().ContainSingle().Which;

        cache.MarkCached(pending, "上帝模式", nowSeconds: 0.05);

        var translated = cache.ResolveForDraw("God Mode", "zh-Hans", "prompt-v5", "title_01", 0.06, 2);
        translated.DisplayText.Should().Be("上帝模式");
        translated.IsTranslated.Should().BeTrue();
        cache.TakePendingBatch(maxCount: 16, nowSeconds: 2).Should().BeEmpty();
    }

    [Fact]
    public void MarkQueued_waits_before_refreshing_cache_and_does_not_reprocess_source()
    {
        var cache = new ImguiTranslationStateCache(
            maxEntries: 128,
            pendingRefreshSeconds: 1,
            entryTtlSeconds: 60);
        cache.ResolveForDraw("God Mode", "zh-Hans", "prompt-v5", "title_01", 0, 1);
        var firstBatchItem = cache.TakePendingBatch(maxCount: 16, nowSeconds: 0.05).Should().ContainSingle().Which;
        firstBatchItem.ShouldProcessSource.Should().BeTrue();

        cache.MarkQueued(firstBatchItem, nowSeconds: 0.05);

        cache.TakePendingBatch(maxCount: 16, nowSeconds: 0.5).Should().BeEmpty();
        var refreshItem = cache.TakePendingBatch(maxCount: 16, nowSeconds: 1.1).Should().ContainSingle().Which;
        refreshItem.SourceText.Should().Be("God Mode");
        refreshItem.ShouldProcessSource.Should().BeFalse();
    }

    [Fact]
    public void TakePendingBatch_caps_work_and_skips_items_until_they_are_released()
    {
        var cache = new ImguiTranslationStateCache(
            maxEntries: 128,
            pendingRefreshSeconds: 1,
            entryTtlSeconds: 60);
        for (var i = 0; i < 5; i++)
        {
            cache.ResolveForDraw($"Command {i}", "zh-Hans", "prompt-v5", "title_01", 0, 1);
        }

        var firstBatch = cache.TakePendingBatch(maxCount: 2, nowSeconds: 0.05);
        firstBatch.Select(item => item.SourceText).Should().Equal("Command 0", "Command 1");
        cache.TakePendingBatch(maxCount: 5, nowSeconds: 0.06)
            .Select(item => item.SourceText)
            .Should().Equal("Command 2", "Command 3", "Command 4");
    }

    [Fact]
    public void MarkIgnored_keeps_known_untranslated_text_out_of_later_batches()
    {
        var cache = new ImguiTranslationStateCache(
            maxEntries: 128,
            pendingRefreshSeconds: 1,
            entryTtlSeconds: 60);
        cache.ResolveForDraw("游戏设置", "zh-Hans", "prompt-v5", "title_01", 0, 1);
        var pending = cache.TakePendingBatch(maxCount: 16, nowSeconds: 0.05).Should().ContainSingle().Which;

        cache.MarkIgnored(pending, nowSeconds: 0.05);

        cache.ResolveForDraw("游戏设置", "zh-Hans", "prompt-v5", "title_01", 2, 2)
            .DisplayText.Should().Be("游戏设置");
        cache.TakePendingBatch(maxCount: 16, nowSeconds: 2).Should().BeEmpty();
    }

    [Fact]
    public void Old_entries_are_trimmed_without_dropping_the_newest_entry()
    {
        var cache = new ImguiTranslationStateCache(
            maxEntries: 16,
            pendingRefreshSeconds: 1,
            entryTtlSeconds: 60);
        for (var i = 0; i < 17; i++)
        {
            cache.ResolveForDraw($"Command {i}", "zh-Hans", "prompt-v5", "title_01", i, i);
        }

        var batch = cache.TakePendingBatch(maxCount: 32, nowSeconds: 17.1);

        batch.Select(item => item.SourceText).Should().Contain("Command 16");
        batch.Select(item => item.SourceText).Should().NotContain("Command 0");
    }
}

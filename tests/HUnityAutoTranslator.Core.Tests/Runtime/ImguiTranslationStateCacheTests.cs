using FluentAssertions;
using HUnityAutoTranslator.Core.Runtime;

namespace HUnityAutoTranslator.Core.Tests.Runtime;

public sealed class ImguiTranslationStateCacheTests
{
    [Fact]
    public void Resolve_reuses_pending_state_without_reprocessing_repeated_draws()
    {
        var cache = new ImguiTranslationStateCache(
            maxEntries: 128,
            maxNewItemsPerFrame: 8,
            maxRefreshesPerFrame: 4,
            pendingRefreshSeconds: 1,
            entryTtlSeconds: 60);
        var cacheLookups = 0;
        var processCalls = 0;

        for (var i = 0; i < 10; i++)
        {
            var result = cache.Resolve(
                "GodMode",
                "zh-Hans",
                "prompt-v5",
                "scene_01",
                nowSeconds: i * 0.1,
                frameId: i,
                tryGetCachedTranslation: () =>
                {
                    cacheLookups++;
                    return null;
                },
                processSourceText: () =>
                {
                    processCalls++;
                    return null;
                });

            result.DisplayText.Should().Be("GodMode");
            result.IsTranslated.Should().BeFalse();
        }

        cacheLookups.Should().Be(1);
        processCalls.Should().Be(1);
    }

    [Fact]
    public void Resolve_refreshes_pending_cache_without_reprocessing_source_text()
    {
        var cache = new ImguiTranslationStateCache(
            maxEntries: 128,
            maxNewItemsPerFrame: 8,
            maxRefreshesPerFrame: 4,
            pendingRefreshSeconds: 1,
            entryTtlSeconds: 60);
        var cacheLookups = 0;
        var processCalls = 0;

        cache.Resolve(
            "GodMode",
            "zh-Hans",
            "prompt-v5",
            "scene_01",
            nowSeconds: 0,
            frameId: 1,
            tryGetCachedTranslation: () =>
            {
                cacheLookups++;
                return null;
            },
            processSourceText: () =>
            {
                processCalls++;
                return null;
            });

        var translated = cache.Resolve(
            "GodMode",
            "zh-Hans",
            "prompt-v5",
            "scene_01",
            nowSeconds: 1.25,
            frameId: 2,
            tryGetCachedTranslation: () =>
            {
                cacheLookups++;
                return "上帝模式";
            },
            processSourceText: () =>
            {
                processCalls++;
                return null;
            });

        var repeated = cache.Resolve(
            "GodMode",
            "zh-Hans",
            "prompt-v5",
            "scene_01",
            nowSeconds: 1.3,
            frameId: 3,
            tryGetCachedTranslation: () =>
            {
                cacheLookups++;
                return "上帝模式";
            },
            processSourceText: () =>
            {
                processCalls++;
                return null;
            });

        translated.DisplayText.Should().Be("上帝模式");
        translated.IsTranslated.Should().BeTrue();
        repeated.DisplayText.Should().Be("上帝模式");
        repeated.IsTranslated.Should().BeTrue();
        cacheLookups.Should().Be(2);
        processCalls.Should().Be(1);
    }

    [Fact]
    public void Resolve_limits_new_source_processing_per_frame()
    {
        var cache = new ImguiTranslationStateCache(
            maxEntries: 128,
            maxNewItemsPerFrame: 2,
            maxRefreshesPerFrame: 4,
            pendingRefreshSeconds: 1,
            entryTtlSeconds: 60);
        var cacheLookups = 0;
        var processCalls = 0;

        for (var i = 0; i < 5; i++)
        {
            cache.Resolve(
                $"Text {i}",
                "zh-Hans",
                "prompt-v5",
                "scene_01",
                nowSeconds: 0,
                frameId: 1,
                tryGetCachedTranslation: () =>
                {
                    cacheLookups++;
                    return null;
                },
                processSourceText: () =>
                {
                    processCalls++;
                    return null;
                });
        }

        cacheLookups.Should().Be(2);
        processCalls.Should().Be(2);

        for (var i = 0; i < 5; i++)
        {
            cache.Resolve(
                $"Text {i}",
                "zh-Hans",
                "prompt-v5",
                "scene_01",
                nowSeconds: 0.1,
                frameId: 2,
                tryGetCachedTranslation: () =>
                {
                    cacheLookups++;
                    return null;
                },
                processSourceText: () =>
                {
                    processCalls++;
                    return null;
                });
        }

        cacheLookups.Should().Be(4);
        processCalls.Should().Be(4);
    }

    [Fact]
    public void Resolve_respects_new_source_time_spacing_across_fast_frames()
    {
        var cache = new ImguiTranslationStateCache(
            maxEntries: 128,
            maxNewItemsPerFrame: 8,
            maxRefreshesPerFrame: 4,
            pendingRefreshSeconds: 1,
            entryTtlSeconds: 60,
            minNewItemIntervalSeconds: 0.25);
        var cacheLookups = 0;
        var processCalls = 0;

        for (var i = 0; i < 4; i++)
        {
            cache.Resolve(
                $"Command {i}",
                "zh-Hans",
                "prompt-v5",
                "scene_01",
                nowSeconds: i * 0.05,
                frameId: i,
                tryGetCachedTranslation: () =>
                {
                    cacheLookups++;
                    return null;
                },
                processSourceText: () =>
                {
                    processCalls++;
                    return null;
                });
        }

        cacheLookups.Should().Be(1);
        processCalls.Should().Be(1);

        cache.Resolve(
            "Command 4",
            "zh-Hans",
            "prompt-v5",
            "scene_01",
            nowSeconds: 0.25,
            frameId: 5,
            tryGetCachedTranslation: () =>
            {
                cacheLookups++;
                return null;
            },
            processSourceText: () =>
            {
                processCalls++;
                return null;
            });

        cacheLookups.Should().Be(2);
        processCalls.Should().Be(2);
    }

    [Fact]
    public void Resolve_allows_burst_processing_within_one_frame_before_spacing_next_frame()
    {
        var cache = new ImguiTranslationStateCache(
            maxEntries: 128,
            maxNewItemsPerFrame: 16,
            maxRefreshesPerFrame: 4,
            pendingRefreshSeconds: 1,
            entryTtlSeconds: 60,
            minNewItemIntervalSeconds: 0.05);
        var processCalls = 0;

        for (var i = 0; i < 20; i++)
        {
            cache.Resolve(
                $"Command {i}",
                "zh-Hans",
                "prompt-v5",
                "scene_01",
                nowSeconds: 0,
                frameId: 1,
                tryGetCachedTranslation: () => null,
                processSourceText: () =>
                {
                    processCalls++;
                    return null;
                });
        }

        processCalls.Should().Be(16);

        cache.Resolve(
            "Command 16",
            "zh-Hans",
            "prompt-v5",
            "scene_01",
            nowSeconds: 0.04,
            frameId: 2,
            tryGetCachedTranslation: () => null,
            processSourceText: () =>
            {
                processCalls++;
                return null;
            });

        processCalls.Should().Be(16);

        cache.Resolve(
            "Command 16",
            "zh-Hans",
            "prompt-v5",
            "scene_01",
            nowSeconds: 0.05,
            frameId: 3,
            tryGetCachedTranslation: () => null,
            processSourceText: () =>
            {
                processCalls++;
                return null;
            });

        processCalls.Should().Be(17);
    }

    [Fact]
    public void Resolve_respects_pending_refresh_time_spacing_across_fast_frames()
    {
        var cache = new ImguiTranslationStateCache(
            maxEntries: 128,
            maxNewItemsPerFrame: 8,
            maxRefreshesPerFrame: 8,
            pendingRefreshSeconds: 0.1,
            entryTtlSeconds: 60,
            minRefreshIntervalSeconds: 0.25);
        var cacheLookups = 0;
        var processCalls = 0;

        for (var i = 0; i < 2; i++)
        {
            cache.Resolve(
                $"Command {i}",
                "zh-Hans",
                "prompt-v5",
                "scene_01",
                nowSeconds: 0,
                frameId: 1,
                tryGetCachedTranslation: () =>
                {
                    cacheLookups++;
                    return null;
                },
                processSourceText: () =>
                {
                    processCalls++;
                    return null;
                });
        }

        for (var i = 0; i < 2; i++)
        {
            cache.Resolve(
                $"Command {i}",
                "zh-Hans",
                "prompt-v5",
                "scene_01",
                nowSeconds: 0.15 + (i * 0.05),
                frameId: 2 + i,
                tryGetCachedTranslation: () =>
                {
                    cacheLookups++;
                    return null;
                },
                processSourceText: () =>
                {
                    processCalls++;
                    return null;
                });
        }

        cacheLookups.Should().Be(3);
        processCalls.Should().Be(2);

        cache.Resolve(
            "Command 1",
            "zh-Hans",
            "prompt-v5",
            "scene_01",
            nowSeconds: 0.4,
            frameId: 5,
            tryGetCachedTranslation: () =>
            {
                cacheLookups++;
                return null;
            },
            processSourceText: () =>
            {
                processCalls++;
                return null;
            });

        cacheLookups.Should().Be(4);
        processCalls.Should().Be(2);
    }

    [Fact]
    public void Resolve_retains_new_entry_when_capacity_trim_runs()
    {
        var cache = new ImguiTranslationStateCache(
            maxEntries: 16,
            maxNewItemsPerFrame: 32,
            maxRefreshesPerFrame: 4,
            pendingRefreshSeconds: 1,
            entryTtlSeconds: 60);
        var processCalls = 0;

        for (var i = 0; i < 17; i++)
        {
            cache.Resolve(
                $"Command {i}",
                "zh-Hans",
                "prompt-v5",
                "scene_01",
                nowSeconds: i,
                frameId: i,
                tryGetCachedTranslation: () => null,
                processSourceText: () =>
                {
                    processCalls++;
                    return null;
                });
        }

        cache.Resolve(
            "Command 16",
            "zh-Hans",
            "prompt-v5",
            "scene_01",
            nowSeconds: 17.1,
            frameId: 18,
            tryGetCachedTranslation: () => null,
            processSourceText: () =>
            {
                processCalls++;
                return null;
            });

        processCalls.Should().Be(17);
    }
}

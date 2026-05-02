using FluentAssertions;
using HUnityAutoTranslator.Core.Caching;
using HUnityAutoTranslator.Core.Configuration;
using HUnityAutoTranslator.Core.Control;
using HUnityAutoTranslator.Core.Dispatching;
using HUnityAutoTranslator.Core.Glossary;
using HUnityAutoTranslator.Core.Pipeline;
using HUnityAutoTranslator.Core.Prompts;
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

    [Fact]
    public async Task WorkerPool_keeps_idle_workers_available_for_jobs_enqueued_while_translation_is_running()
    {
        var queue = new TranslationJobQueue();
        var dispatcher = new ResultDispatcher();
        var provider = new BlockingProvider();
        var limiter = new ProviderRateLimiter(requestsPerMinute: 120);
        var metrics = new ControlPanelMetrics();
        var pool = new TranslationWorkerPool(queue, dispatcher, provider, limiter, RuntimeConfig.CreateDefault() with { MaxConcurrentRequests = 4 }, metrics: metrics);

        queue.Enqueue(TranslationJob.Create("a", "A", TranslationPriority.Normal));
        var runTask = pool.RunUntilIdleAsync(CancellationToken.None);

        try
        {
            await WaitUntilAsync(() => provider.StartedCount == 1, TimeSpan.FromSeconds(1));
            provider.StartedCount.Should().Be(1);

            queue.Enqueue(TranslationJob.Create("b", "B", TranslationPriority.Normal));
            queue.Enqueue(TranslationJob.Create("c", "C", TranslationPriority.Normal));
            queue.Enqueue(TranslationJob.Create("d", "D", TranslationPriority.Normal));

            await WaitUntilAsync(() => provider.MaxObservedConcurrency > 1, TimeSpan.FromSeconds(1));
            provider.MaxObservedConcurrency.Should().BeGreaterThan(1);
            metrics.Snapshot().InFlightTranslationCount.Should().BeGreaterThan(1);
        }
        finally
        {
            provider.Release();
            await runTask;
        }

        dispatcher.PendingCount.Should().Be(4);
    }

    [Fact]
    public async Task WorkerPool_allows_online_provider_to_reach_one_hundred_concurrent_requests()
    {
        var queue = new TranslationJobQueue();
        var dispatcher = new ResultDispatcher();
        var provider = new BlockingProvider();
        var limiter = new ProviderRateLimiter(requestsPerMinute: 6000);
        var metrics = new ControlPanelMetrics();
        var config = RuntimeConfig.CreateDefault() with { MaxConcurrentRequests = 100 };
        var pool = new TranslationWorkerPool(queue, dispatcher, provider, limiter, config, metrics: metrics);

        for (var i = 0; i < 100; i++)
        {
            queue.Enqueue(TranslationJob.Create($"target-{i}", $"Text {i:D3}", TranslationPriority.Normal));
        }

        var runTask = pool.RunUntilIdleAsync(CancellationToken.None);

        try
        {
            await WaitUntilAsync(() => provider.StartedCount == 100, TimeSpan.FromSeconds(2));
            provider.StartedCount.Should().Be(100);
            provider.MaxObservedConcurrency.Should().Be(100);
            metrics.Snapshot().InFlightTranslationCount.Should().Be(100);
        }
        finally
        {
            provider.Release();
            await runTask;
        }

        metrics.Snapshot().InFlightTranslationCount.Should().Be(0);
        dispatcher.PendingCount.Should().Be(100);
    }

    [Fact]
    public async Task WorkerPool_limits_llamacpp_effective_concurrency_to_parallel_slots()
    {
        var queue = new TranslationJobQueue();
        var dispatcher = new ResultDispatcher();
        var provider = new BlockingProvider(ProviderKind.LlamaCpp);
        var limiter = new ProviderRateLimiter(requestsPerMinute: 6000);
        var config = RuntimeConfig.CreateDefault() with
        {
            Provider = ProviderProfile.DefaultLlamaCpp(),
            MaxConcurrentRequests = 100,
            LlamaCpp = RuntimeConfig.CreateDefault().LlamaCpp with { ParallelSlots = 2 }
        };
        var pool = new TranslationWorkerPool(queue, dispatcher, provider, limiter, config);

        for (var i = 0; i < 10; i++)
        {
            queue.Enqueue(TranslationJob.Create($"target-{i}", $"Local {i:D2}", TranslationPriority.Normal));
        }

        var runTask = pool.RunUntilIdleAsync(CancellationToken.None);

        try
        {
            await WaitUntilAsync(() => provider.StartedCount == 2, TimeSpan.FromSeconds(1));
            provider.StartedCount.Should().Be(2);
            provider.MaxObservedConcurrency.Should().Be(2);
        }
        finally
        {
            provider.Release();
            await runTask;
        }

        dispatcher.PendingCount.Should().Be(10);
    }

    [Fact]
    public async Task WorkerPool_reports_in_flight_count_as_active_requests_not_batched_text_items()
    {
        var queue = new TranslationJobQueue();
        var dispatcher = new ResultDispatcher();
        var provider = new BlockingProvider(ProviderKind.LlamaCpp);
        var limiter = new ProviderRateLimiter(requestsPerMinute: 6000);
        var metrics = new ControlPanelMetrics();
        var config = RuntimeConfig.CreateDefault() with
        {
            Provider = ProviderProfile.DefaultLlamaCpp(),
            MaxConcurrentRequests = 100,
            LlamaCpp = RuntimeConfig.CreateDefault().LlamaCpp with { ParallelSlots = 1 }
        };
        var pool = new TranslationWorkerPool(queue, dispatcher, provider, limiter, config, metrics: metrics);

        for (var i = 0; i < 4; i++)
        {
            queue.Enqueue(TranslationJob.Create($"target-{i}", $"Local {i:D2}", TranslationPriority.Normal));
        }

        var runTask = pool.RunUntilIdleAsync(CancellationToken.None);

        try
        {
            await WaitUntilAsync(() => provider.StartedCount == 1, TimeSpan.FromSeconds(1));
            provider.StartedCount.Should().Be(1);
            provider.MaxObservedConcurrency.Should().Be(1);
            metrics.Snapshot().InFlightTranslationCount.Should().Be(1);
        }
        finally
        {
            provider.Release();
            await runTask;
        }

        metrics.Snapshot().InFlightTranslationCount.Should().Be(0);
        dispatcher.PendingCount.Should().Be(4);
    }

    [Fact]
    public async Task WorkerPool_reports_provider_exceptions_without_faulting_the_pool()
    {
        var queue = new TranslationJobQueue();
        var dispatcher = new ResultDispatcher();
        var failures = new List<string>();
        var metrics = new ControlPanelMetrics();
        var config = RuntimeConfig.CreateDefault() with { MaxConcurrentRequests = 2 };
        var pool = new TranslationWorkerPool(
            queue,
            dispatcher,
            new ThrowingProvider("network timeout"),
            new ProviderRateLimiter(600),
            config,
            metrics: metrics,
            failureReporter: failures.Add);

        queue.Enqueue(TranslationJob.Create("ui-1", "Start", TranslationPriority.VisibleUi));
        queue.Enqueue(TranslationJob.Create("ui-2", "Continue", TranslationPriority.VisibleUi));

        var act = () => pool.RunUntilIdleAsync(CancellationToken.None);

        await act.Should().NotThrowAsync();
        failures.Should().HaveCount(2);
        failures.Should().OnlyContain(message => message.Contains("network timeout", StringComparison.Ordinal));
        metrics.Snapshot().InFlightTranslationCount.Should().Be(0);
        dispatcher.PendingCount.Should().Be(0);
        queue.PendingCount.Should().Be(0);
        queue.InFlightCount.Should().Be(0);
    }

    [Fact]
    public async Task WorkerPool_builds_guarded_prompt_restores_placeholders_and_caches_results()
    {
        var queue = new TranslationJobQueue();
        var dispatcher = new ResultDispatcher();
        var cache = new MemoryTranslationCache();
        var provider = new CapturingProvider(new[] { "Hello translated __HUT_TOKEN_0__" });
        var limiter = new ProviderRateLimiter(requestsPerMinute: 120);
        var config = RuntimeConfig.CreateDefault() with { MaxConcurrentRequests = 1 };
        var pool = new TranslationWorkerPool(queue, dispatcher, provider, limiter, config, cache);

        queue.Enqueue(TranslationJob.Create("ui-1", "Hello {playerName}", TranslationPriority.VisibleUi));

        await pool.RunUntilIdleAsync(CancellationToken.None);

        provider.LastRequest.Should().NotBeNull();
        provider.LastRequest!.SystemPrompt.Should().Contain("Simplified Chinese");
        provider.LastRequest.SystemPrompt.Should().NotContain(config.TargetLanguage);
        provider.LastRequest.UserPrompt.Should().Contain("__HUT_TOKEN_0__");

        var results = dispatcher.Drain(10);
        results.Should().ContainSingle();
        results[0].TranslatedText.Should().Be("Hello translated {playerName}");

        var key = TranslationCacheKey.Create(
            "Hello {playerName}",
            config.TargetLanguage,
            config.Provider,
            TextPipeline.PromptPolicyVersion);
        cache.TryGet(key, TranslationCacheContext.Empty, out var cached).Should().BeTrue();
        cached.Should().Be("Hello translated {playerName}");
    }

    [Fact]
    public async Task WorkerPool_preserves_capture_context_when_caching_results()
    {
        var queue = new TranslationJobQueue();
        var dispatcher = new ResultDispatcher();
        var cache = new RecordingCache();
        var provider = new CapturingProvider(new[] { "Options translated" });
        var limiter = new ProviderRateLimiter(requestsPerMinute: 120);
        var config = RuntimeConfig.CreateDefault() with { MaxConcurrentRequests = 1 };
        var pool = new TranslationWorkerPool(queue, dispatcher, provider, limiter, config, cache);
        var context = new TranslationCacheContext("MainMenu", "Canvas/Menu/Options", "UnityEngine.UI.Text");

        queue.Enqueue(TranslationJob.Create("ui-1", "Options", TranslationPriority.VisibleUi, context));

        await pool.RunUntilIdleAsync(CancellationToken.None);

        cache.LastKey.Should().NotBeNull();
        cache.LastKey!.SourceText.Should().Be("Options");
        cache.LastContext.Should().Be(context);
        cache.LastTranslatedText.Should().Be("Options translated");
    }

    [Fact]
    public async Task WorkerPool_includes_translation_context_examples_in_provider_request()
    {
        var queue = new TranslationJobQueue();
        var dispatcher = new ResultDispatcher();
        var cache = new MemoryTranslationCache();
        var provider = new CapturingProvider(new[] { "Current translated" });
        var limiter = new ProviderRateLimiter(requestsPerMinute: 120);
        var config = RuntimeConfig.CreateDefault() with { MaxConcurrentRequests = 1 };
        var pool = new TranslationWorkerPool(queue, dispatcher, provider, limiter, config, cache);
        var context = new TranslationCacheContext("Menu", "Canvas/Dialog", "Text");
        cache.Update(SampleRow("Previous line", "Menu", "Canvas/Dialog", "Text", "Previous translated", DateTimeOffset.UtcNow));

        queue.Enqueue(TranslationJob.Create("ui-1", "Current line", TranslationPriority.VisibleUi, context));

        await pool.RunUntilIdleAsync(CancellationToken.None);

        provider.LastRequest.Should().NotBeNull();
        provider.LastRequest!.UserPrompt.Should().Contain("Translation context examples");
        provider.LastRequest.UserPrompt.Should().Contain("Previous line");
        provider.LastRequest.UserPrompt.Should().Contain("Previous translated");
    }

    [Fact]
    public async Task WorkerPool_batches_short_ui_text_and_includes_per_item_context()
    {
        var queue = new TranslationJobQueue();
        var dispatcher = new ResultDispatcher();
        var cache = new MemoryTranslationCache();
        var provider = new CapturingProvider(new[] { "超高", "开启" });
        var limiter = new ProviderRateLimiter(requestsPerMinute: 120);
        var config = RuntimeConfig.CreateDefault() with
        {
            MaxConcurrentRequests = 1,
            MaxBatchCharacters = 1800,
            GameTitle = "The Glitched Attraction"
        };
        var pool = new TranslationWorkerPool(queue, dispatcher, provider, limiter, config, cache);
        var now = DateTimeOffset.UtcNow;
        cache.Update(SampleRow("Textures", "MainMenu", "Canvas/Settings/Textures/Label", "Text", "纹理", now.AddMinutes(3)));
        cache.Update(SampleRow("Fullscreen", "MainMenu", "Canvas/Settings/Toggles/FullscreenLabel", "Text", "全屏", now.AddMinutes(2)));

        queue.Enqueue(TranslationJob.Create(
            "ui-1",
            "Ultra",
            TranslationPriority.VisibleUi,
            new TranslationCacheContext("MainMenu", "Canvas/Settings/Textures/QualityValue", "TMPro.TextMeshProUGUI")));
        queue.Enqueue(TranslationJob.Create(
            "ui-2",
            "On",
            TranslationPriority.VisibleUi,
            new TranslationCacheContext("MainMenu", "Canvas/Settings/Toggles/FullscreenValue", "UnityEngine.UI.Text")));

        await pool.RunUntilIdleAsync(CancellationToken.None);

        provider.Requests.Should().ContainSingle();
        var request = provider.Requests[0];
        request.ProtectedTexts.Should().Equal("Ultra", "On");
        request.SystemPrompt.Should().Contain("Game title: The Glitched Attraction.");
        request.UserPrompt.Should().Contain("\"text_index\":0");
        request.UserPrompt.Should().Contain("\"component_hierarchy\":\"Canvas/Settings/Textures/QualityValue\"");
        request.UserPrompt.Should().Contain("\"text_index\":1");
        request.UserPrompt.Should().Contain("\"component_hierarchy\":\"Canvas/Settings/Toggles/FullscreenValue\"");
        request.UserPrompt.Should().Contain("Textures");
        request.UserPrompt.Should().Contain("纹理");
        dispatcher.Drain(10).Select(result => result.TranslatedText).Should().Equal("超高", "开启");
    }

    [Fact]
    public async Task WorkerPool_batches_sixteen_short_imgui_texts_in_one_request()
    {
        var queue = new TranslationJobQueue();
        var dispatcher = new ResultDispatcher();
        var cache = new MemoryTranslationCache();
        var provider = new CapturingProvider(Enumerable.Range(0, 16).Select(index => "\u8bd1" + index).ToArray());
        var config = RuntimeConfig.CreateDefault() with
        {
            MaxConcurrentRequests = 1,
            MaxBatchCharacters = 1800
        };
        var pool = new TranslationWorkerPool(queue, dispatcher, provider, new ProviderRateLimiter(600), config, cache);

        for (var i = 0; i < 16; i++)
        {
            queue.Enqueue(TranslationJob.Create(
                $"imgui-{i}",
                $"Command {i}",
                TranslationPriority.VisibleUi,
                new TranslationCacheContext("title_01", null, "IMGUI"),
                publishResult: false));
        }

        await pool.RunUntilIdleAsync(CancellationToken.None);

        provider.Requests.Should().ContainSingle();
        provider.Requests[0].ProtectedTexts.Should().HaveCount(16);
        dispatcher.PendingCount.Should().Be(0);
    }

    [Fact]
    public async Task WorkerPool_emits_debug_snapshot_for_quality_context()
    {
        var queue = new TranslationJobQueue();
        var dispatcher = new ResultDispatcher();
        var cache = new MemoryTranslationCache();
        var provider = new CapturingProvider(new[] { "超高", "开启" });
        var snapshots = new List<TranslationRequestDebugSnapshot>();
        var config = RuntimeConfig.CreateDefault() with
        {
            MaxConcurrentRequests = 1,
            MaxBatchCharacters = 1800,
            GameTitle = "The Glitched Attraction"
        };
        var pool = new TranslationWorkerPool(
            queue,
            dispatcher,
            provider,
            new ProviderRateLimiter(120),
            config,
            cache,
            debugReporter: snapshots.Add);

        queue.Enqueue(TranslationJob.Create(
            "ui-1",
            "Ultra",
            TranslationPriority.VisibleUi,
            new TranslationCacheContext("MainMenu", "Canvas/Settings/Textures/QualityValue", "TMPro.TextMeshProUGUI")));
        queue.Enqueue(TranslationJob.Create(
            "ui-2",
            "On",
            TranslationPriority.VisibleUi,
            new TranslationCacheContext("MainMenu", "Canvas/Settings/Toggles/FullscreenValue", "UnityEngine.UI.Text")));

        await pool.RunUntilIdleAsync(CancellationToken.None);

        snapshots.Should().ContainSingle();
        var snapshot = snapshots[0];
        snapshot.Phase.Should().Be("translate");
        snapshot.PromptPolicyVersion.Should().Be("prompt-v5");
        snapshot.GameTitle.Should().Be("The Glitched Attraction");
        snapshot.Items.Should().HaveCount(2);
        snapshot.Items[0].SourceText.Should().Be("Ultra");
        snapshot.Items[0].Hints.Should().Contain("settings_value");
        snapshot.Items[1].SourceText.Should().Be("On");
        snapshot.Items[1].Hints.Should().Contain("toggle_state");
        snapshot.ItemContextsIncluded.Should().BeTrue();
        snapshot.QualityRulesEnabled.Should().BeTrue();
    }

    [Fact]
    public async Task WorkerPool_debug_snapshot_reports_quality_rules_disabled()
    {
        var queue = new TranslationJobQueue();
        var dispatcher = new ResultDispatcher();
        var cache = new MemoryTranslationCache();
        var provider = new CapturingProvider(new[] { "\u8d85" });
        var snapshots = new List<TranslationRequestDebugSnapshot>();
        var config = RuntimeConfig.CreateDefault() with
        {
            MaxConcurrentRequests = 1,
            TranslationQuality = TranslationQualityConfig.Default() with { Enabled = false }
        };
        var pool = new TranslationWorkerPool(
            queue,
            dispatcher,
            provider,
            new ProviderRateLimiter(120),
            config,
            cache,
            debugReporter: snapshots.Add);

        queue.Enqueue(TranslationJob.Create(
            "ui-1",
            "Ultra",
            TranslationPriority.VisibleUi,
            new TranslationCacheContext("MainMenu", "Canvas/Settings/Textures/QualityValue", "TMPro.TextMeshProUGUI")));

        await pool.RunUntilIdleAsync(CancellationToken.None);

        snapshots.Should().ContainSingle();
        snapshots[0].QualityRulesEnabled.Should().BeFalse();
        dispatcher.PendingCount.Should().Be(1);
    }

    [Fact]
    public async Task WorkerPool_repairs_translation_once_when_quality_rule_fails()
    {
        var queue = new TranslationJobQueue();
        var dispatcher = new ResultDispatcher();
        var cache = new MemoryTranslationCache();
        var provider = new SequencedProvider(new[]
        {
            new[] { "超" },
            new[] { "超高" }
        });
        var snapshots = new List<TranslationRequestDebugSnapshot>();
        var config = RuntimeConfig.CreateDefault() with
        {
            MaxConcurrentRequests = 1,
            GameTitle = "The Glitched Attraction"
        };
        var pool = new TranslationWorkerPool(
            queue,
            dispatcher,
            provider,
            new ProviderRateLimiter(120),
            config,
            cache,
            debugReporter: snapshots.Add);

        var context = new TranslationCacheContext("Main Menu", "Menu/Camera/Canvas/Settings Menu/Gameplay Panel/Textures/Text", "TMPro.TextMeshProUGUI");
        queue.Enqueue(TranslationJob.Create("ui-1", "Ultra", TranslationPriority.VisibleUi, context));

        await pool.RunUntilIdleAsync(CancellationToken.None);

        provider.Requests.Should().HaveCount(2);
        provider.Requests[1].UserPrompt.Should().Contain("translation quality rules");
        provider.Requests[1].UserPrompt.Should().Contain("too short");
        snapshots.Should().HaveCount(2);
        snapshots[1].Phase.Should().Be("quality-repair");
        snapshots[1].RepairReason.Should().Be("设置值译文过短或不完整");
        var results = dispatcher.Drain(10);
        results.Should().ContainSingle();
        results[0].TranslatedText.Should().Be("超高");
        var key = TranslationCacheKey.Create("Ultra", config.TargetLanguage, config.Provider, TextPipeline.PromptPolicyVersion);
        cache.TryGet(key, context, out var cached).Should().BeTrue();
        cached.Should().Be("超高");
    }

    [Fact]
    public async Task WorkerPool_skips_quality_repair_when_disabled()
    {
        var queue = new TranslationJobQueue();
        var dispatcher = new ResultDispatcher();
        var cache = new MemoryTranslationCache();
        var failures = new List<string>();
        var provider = new SequencedProvider(new[]
        {
            new[] { "\u8d85" }
        });
        var config = RuntimeConfig.CreateDefault() with
        {
            MaxConcurrentRequests = 1,
            GameTitle = "The Glitched Attraction",
            TranslationQuality = TranslationQualityConfig.Default() with { Mode = "custom", EnableRepair = false }
        };
        var pool = new TranslationWorkerPool(
            queue,
            dispatcher,
            provider,
            new ProviderRateLimiter(120),
            config,
            cache,
            failureReporter: failures.Add);

        var context = new TranslationCacheContext("Main Menu", "Menu/Camera/Canvas/Settings Menu/Gameplay Panel/Textures/Text", "TMPro.TextMeshProUGUI");
        queue.Enqueue(TranslationJob.Create("ui-1", "Ultra", TranslationPriority.VisibleUi, context));

        await pool.RunUntilIdleAsync(CancellationToken.None);

        provider.Requests.Should().ContainSingle();
        queue.DeferredCount.Should().Be(1);
        dispatcher.PendingCount.Should().Be(0);
        failures.Should().ContainSingle(message => message.Contains("1/3", StringComparison.Ordinal));
    }

    [Fact]
    public async Task WorkerPool_requeues_quality_failures_until_valid_translation_is_returned()
    {
        var queue = new TranslationJobQueue();
        var dispatcher = new ResultDispatcher();
        var cache = new MemoryTranslationCache();
        var failures = new List<string>();
        var snapshots = new List<TranslationRequestDebugSnapshot>();
        var provider = new SequencedProvider(new[]
        {
            new[] { "超" },
            new[] { "超" },
            new[] { "超高" }
        });
        var config = RuntimeConfig.CreateDefault() with
        {
            MaxConcurrentRequests = 1,
            GameTitle = "The Glitched Attraction"
        };
        var pool = new TranslationWorkerPool(
            queue,
            dispatcher,
            provider,
            new ProviderRateLimiter(120),
            config,
            cache,
            failureReporter: failures.Add,
            debugReporter: snapshots.Add);

        var context = new TranslationCacheContext("Main Menu", "Menu/Camera/Canvas/Settings Menu/Gameplay Panel/Textures/Text", "TMPro.TextMeshProUGUI");
        queue.Enqueue(TranslationJob.Create("ui-1", "Ultra", TranslationPriority.VisibleUi, context));

        await pool.RunUntilIdleAsync(CancellationToken.None);

        provider.Requests.Should().HaveCount(2);
        queue.PendingCount.Should().Be(0);
        queue.DeferredCount.Should().Be(1);
        dispatcher.PendingCount.Should().Be(0);
        cache.TryGet(
            TranslationCacheKey.Create("Ultra", config.TargetLanguage, config.Provider, TextPipeline.PromptPolicyVersion),
            context,
            out _).Should().BeFalse();
        var retryFailure = failures.Should().ContainSingle().Which;
        retryFailure.Should().Contain("Ultra");
        retryFailure.Should().Contain("1/3");
        retryFailure.Should().Contain("队列");
        snapshots.Should().Contain(snapshot =>
            snapshot.Phase == "quality-repair" &&
            snapshot.Items[0].CandidateTranslation == "超" &&
            snapshot.Items[0].QualityFailureReason == "设置值译文过短或不完整" &&
            snapshot.Items[0].QualityRetryCount == 0);

        queue.PromoteDeferred().Should().Be(1);
        await pool.RunUntilIdleAsync(CancellationToken.None);

        provider.Requests.Should().HaveCount(3);
        var results = dispatcher.Drain(10);
        results.Should().ContainSingle();
        results[0].TranslatedText.Should().Be("超高");
        var key = TranslationCacheKey.Create("Ultra", config.TargetLanguage, config.Provider, TextPipeline.PromptPolicyVersion);
        cache.TryGet(key, context, out var cached).Should().BeTrue();
        cached.Should().Be("超高");
    }

    [Fact]
    public async Task WorkerPool_stops_requeueing_after_quality_retry_limit()
    {
        var queue = new TranslationJobQueue();
        var dispatcher = new ResultDispatcher();
        var cache = new MemoryTranslationCache();
        var failures = new List<string>();
        var exhaustedJobs = new List<TranslationJob>();
        var provider = new SequencedProvider(new[]
        {
            new[] { "超" },
            new[] { "超" },
            new[] { "超" },
            new[] { "超" },
            new[] { "超" },
            new[] { "超" },
            new[] { "超" },
            new[] { "超" }
        });
        var config = RuntimeConfig.CreateDefault() with
        {
            MaxConcurrentRequests = 1,
            GameTitle = "The Glitched Attraction"
        };
        var pool = new TranslationWorkerPool(
            queue,
            dispatcher,
            provider,
            new ProviderRateLimiter(120),
            config,
            cache,
            failureReporter: failures.Add,
            qualityRetryLimitReporter: exhaustedJobs.Add);

        var context = new TranslationCacheContext("Main Menu", "Menu/Camera/Canvas/Settings Menu/Gameplay Panel/Textures/Text", "TMPro.TextMeshProUGUI");
        queue.Enqueue(TranslationJob.Create("ui-1", "Ultra", TranslationPriority.VisibleUi, context));

        await RunPoolUntilNoDeferredRetriesAsync(pool, queue);

        provider.Requests.Should().HaveCount(8);
        var key = TranslationCacheKey.Create("Ultra", config.TargetLanguage, config.Provider, TextPipeline.PromptPolicyVersion);
        cache.TryGet(key, context, out _).Should().BeFalse();
        dispatcher.PendingCount.Should().Be(0);
        failures.Count(message => message.Contains("队列", StringComparison.Ordinal)).Should().Be(3);
        failures.Should().ContainSingle(message =>
            message == "质量失败：设置值译文过短或不完整。原文：Ultra。译文：超。重试：3/3，已达上限，保留为待翻译。");
        exhaustedJobs.Should().ContainSingle(job =>
            job.SourceText == "Ultra" &&
            job.Context == context &&
            job.QualityRetryCount == 3);
    }

    [Fact]
    public async Task WorkerPool_uses_configured_quality_retry_limit()
    {
        var queue = new TranslationJobQueue();
        var dispatcher = new ResultDispatcher();
        var cache = new MemoryTranslationCache();
        var failures = new List<string>();
        var exhaustedJobs = new List<TranslationJob>();
        var provider = new SequencedProvider(new[]
        {
            new[] { "\u8d85" },
            new[] { "\u8d85" },
            new[] { "\u8d85" },
            new[] { "\u8d85" }
        });
        var config = RuntimeConfig.CreateDefault() with
        {
            MaxConcurrentRequests = 1,
            GameTitle = "The Glitched Attraction",
            TranslationQuality = TranslationQualityConfig.Default() with { Mode = "custom", MaxRetryCount = 1 }
        };
        var pool = new TranslationWorkerPool(
            queue,
            dispatcher,
            provider,
            new ProviderRateLimiter(120),
            config,
            cache,
            failureReporter: failures.Add,
            qualityRetryLimitReporter: exhaustedJobs.Add);

        var context = new TranslationCacheContext("Main Menu", "Menu/Camera/Canvas/Settings Menu/Gameplay Panel/Textures/Text", "TMPro.TextMeshProUGUI");
        queue.Enqueue(TranslationJob.Create("ui-1", "Ultra", TranslationPriority.VisibleUi, context));

        await RunPoolUntilNoDeferredRetriesAsync(pool, queue);

        provider.Requests.Should().HaveCount(4);
        failures.Should().HaveCount(2);
        failures.Should().OnlyContain(message => message.Contains("1/1", StringComparison.Ordinal));
        exhaustedJobs.Should().ContainSingle(job => job.QualityRetryCount == 1);
    }

    [Fact]
    public async Task WorkerPool_caches_valid_items_when_one_item_in_batch_fails_quality_repair()
    {
        var queue = new TranslationJobQueue();
        var dispatcher = new ResultDispatcher();
        var cache = new MemoryTranslationCache();
        var failures = new List<string>();
        var provider = new SequencedProvider(new[]
        {
            new[] { "\u8d85", "\u5b57\u5e55" },
            new[] { "\u8d85" },
            new[] { "\u8d85\u9ad8" }
        });
        var config = RuntimeConfig.CreateDefault() with
        {
            MaxConcurrentRequests = 1,
            GameTitle = "The Glitched Attraction"
        };
        var pool = new TranslationWorkerPool(
            queue,
            dispatcher,
            provider,
            new ProviderRateLimiter(120),
            config,
            cache,
            failureReporter: failures.Add);
        var ultraContext = new TranslationCacheContext("Main Menu", "Menu/Camera/Canvas/Settings Menu/Gameplay Panel/Textures/Text", "TMPro.TextMeshProUGUI");
        var subtitlesContext = new TranslationCacheContext("Main Menu", "Menu/Camera/Canvas/Settings Menu/Screen Panel/Subtitles/Name", "UnityEngine.UI.Text");

        queue.Enqueue(TranslationJob.Create("ultra", "Ultra", TranslationPriority.VisibleUi, ultraContext));
        queue.Enqueue(TranslationJob.Create("subtitles", "Subtitles", TranslationPriority.VisibleUi, subtitlesContext));

        await pool.RunUntilIdleAsync(CancellationToken.None);

        provider.Requests.Should().HaveCount(2);
        queue.DeferredCount.Should().Be(1);
        queue.PromoteDeferred().Should().Be(1);
        await pool.RunUntilIdleAsync(CancellationToken.None);

        provider.Requests.Should().HaveCount(3);
        var ultraKey = TranslationCacheKey.Create("Ultra", config.TargetLanguage, config.Provider, TextPipeline.PromptPolicyVersion);
        cache.TryGet(ultraKey, ultraContext, out var ultraCached).Should().BeTrue();
        ultraCached.Should().Be("\u8d85\u9ad8");
        var subtitlesKey = TranslationCacheKey.Create("Subtitles", config.TargetLanguage, config.Provider, TextPipeline.PromptPolicyVersion);
        cache.TryGet(subtitlesKey, subtitlesContext, out var cached).Should().BeTrue();
        cached.Should().Be("\u5b57\u5e55");
        dispatcher.Drain(10).Select(result => result.TranslatedText).Should().Equal("\u5b57\u5e55", "\u8d85\u9ad8");
        failures.Should().ContainSingle(message =>
            message.Contains("Ultra", StringComparison.Ordinal) &&
            message.Contains("1/3", StringComparison.Ordinal) &&
            message.Contains("队列", StringComparison.Ordinal));
    }

    [Fact]
    public async Task WorkerPool_normalizes_escaped_control_characters_when_source_uses_real_line_breaks()
    {
        var queue = new TranslationJobQueue();
        var dispatcher = new ResultDispatcher();
        var cache = new MemoryTranslationCache();
        var provider = new SequencedProvider(new[]
        {
            new[] { "\u7b2c\u4e00\u884c\\n\u7b2c\u4e8c\u884c" }
        });
        var config = RuntimeConfig.CreateDefault() with { MaxConcurrentRequests = 1 };
        var pool = new TranslationWorkerPool(
            queue,
            dispatcher,
            provider,
            new ProviderRateLimiter(120),
            config,
            cache);
        var source = "First line\nSecond line";
        var context = new TranslationCacheContext("Menu", "Canvas/Description", "UnityEngine.UI.Text");

        queue.Enqueue(TranslationJob.Create("description", source, TranslationPriority.VisibleUi, context));

        await pool.RunUntilIdleAsync(CancellationToken.None);

        var key = TranslationCacheKey.Create(source, config.TargetLanguage, config.Provider, TextPipeline.PromptPolicyVersion);
        cache.TryGet(key, context, out var cached).Should().BeTrue();
        cached.Should().Be("\u7b2c\u4e00\u884c\n\u7b2c\u4e8c\u884c");
    }

    [Fact]
    public async Task WorkerPool_repairs_translation_once_when_generated_outer_symbols_fail_quality_rules()
    {
        var queue = new TranslationJobQueue();
        var dispatcher = new ResultDispatcher();
        var cache = new MemoryTranslationCache();
        var provider = new SequencedProvider(new[]
        {
            new[] { "\u3010\u91cd\u8981\u63d0\u793a\u3011" },
            new[] { "\u91cd\u8981\u63d0\u793a" }
        });
        var config = RuntimeConfig.CreateDefault() with
        {
            MaxConcurrentRequests = 1,
            GameTitle = "The Glitched Attraction"
        };
        var pool = new TranslationWorkerPool(
            queue,
            dispatcher,
            provider,
            new ProviderRateLimiter(120),
            config,
            cache);

        var context = new TranslationCacheContext("Disclaimer", "Canvas/Text (Legacy)", "UnityEngine.UI.Text");
        queue.Enqueue(TranslationJob.Create("ui-1", "IMPORTANT", TranslationPriority.VisibleUi, context));

        await pool.RunUntilIdleAsync(CancellationToken.None);

        provider.Requests.Should().HaveCount(2);
        provider.Requests[1].UserPrompt.Should().Contain("outer symbols");
        var results = dispatcher.Drain(10);
        results.Should().ContainSingle();
        results[0].TranslatedText.Should().Be("\u91cd\u8981\u63d0\u793a");
        var key = TranslationCacheKey.Create("IMPORTANT", config.TargetLanguage, config.Provider, TextPipeline.PromptPolicyVersion);
        cache.TryGet(key, context, out var cached).Should().BeTrue();
        cached.Should().Be("\u91cd\u8981\u63d0\u793a");
    }

    [Fact]
    public async Task WorkerPool_preserves_ui_marker_prefix_when_provider_omits_it()
    {
        var queue = new TranslationJobQueue();
        var dispatcher = new ResultDispatcher();
        var cache = new MemoryTranslationCache();
        var provider = new CapturingProvider(new[] { "\u52a0\u5165\u961f\u4f0d" });
        var config = RuntimeConfig.CreateDefault() with { MaxConcurrentRequests = 1 };
        var pool = new TranslationWorkerPool(
            queue,
            dispatcher,
            provider,
            new ProviderRateLimiter(120),
            config,
            cache);

        var context = new TranslationCacheContext("Lobby", "Canvas/Menu/JoinCrew", "UnityEngine.UI.Text");
        queue.Enqueue(TranslationJob.Create("ui-1", "> Join a crew", TranslationPriority.VisibleUi, context));

        await pool.RunUntilIdleAsync(CancellationToken.None);

        var results = dispatcher.Drain(10);
        results.Should().ContainSingle();
        results[0].TranslatedText.Should().Be("> \u52a0\u5165\u961f\u4f0d");
        var key = TranslationCacheKey.Create("> Join a crew", config.TargetLanguage, config.Provider, TextPipeline.PromptPolicyVersion);
        cache.TryGet(key, context, out var cached).Should().BeTrue();
        cached.Should().Be("> \u52a0\u5165\u961f\u4f0d");
    }

    [Fact]
    public async Task WorkerPool_does_not_cache_translation_when_outer_symbol_repair_fails()
    {
        var queue = new TranslationJobQueue();
        var dispatcher = new ResultDispatcher();
        var cache = new MemoryTranslationCache();
        var failures = new List<string>();
        var provider = new SequencedProvider(new[]
        {
            new[] { "\u3010\u91cd\u8981\u63d0\u793a\u3011" },
            new[] { "\u3010\u91cd\u8981\u63d0\u793a\u3011" },
            new[] { "\u3010\u91cd\u8981\u63d0\u793a\u3011" },
            new[] { "\u3010\u91cd\u8981\u63d0\u793a\u3011" },
            new[] { "\u3010\u91cd\u8981\u63d0\u793a\u3011" },
            new[] { "\u3010\u91cd\u8981\u63d0\u793a\u3011" },
            new[] { "\u3010\u91cd\u8981\u63d0\u793a\u3011" },
            new[] { "\u3010\u91cd\u8981\u63d0\u793a\u3011" }
        });
        var config = RuntimeConfig.CreateDefault() with
        {
            MaxConcurrentRequests = 1,
            GameTitle = "The Glitched Attraction"
        };
        var pool = new TranslationWorkerPool(
            queue,
            dispatcher,
            provider,
            new ProviderRateLimiter(120),
            config,
            cache,
            failureReporter: failures.Add);

        var context = new TranslationCacheContext("Disclaimer", "Canvas/Text (Legacy)", "UnityEngine.UI.Text");
        queue.Enqueue(TranslationJob.Create("ui-1", "IMPORTANT", TranslationPriority.VisibleUi, context));

        await RunPoolUntilNoDeferredRetriesAsync(pool, queue);

        provider.Requests.Should().HaveCount(8);
        var key = TranslationCacheKey.Create("IMPORTANT", config.TargetLanguage, config.Provider, TextPipeline.PromptPolicyVersion);
        cache.TryGet(key, context, out _).Should().BeFalse();
        dispatcher.PendingCount.Should().Be(0);
        failures.Count(message => message.Contains("队列", StringComparison.Ordinal)).Should().Be(3);
        failures.Should().ContainSingle(message =>
            message == "质量失败：译文添加了原文没有的外层符号。原文：IMPORTANT。译文：【重要提示】。重试：3/3，已达上限，保留为待翻译。");
    }

    [Fact]
    public async Task WorkerPool_repairs_same_parent_option_collision()
    {
        var queue = new TranslationJobQueue();
        var dispatcher = new ResultDispatcher();
        var cache = new MemoryTranslationCache();
        var provider = new SequencedProvider(new[]
        {
            new[] { "相同选项", "相同选项" },
            new[] { "第二选项" }
        });
        var config = RuntimeConfig.CreateDefault() with { MaxConcurrentRequests = 1 };
        var pool = new TranslationWorkerPool(queue, dispatcher, provider, new ProviderRateLimiter(120), config, cache);

        queue.Enqueue(TranslationJob.Create(
            "ui-1",
            "Option Alpha",
            TranslationPriority.VisibleUi,
            new TranslationCacheContext("Settings", "Canvas/Options/OptionAlpha", "UnityEngine.UI.Text")));
        queue.Enqueue(TranslationJob.Create(
            "ui-2",
            "Option Beta",
            TranslationPriority.VisibleUi,
            new TranslationCacheContext("Settings", "Canvas/Options/OptionBeta", "UnityEngine.UI.Text")));

        await pool.RunUntilIdleAsync(CancellationToken.None);

        provider.Requests.Should().HaveCount(2);
        provider.Requests[1].UserPrompt.Should().Contain("same parent");
        provider.Requests[1].UserPrompt.Should().Contain("Option Alpha");
        provider.Requests[1].UserPrompt.Should().Contain("Option Beta");
        dispatcher.Drain(10).Select(result => result.TranslatedText).Should().Equal("相同选项", "第二选项");
    }

    [Fact]
    public async Task WorkerPool_omits_translation_context_when_disabled()
    {
        var queue = new TranslationJobQueue();
        var dispatcher = new ResultDispatcher();
        var cache = new MemoryTranslationCache();
        var provider = new CapturingProvider(new[] { "Current translated" });
        var limiter = new ProviderRateLimiter(requestsPerMinute: 120);
        var config = RuntimeConfig.CreateDefault() with { MaxConcurrentRequests = 1, EnableTranslationContext = false };
        var pool = new TranslationWorkerPool(queue, dispatcher, provider, limiter, config, cache);
        var context = new TranslationCacheContext("Menu", "Canvas/Dialog", "Text");
        cache.Update(SampleRow("Previous line", "Menu", "Canvas/Dialog", "Text", "Previous translated", DateTimeOffset.UtcNow));

        queue.Enqueue(TranslationJob.Create("ui-1", "Current line", TranslationPriority.VisibleUi, context));

        await pool.RunUntilIdleAsync(CancellationToken.None);

        provider.LastRequest.Should().NotBeNull();
        provider.LastRequest!.UserPrompt.Should().NotContain("Translation context examples");
    }

    [Fact]
    public async Task WorkerPool_includes_glossary_terms_in_provider_request()
    {
        var queue = new TranslationJobQueue();
        var dispatcher = new ResultDispatcher();
        var cache = new MemoryTranslationCache();
        var glossary = new MemoryGlossaryStore();
        glossary.UpsertManual(GlossaryTerm.CreateManual("Freddy", "弗雷迪", "zh-Hans", "角色名"));
        var provider = new CapturingProvider(new[] { "找到弗雷迪" });
        var config = RuntimeConfig.CreateDefault() with { MaxConcurrentRequests = 1 };
        var pool = new TranslationWorkerPool(queue, dispatcher, provider, new ProviderRateLimiter(120), config, cache, metrics: null, glossary: glossary);

        queue.Enqueue(TranslationJob.Create("ui-1", "Find Freddy", TranslationPriority.VisibleUi));

        await pool.RunUntilIdleAsync(CancellationToken.None);

        provider.LastRequest.Should().NotBeNull();
        provider.LastRequest!.SystemPrompt.Should().Contain("Mandatory glossary policy");
        provider.LastRequest.UserPrompt.Should().Contain("Mandatory glossary terms");
        provider.LastRequest.UserPrompt.Should().Contain("Freddy");
        provider.LastRequest.UserPrompt.Should().Contain("弗雷迪");
    }

    [Fact]
    public async Task WorkerPool_repairs_translation_once_when_required_glossary_term_is_missing()
    {
        var queue = new TranslationJobQueue();
        var dispatcher = new ResultDispatcher();
        var cache = new MemoryTranslationCache();
        var glossary = new MemoryGlossaryStore();
        glossary.UpsertManual(GlossaryTerm.CreateManual("Freddy", "弗雷迪", "zh-Hans", null));
        var provider = new SequencedProvider(new[]
        {
            new[] { "找到佛莱迪" },
            new[] { "找到弗雷迪" }
        });
        var config = RuntimeConfig.CreateDefault() with { MaxConcurrentRequests = 1 };
        var pool = new TranslationWorkerPool(queue, dispatcher, provider, new ProviderRateLimiter(120), config, cache, metrics: null, glossary: glossary);

        queue.Enqueue(TranslationJob.Create("ui-1", "Find Freddy", TranslationPriority.VisibleUi));

        await pool.RunUntilIdleAsync(CancellationToken.None);

        provider.Requests.Should().HaveCount(2);
        provider.Requests[1].UserPrompt.Should().Contain("The previous translation missed required glossary terms");
        var results = dispatcher.Drain(10);
        results.Should().ContainSingle();
        results[0].TranslatedText.Should().Be("找到弗雷迪");
    }

    [Fact]
    public async Task WorkerPool_does_not_cache_translation_when_glossary_repair_fails()
    {
        var queue = new TranslationJobQueue();
        var dispatcher = new ResultDispatcher();
        var cache = new MemoryTranslationCache();
        var glossary = new MemoryGlossaryStore();
        glossary.UpsertManual(GlossaryTerm.CreateManual("Freddy", "弗雷迪", "zh-Hans", null));
        var provider = new SequencedProvider(new[]
        {
            new[] { "找到佛莱迪" },
            new[] { "还是佛莱迪" }
        });
        var config = RuntimeConfig.CreateDefault() with { MaxConcurrentRequests = 1 };
        var pool = new TranslationWorkerPool(queue, dispatcher, provider, new ProviderRateLimiter(120), config, cache, metrics: null, glossary: glossary);

        queue.Enqueue(TranslationJob.Create("ui-1", "Find Freddy", TranslationPriority.VisibleUi));

        await pool.RunUntilIdleAsync(CancellationToken.None);

        var key = TranslationCacheKey.Create("Find Freddy", config.TargetLanguage, config.Provider, TextPipeline.PromptPolicyVersion);
        cache.TryGet(key, TranslationCacheContext.Empty, out _).Should().BeFalse();
        dispatcher.PendingCount.Should().Be(0);
    }

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
        var provider = new CapturingProvider(new[] { "Start translated" });
        var pool = new TranslationWorkerPool(queue, dispatcher, provider, new ProviderRateLimiter(600), config, cache, metrics);

        await pool.RunUntilIdleAsync(CancellationToken.None);

        var snapshot = metrics.Snapshot();
        snapshot.CompletedTranslationCount.Should().Be(1);
        snapshot.RecentTranslations.Should().ContainSingle(item => item.SourceText == "Start Game" && item.TranslatedText == "Start translated");
    }

    [Fact]
    public async Task WorkerPool_can_complete_resumed_pending_job_without_dispatching_writeback()
    {
        var queue = new TranslationJobQueue();
        var dispatcher = new ResultDispatcher();
        var cache = new MemoryTranslationCache();
        var provider = new CapturingProvider(new[] { "Continue translated" });
        var config = RuntimeConfig.CreateDefault() with { MaxConcurrentRequests = 1 };
        var pool = new TranslationWorkerPool(queue, dispatcher, provider, new ProviderRateLimiter(600), config, cache);

        queue.Enqueue(TranslationJob.Create(
            "pending:Continue",
            "Continue",
            TranslationPriority.Normal,
            new TranslationCacheContext("Menu", "Canvas/Continue", "Text"),
            publishResult: false));

        await pool.RunUntilIdleAsync(CancellationToken.None);

        var key = TranslationCacheKey.Create("Continue", config.TargetLanguage, config.Provider, TextPipeline.PromptPolicyVersion);
        cache.TryGet(key, new TranslationCacheContext("Menu", "Canvas/Continue", "Text"), out var cached).Should().BeTrue();
        cached.Should().Be("Continue translated");
        dispatcher.PendingCount.Should().Be(0);
    }

    [Fact]
    public async Task WorkerPool_reassembles_split_single_item_response_before_validation_and_cache_write()
    {
        var queue = new TranslationJobQueue();
        var dispatcher = new ResultDispatcher();
        var cache = new MemoryTranslationCache();
        var provider = new CapturingProvider(new[]
        {
            "第一段。",
            "第二段。"
        });
        var config = RuntimeConfig.CreateDefault() with { MaxConcurrentRequests = 1 };
        var pool = new TranslationWorkerPool(queue, dispatcher, provider, new ProviderRateLimiter(600), config, cache);
        var source = "First paragraph.\n\nSecond paragraph.";

        queue.Enqueue(TranslationJob.Create("ui-1", source, TranslationPriority.VisibleUi));

        await pool.RunUntilIdleAsync(CancellationToken.None);

        var key = TranslationCacheKey.Create(source, config.TargetLanguage, config.Provider, TextPipeline.PromptPolicyVersion);
        cache.TryGet(key, TranslationCacheContext.Empty, out var cached).Should().BeTrue();
        cached.Should().Be("第一段。\n\n第二段。");
        dispatcher.Drain(10).Should().ContainSingle(result => result.TranslatedText == "第一段。\n\n第二段。");
    }

    [Fact]
    public async Task WorkerPool_translates_per_character_tmp_text_as_plain_text_and_rebuilds_rich_text()
    {
        var queue = new TranslationJobQueue();
        var dispatcher = new ResultDispatcher();
        var cache = new MemoryTranslationCache();
        var provider = new CapturingProvider(new[] { "\u4e2d\u6587" });
        var config = RuntimeConfig.CreateDefault() with { MaxConcurrentRequests = 1 };
        var pool = new TranslationWorkerPool(queue, dispatcher, provider, new ProviderRateLimiter(600), config, cache);
        var source = "<rotate=90>A</rotate><rotate=90>B</rotate><rotate=90>C</rotate>";

        queue.Enqueue(TranslationJob.Create("ui-1", source, TranslationPriority.VisibleUi));

        await pool.RunUntilIdleAsync(CancellationToken.None);

        provider.LastRequest.Should().NotBeNull();
        provider.LastRequest!.ProtectedTexts.Should().Equal("ABC");
        provider.LastRequest.UserPrompt.Should().Contain("[\"ABC\"]");
        provider.LastRequest.UserPrompt.Should().NotContain("<rotate");

        var key = TranslationCacheKey.Create(source, config.TargetLanguage, config.Provider, TextPipeline.PromptPolicyVersion);
        cache.TryGet(key, TranslationCacheContext.Empty, out var cached).Should().BeTrue();
        cached.Should().Be("<rotate=90>\u4e2d</rotate><rotate=90>\u6587</rotate>");
        cached.Should().NotContain("<rotate=90></rotate>");
        dispatcher.Drain(10).Should().ContainSingle(result => result.TranslatedText == cached);
    }

    [Fact]
    public async Task WorkerPool_reports_provider_failures_that_do_not_produce_cache_entries()
    {
        var queue = new TranslationJobQueue();
        var dispatcher = new ResultDispatcher();
        var cache = new MemoryTranslationCache();
        var failures = new List<string>();
        var config = RuntimeConfig.CreateDefault() with { MaxConcurrentRequests = 1 };
        var pool = new TranslationWorkerPool(
            queue,
            dispatcher,
            new FailureProvider("request timed out"),
            new ProviderRateLimiter(600),
            config,
            cache,
            failureReporter: failures.Add);

        queue.Enqueue(TranslationJob.Create("ui-1", "Disclaimer text", TranslationPriority.VisibleUi));

        await pool.RunUntilIdleAsync(CancellationToken.None);

        var failure = failures.Should().ContainSingle().Which;
        failure.Should().Contain("翻译服务请求失败");
        failure.Should().Contain("request timed out");
        failure.Should().Contain("源文本预览：Disclaimer text");
        var key = TranslationCacheKey.Create("Disclaimer text", config.TargetLanguage, config.Provider, TextPipeline.PromptPolicyVersion);
        cache.TryGet(key, TranslationCacheContext.Empty, out _).Should().BeFalse();
    }

    private sealed class DelayedProvider : ITranslationProvider
    {
        private int _current;
        private readonly TimeSpan _delay;

        public DelayedProvider(TimeSpan delay) => _delay = delay;

        public int MaxObservedConcurrency { get; private set; }

        public ProviderKind Kind => ProviderKind.OpenAI;

        public async Task<TranslationResponse> TranslateAsync(TranslationRequest request, CancellationToken cancellationToken)
        {
            var current = Interlocked.Increment(ref _current);
            MaxObservedConcurrency = Math.Max(MaxObservedConcurrency, current);
            await Task.Delay(_delay, cancellationToken);
            Interlocked.Decrement(ref _current);
            return TranslationResponse.Success(request.ProtectedTexts.Select(x => "T:" + x).ToArray());
        }
    }

    private sealed class BlockingProvider : ITranslationProvider
    {
        private readonly object _gate = new();
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly ProviderKind _kind;
        private int _current;
        private int _startedCount;

        public BlockingProvider(ProviderKind kind = ProviderKind.OpenAI)
        {
            _kind = kind;
        }

        public int StartedCount => Volatile.Read(ref _startedCount);

        public int MaxObservedConcurrency { get; private set; }

        public ProviderKind Kind => _kind;

        public async Task<TranslationResponse> TranslateAsync(TranslationRequest request, CancellationToken cancellationToken)
        {
            var current = Interlocked.Increment(ref _current);
            Interlocked.Increment(ref _startedCount);
            lock (_gate)
            {
                MaxObservedConcurrency = Math.Max(MaxObservedConcurrency, current);
            }

            try
            {
                await _release.Task.WaitAsync(cancellationToken);
                return TranslationResponse.Success(request.ProtectedTexts.Select(x => "T:" + x).ToArray());
            }
            finally
            {
                Interlocked.Decrement(ref _current);
            }
        }

        public void Release()
        {
            _release.TrySetResult();
        }
    }

    private sealed class CapturingProvider : ITranslationProvider
    {
        private readonly IReadOnlyList<string> _translations;

        public CapturingProvider(IReadOnlyList<string> translations) => _translations = translations;

        public TranslationRequest? LastRequest { get; private set; }

        public List<TranslationRequest> Requests { get; } = new();

        public ProviderKind Kind => ProviderKind.OpenAI;

        public Task<TranslationResponse> TranslateAsync(TranslationRequest request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            Requests.Add(request);
            return Task.FromResult(TranslationResponse.Success(_translations));
        }
    }

    private sealed class FailureProvider : ITranslationProvider
    {
        private readonly string _message;

        public FailureProvider(string message) => _message = message;

        public ProviderKind Kind => ProviderKind.OpenAI;

        public Task<TranslationResponse> TranslateAsync(TranslationRequest request, CancellationToken cancellationToken)
        {
            return Task.FromResult(TranslationResponse.Failure(_message));
        }
    }

    private sealed class ThrowingProvider : ITranslationProvider
    {
        private readonly string _message;

        public ThrowingProvider(string message) => _message = message;

        public ProviderKind Kind => ProviderKind.OpenAI;

        public Task<TranslationResponse> TranslateAsync(TranslationRequest request, CancellationToken cancellationToken)
        {
            throw new InvalidOperationException(_message);
        }
    }

    private sealed class SequencedProvider : ITranslationProvider
    {
        private readonly Queue<IReadOnlyList<string>> _responses;

        public SequencedProvider(IEnumerable<IReadOnlyList<string>> responses)
        {
            _responses = new Queue<IReadOnlyList<string>>(responses);
        }

        public List<TranslationRequest> Requests { get; } = new();

        public ProviderKind Kind => ProviderKind.OpenAI;

        public Task<TranslationResponse> TranslateAsync(TranslationRequest request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(TranslationResponse.Success(_responses.Count == 0 ? Array.Empty<string>() : _responses.Dequeue()));
        }
    }

    private sealed class RecordingCache : ITranslationCache
    {
        public TranslationCacheKey? LastKey { get; private set; }

        public TranslationCacheContext? LastContext { get; private set; }

        public string? LastTranslatedText { get; private set; }

        public int Count => LastKey == null ? 0 : 1;

        public bool TryGet(TranslationCacheKey key, TranslationCacheContext? context, out string translatedText)
        {
            translatedText = string.Empty;
            return false;
        }

        public bool TryGetReplacementFont(TranslationCacheKey key, TranslationCacheContext context, out string replacementFont)
        {
            replacementFont = string.Empty;
            return false;
        }

        public IReadOnlyList<TranslationCacheEntry> GetCompletedTranslationsBySource(
            TranslationCacheKey key,
            int limit)
        {
            return Array.Empty<TranslationCacheEntry>();
        }

        public void Set(TranslationCacheKey key, string translatedText, TranslationCacheContext? context = null)
        {
            LastKey = key;
            LastTranslatedText = translatedText;
            LastContext = context;
        }

        public TranslationCachePage Query(TranslationCacheQuery query)
        {
            return new TranslationCachePage(0, Array.Empty<TranslationCacheEntry>());
        }

        public TranslationCacheFilterOptionPage GetFilterOptions(TranslationCacheFilterOptionsQuery query)
        {
            return new TranslationCacheFilterOptionPage(query.Column, Array.Empty<TranslationCacheFilterOption>());
        }

        public void Update(TranslationCacheEntry entry)
        {
        }

        public void Delete(TranslationCacheEntry entry)
        {
        }

        public string Export(string format)
        {
            return string.Empty;
        }

        public TranslationCacheImportResult Import(string content, string format)
        {
            return new TranslationCacheImportResult(0, Array.Empty<string>());
        }

        public void RecordCaptured(TranslationCacheKey key, TranslationCacheContext? context = null)
        {
        }

        public IReadOnlyList<TranslationCacheEntry> GetPendingTranslations(
            string targetLanguage,
            string promptPolicyVersion,
            int limit)
        {
            return Array.Empty<TranslationCacheEntry>();
        }

        public IReadOnlyList<TranslationContextExample> GetTranslationContextExamples(
            string currentSourceText,
            string targetLanguage,
            TranslationCacheContext? context,
            int maxExamples,
            int maxCharacters)
        {
            return Array.Empty<TranslationContextExample>();
        }
    }

    private static TranslationCacheEntry SampleRow(
        string sourceText,
        string sceneName,
        string componentHierarchy,
        string? componentType,
        string? translatedText,
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

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (!condition() && DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(10);
        }
    }

    private static async Task RunPoolUntilNoDeferredRetriesAsync(
        TranslationWorkerPool pool,
        TranslationJobQueue queue,
        int maxCycles = 8)
    {
        for (var i = 0; i < maxCycles; i++)
        {
            await pool.RunUntilIdleAsync(CancellationToken.None);
            if (queue.PromoteDeferred() == 0)
            {
                return;
            }
        }

        throw new InvalidOperationException("Deferred quality retries did not drain.");
    }
}

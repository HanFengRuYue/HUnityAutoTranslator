using FluentAssertions;
using HUnityAutoTranslator.Core.Caching;
using HUnityAutoTranslator.Core.Configuration;
using HUnityAutoTranslator.Core.Control;
using HUnityAutoTranslator.Core.Dispatching;
using HUnityAutoTranslator.Core.Glossary;
using HUnityAutoTranslator.Core.Pipeline;
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
        private int _current;
        private int _startedCount;

        public int StartedCount => Volatile.Read(ref _startedCount);

        public int MaxObservedConcurrency { get; private set; }

        public ProviderKind Kind => ProviderKind.OpenAI;

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

        public ProviderKind Kind => ProviderKind.OpenAI;

        public Task<TranslationResponse> TranslateAsync(TranslationRequest request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(TranslationResponse.Success(_translations));
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
            ProviderProfile provider,
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
}

using FluentAssertions;
using HUnityAutoTranslator.Core.Caching;
using HUnityAutoTranslator.Core.Configuration;
using HUnityAutoTranslator.Core.Control;
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

    [Fact]
    public void Process_queues_capture_context_for_cache_write()
    {
        var queue = new TranslationJobQueue();
        var pipeline = new TextPipeline(new MemoryTranslationCache(), queue, RuntimeConfig.CreateDefault());
        var context = new TranslationCacheContext("MainMenu", "Canvas/Menu/Options", "UnityEngine.UI.Text");

        pipeline.Process(new CapturedText("ui-2", "Options", isVisible: true, context));

        queue.TryDequeueBatch(1, 100, out var batch).Should().BeTrue();
        batch[0].Context.Should().Be(context);
    }

    [Fact]
    public void Process_records_valid_capture_in_cache_before_queueing()
    {
        var cache = new MemoryTranslationCache();
        var queue = new TranslationJobQueue();
        var config = RuntimeConfig.CreateDefault();
        var pipeline = new TextPipeline(cache, queue, config);
        var context = new TranslationCacheContext("MainMenu", "Canvas/Menu/Options", "UnityEngine.UI.Text");

        pipeline.Process(new CapturedText("ui-2", "Options", isVisible: true, context));

        var pending = cache.GetPendingTranslations(config.TargetLanguage, config.Provider, TextPipeline.PromptPolicyVersion, limit: 10);
        pending.Should().ContainSingle();
        pending[0].SourceText.Should().Be("Options");
        pending[0].TranslatedText.Should().BeNull();
        queue.PendingCount.Should().Be(1);
    }

    [Fact]
    public void Process_queues_original_source_text_for_writeback_matching()
    {
        var queue = new TranslationJobQueue();
        var pipeline = new TextPipeline(new MemoryTranslationCache(), queue, RuntimeConfig.CreateDefault());
        var source = "  Start   Game  ";

        pipeline.Process(new CapturedText("ui-2", source, isVisible: true));

        queue.TryDequeueBatch(1, 100, out var batch).Should().BeTrue();
        batch[0].SourceText.Should().Be(source);
    }

    [Fact]
    public void Process_reads_latest_runtime_config_from_provider()
    {
        var queue = new TranslationJobQueue();
        var config = RuntimeConfig.CreateDefault();
        var pipeline = new TextPipeline(new MemoryTranslationCache(), queue, () => config);

        config = config with { Enabled = false };
        var decision = pipeline.Process(new CapturedText("ui-3", "Options", isVisible: true));

        decision.Kind.Should().Be(PipelineDecisionKind.Ignored);
        queue.PendingCount.Should().Be(0);
    }

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

    [Fact]
    public void Process_records_metrics_once_for_repeated_pending_source_text()
    {
        var cache = new MemoryTranslationCache();
        var queue = new TranslationJobQueue();
        var metrics = new ControlPanelMetrics();
        var pipeline = new TextPipeline(cache, queue, RuntimeConfig.CreateDefault(), metrics);

        pipeline.Process(new CapturedText("target-1", "Start Game", true, TranslationCacheContext.Empty));
        pipeline.Process(new CapturedText("target-2", "Start Game", true, TranslationCacheContext.Empty));

        var snapshot = metrics.Snapshot();
        snapshot.CapturedTextCount.Should().Be(1);
        snapshot.QueuedTextCount.Should().Be(1);
        queue.PendingCount.Should().Be(1);
    }

    [Fact]
    public void Process_does_not_count_cache_hit_as_completed_translation()
    {
        var cache = new MemoryTranslationCache();
        var queue = new TranslationJobQueue();
        var metrics = new ControlPanelMetrics();
        var config = RuntimeConfig.CreateDefault();
        var key = TranslationCacheKey.Create("Start Game", config.TargetLanguage, config.Provider, TextPipeline.PromptPolicyVersion);
        cache.Set(key, "Start translated");
        var pipeline = new TextPipeline(cache, queue, config, metrics);

        pipeline.Process(new CapturedText("target", "Start Game", true, TranslationCacheContext.Empty));

        var snapshot = metrics.Snapshot();
        snapshot.CapturedTextCount.Should().Be(1);
        snapshot.CompletedTranslationCount.Should().Be(0);
        snapshot.RecentTranslations.Should().BeEmpty();
    }

    [Fact]
    public void Process_does_not_record_capture_metrics_for_ignored_text()
    {
        var cache = new MemoryTranslationCache();
        var queue = new TranslationJobQueue();
        var metrics = new ControlPanelMetrics();
        var pipeline = new TextPipeline(cache, queue, RuntimeConfig.CreateDefault(), metrics);

        pipeline.Process(new CapturedText("target", "12345", true, TranslationCacheContext.Empty));

        metrics.Snapshot().CapturedTextCount.Should().Be(0);
    }
}

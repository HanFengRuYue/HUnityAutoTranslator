using FluentAssertions;
using HUnityAutoTranslator.Core.Caching;
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
    public void Queue_deduplicates_same_source_text_in_same_context()
    {
        var queue = new TranslationJobQueue();
        var context = new TranslationCacheContext("Menu", "Canvas/Start", "Text");
        queue.Enqueue(TranslationJob.Create("a", "Start", TranslationPriority.Normal, context));
        queue.Enqueue(TranslationJob.Create("b", "Start", TranslationPriority.VisibleUi, context));

        queue.PendingCount.Should().Be(1);
    }

    [Fact]
    public void Queue_keeps_same_source_text_separate_for_different_contexts()
    {
        var queue = new TranslationJobQueue();

        var menuQueued = queue.Enqueue(TranslationJob.Create(
            "menu",
            "Back",
            TranslationPriority.Normal,
            new TranslationCacheContext("MainMenu", "Canvas/Menu/Back", "Text")));
        var hudQueued = queue.Enqueue(TranslationJob.Create(
            "hud",
            "Back",
            TranslationPriority.Normal,
            new TranslationCacheContext("Gameplay", "Canvas/Hud/Back", "Text")));

        menuQueued.Should().BeTrue();
        hudQueued.Should().BeTrue();
        queue.PendingCount.Should().Be(2);
    }

    [Fact]
    public void TryDequeueBatch_updates_pending_and_inflight_counts()
    {
        var queue = new TranslationJobQueue();
        queue.Enqueue(TranslationJob.Create("target", "Start Game", TranslationPriority.Normal));

        queue.TryDequeueBatch(1, 2000, out var batch).Should().BeTrue();

        queue.PendingCount.Should().Be(0);
        queue.InFlightCount.Should().Be(1);

        queue.MarkCompleted(batch);

        queue.InFlightCount.Should().Be(0);
    }

    [Fact]
    public void Deferred_jobs_are_not_dequeued_until_promoted()
    {
        var queue = new TranslationJobQueue();
        var retry = TranslationJob.Create(
            "retry",
            "Ultra",
            TranslationPriority.VisibleUi,
            qualityRetryCount: 1);

        queue.EnqueueDeferred(retry).Should().BeTrue();

        queue.PendingCount.Should().Be(0);
        queue.TryDequeueBatch(1, 2000, out _).Should().BeFalse();

        queue.PromoteDeferred().Should().Be(1);
        queue.PendingCount.Should().Be(1);
        queue.TryDequeueBatch(1, 2000, out var batch).Should().BeTrue();
        batch.Should().ContainSingle();
        batch[0].QualityRetryCount.Should().Be(1);
    }

    [Fact]
    public async Task Wait_for_pending_returns_when_new_job_is_enqueued()
    {
        var queue = new TranslationJobQueue();
        var waitTask = queue.WaitForPendingAsync(TimeSpan.FromSeconds(1), CancellationToken.None);

        queue.Enqueue(TranslationJob.Create("button", "Start Game", TranslationPriority.VisibleUi)).Should().BeTrue();

        (await waitTask).Should().BeTrue();
    }

    [Fact]
    public async Task Wait_for_pending_times_out_when_queue_stays_empty()
    {
        var queue = new TranslationJobQueue();

        var signaled = await queue.WaitForPendingAsync(TimeSpan.FromMilliseconds(1), CancellationToken.None);

        signaled.Should().BeFalse();
    }

    [Fact]
    public void Quality_retry_resume_suppression_skips_only_stale_pending_rows()
    {
        var suppressions = new QualityRetryResumeSuppressions();
        var context = new TranslationCacheContext("Menu", "Canvas/Settings/SFX", "TMPro.TextMeshProUGUI");
        var retryLimitUtc = DateTimeOffset.Parse("2026-04-29T12:00:00Z");

        suppressions.Suppress("SFX Volume", context, retryLimitUtc);

        suppressions.ShouldSkip(PendingRow("SFX Volume", context, retryLimitUtc.AddSeconds(-1))).Should().BeTrue();
        suppressions.ShouldSkip(PendingRow("SFX Volume", context, retryLimitUtc.AddSeconds(1))).Should().BeFalse();
        suppressions.ShouldSkip(PendingRow("SFX Volume", context, retryLimitUtc.AddSeconds(-1))).Should().BeFalse();
    }

    [Fact]
    public void Prefetch_jobs_drain_after_normal_and_visible_jobs()
    {
        var queue = new TranslationJobQueue();
        queue.Enqueue(TranslationJob.Create("prefetch", "Hidden panel", TranslationPriority.Prefetch));
        queue.Enqueue(TranslationJob.Create("normal", "Some lore", TranslationPriority.Normal));
        queue.Enqueue(TranslationJob.Create("visible", "Start", TranslationPriority.VisibleUi));

        queue.TryDequeueBatch(10, 2000, out var batch).Should().BeTrue();

        batch.Select(job => job.Id).Should().Equal("visible", "normal", "prefetch");
    }

    [Fact]
    public void Visible_enqueue_upgrades_a_pending_prefetch_job()
    {
        var queue = new TranslationJobQueue();
        var context = new TranslationCacheContext("Menu", "Canvas/Options/Title", "UnityEngine.UI.Text");
        queue.Enqueue(TranslationJob.Create("prefetch", "Options", TranslationPriority.Prefetch, context));
        queue.Enqueue(TranslationJob.Create("visible", "Options", TranslationPriority.VisibleUi, context));

        queue.PendingCount.Should().Be(1);
        queue.TryDequeueBatch(10, 2000, out var batch).Should().BeTrue();
        batch[0].Priority.Should().Be(TranslationPriority.VisibleUi);
    }

    private static TranslationCacheEntry PendingRow(
        string sourceText,
        TranslationCacheContext context,
        DateTimeOffset updatedUtc)
    {
        return new TranslationCacheEntry(
            SourceText: sourceText,
            TargetLanguage: "zh-Hans",
            ProviderKind: string.Empty,
            ProviderBaseUrl: string.Empty,
            ProviderEndpoint: string.Empty,
            ProviderModel: string.Empty,
            PromptPolicyVersion: "prompt-v4",
            TranslatedText: null,
            SceneName: context.SceneName,
            ComponentHierarchy: context.ComponentHierarchy,
            ComponentType: context.ComponentType,
            ReplacementFont: null,
            CreatedUtc: updatedUtc.AddMinutes(-1),
            UpdatedUtc: updatedUtc);
    }
}

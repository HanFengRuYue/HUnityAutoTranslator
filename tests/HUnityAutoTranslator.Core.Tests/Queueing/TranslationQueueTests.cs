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
}

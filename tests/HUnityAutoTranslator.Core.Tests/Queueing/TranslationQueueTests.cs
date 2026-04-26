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
}

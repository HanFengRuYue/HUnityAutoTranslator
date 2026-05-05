using FluentAssertions;
using HUnityAutoTranslator.Core.Dispatching;

namespace HUnityAutoTranslator.Core.Tests.Dispatching;

public sealed class ResultDispatcherTests
{
    [Fact]
    public void Drain_returns_high_priority_results_first_with_budget()
    {
        var dispatcher = new ResultDispatcher();
        dispatcher.Publish(new TranslationResult("normal", "原文", "译文", priority: 0));
        dispatcher.Publish(new TranslationResult("visible", "Start", "开始", priority: 100));

        var drained = dispatcher.Drain(maxCount: 1);

        drained.Should().ContainSingle();
        drained[0].TargetId.Should().Be("visible");
        dispatcher.PendingCount.Should().Be(1);
    }

    [Fact]
    public void Publish_coalesces_pending_results_for_the_same_target()
    {
        var dispatcher = new ResultDispatcher();

        dispatcher.Publish(new TranslationResult("button", "Start", "开始", priority: 0));
        dispatcher.Publish(new TranslationResult("button", "Start", "开始游戏", priority: 0));

        dispatcher.PendingCount.Should().Be(1);
        dispatcher.Drain(maxCount: 10).Should().ContainSingle().Which.TranslatedText.Should().Be("开始游戏");
    }

    [Fact]
    public void Publish_bounds_pending_results_when_writeback_is_blocked()
    {
        var dispatcher = new ResultDispatcher(maxPendingResults: 3);

        dispatcher.Publish(new TranslationResult("first", "A", "一", priority: 0));
        dispatcher.Publish(new TranslationResult("second", "B", "二", priority: 0));
        dispatcher.Publish(new TranslationResult("third", "C", "三", priority: 0));
        dispatcher.Publish(new TranslationResult("fourth", "D", "四", priority: 0));

        dispatcher.PendingCount.Should().Be(3);
        dispatcher.Drain(maxCount: 10).Select(item => item.TargetId)
            .Should().Equal("second", "third", "fourth");
    }
}

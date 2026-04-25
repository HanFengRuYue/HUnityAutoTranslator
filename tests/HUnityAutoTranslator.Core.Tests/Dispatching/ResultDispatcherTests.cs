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
}

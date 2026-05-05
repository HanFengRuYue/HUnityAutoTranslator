using FluentAssertions;
using HUnityAutoTranslator.Core.Caching;
using HUnityAutoTranslator.Core.Configuration;
using HUnityAutoTranslator.Core.Control;

namespace HUnityAutoTranslator.Core.Tests.Control;

public sealed class ControlPanelMetricsTests
{
    [Fact]
    public void Captured_key_tracker_is_bounded_for_long_sessions()
    {
        var metrics = new ControlPanelMetrics();
        var config = RuntimeConfig.CreateDefault();

        for (var i = 0; i < ControlPanelMetrics.MaxTrackedCapturedKeys + 250; i++)
        {
            metrics.RecordCaptured(TranslationCacheKey.Create(
                $"source-{i}",
                config.TargetLanguage,
                config.Provider,
                "policy"));
        }

        var snapshot = metrics.Snapshot();

        snapshot.CapturedTextCount.Should().Be(ControlPanelMetrics.MaxTrackedCapturedKeys + 250);
        snapshot.CapturedKeyTrackerCount.Should().BeLessThanOrEqualTo(ControlPanelMetrics.MaxTrackedCapturedKeys);
    }

    [Fact]
    public void Runtime_performance_counters_are_recorded_for_hot_paths()
    {
        var metrics = new ControlPanelMetrics();

        metrics.RecordTextChangeHookEvent();
        metrics.RecordTextChangeHookQueued();
        metrics.RecordTextChangeHookMerged();
        metrics.RecordTextChangeHookDropped();
        metrics.RecordTextChangeRawPrefiltered();
        metrics.RecordTextChangeQueueDrain(TimeSpan.FromMilliseconds(2), processedItems: 4);
        metrics.RecordTextTargetMetadataBuild();
        metrics.RecordCacheLookup();
        metrics.RecordGlobalTextScanRequest();
        metrics.RecordGlobalTextScan(TimeSpan.FromMilliseconds(3), processedTargets: 42);
        metrics.RecordRememberedReapply(checkedCount: 10, appliedCount: 3);
        metrics.RecordFontApplication();
        metrics.RecordFontApplicationSkipped();
        metrics.RecordLayoutApplication();
        metrics.RecordLayoutApplicationSkipped();
        metrics.RecordTmpMeshForceUpdate();

        var snapshot = metrics.Snapshot();

        snapshot.TextChangeHookEventCount.Should().Be(1);
        snapshot.TextChangeHookQueuedCount.Should().Be(1);
        snapshot.TextChangeHookMergedCount.Should().Be(1);
        snapshot.TextChangeHookDroppedCount.Should().Be(1);
        snapshot.TextChangeRawPrefilteredCount.Should().Be(1);
        snapshot.TextChangeQueueProcessedCount.Should().Be(4);
        snapshot.TextChangeQueueMilliseconds.Should().Be(2);
        snapshot.TextTargetMetadataBuildCount.Should().Be(1);
        snapshot.CacheLookupCount.Should().Be(1);
        snapshot.GlobalTextScanRequestCount.Should().Be(1);
        snapshot.GlobalTextScanCount.Should().Be(1);
        snapshot.GlobalTextScanTargetCount.Should().Be(42);
        snapshot.GlobalTextScanMilliseconds.Should().Be(3);
        snapshot.RememberedReapplyCheckCount.Should().Be(10);
        snapshot.RememberedReapplyAppliedCount.Should().Be(3);
        snapshot.FontApplicationCount.Should().Be(1);
        snapshot.FontApplicationSkippedCount.Should().Be(1);
        snapshot.LayoutApplicationCount.Should().Be(1);
        snapshot.LayoutApplicationSkippedCount.Should().Be(1);
        snapshot.TmpMeshForceUpdateCount.Should().Be(1);
    }
}

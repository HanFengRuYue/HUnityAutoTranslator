using System.Diagnostics;
using System.Reflection;
using HUnityAutoTranslator.Core.Control;
using UnityEngine;

namespace HUnityAutoTranslator.Plugin.Capture;

internal sealed class UnityTextChangeQueue
{
    private const int MaxQueuedComponents = 4096;

    private readonly Queue<int> _componentOrder = new();
    private readonly Dictionary<int, UnityTextChangeWorkItem> _queuedByComponentId = new();
    private readonly ControlPanelMetrics? _metrics;

    public UnityTextChangeQueue(ControlPanelMetrics? metrics = null)
    {
        _metrics = metrics;
    }

    public float LastHookEventTime { get; private set; } = float.NegativeInfinity;

    public int Count => _queuedByComponentId.Count;

    public bool HasRecentHookEvent(float now, float maxAgeSeconds)
    {
        return LastHookEventTime > 0 && now - LastHookEventTime <= maxAgeSeconds;
    }

    public bool Enqueue(
        UnityEngine.Object component,
        PropertyInfo textProperty,
        UnityTextTargetKind targetKind,
        string? observedText)
    {
        LastHookEventTime = Time.unscaledTime;
        if (component == null)
        {
            return false;
        }

        var componentId = component.GetInstanceID();
        var item = new UnityTextChangeWorkItem(component, textProperty, targetKind, observedText, Time.unscaledTime);
        if (_queuedByComponentId.ContainsKey(componentId))
        {
            _queuedByComponentId[componentId] = item;
            _metrics?.RecordTextChangeHookMerged();
            return true;
        }

        if (_queuedByComponentId.Count >= MaxQueuedComponents)
        {
            _metrics?.RecordTextChangeHookDropped();
            return false;
        }

        _queuedByComponentId.Add(componentId, item);
        _componentOrder.Enqueue(componentId);
        _metrics?.RecordTextChangeHookQueued();
        return true;
    }

    public void RequeueForStability(UnityTextChangeWorkItem item, float readyTime)
    {
        if (item.Component == null)
        {
            return;
        }

        var componentId = item.Component.GetInstanceID();
        if (_queuedByComponentId.ContainsKey(componentId))
        {
            return;
        }

        _queuedByComponentId[componentId] = item with { ReadyTime = readyTime };
        _componentOrder.Enqueue(componentId);
    }

    public int Drain(Action<UnityTextChangeWorkItem> process, int maxItems, int maxMilliseconds)
    {
        if (process == null || maxItems <= 0 || _queuedByComponentId.Count == 0)
        {
            return 0;
        }

        var stopwatch = Stopwatch.StartNew();
        var processed = 0;
        var budgetMilliseconds = Math.Max(1, maxMilliseconds);
        var now = Time.unscaledTime;
        var attempts = _componentOrder.Count;
        while (processed < maxItems &&
            _componentOrder.Count > 0 &&
            attempts-- > 0 &&
            (processed == 0 || stopwatch.ElapsedMilliseconds < budgetMilliseconds))
        {
            var componentId = _componentOrder.Dequeue();
            if (!_queuedByComponentId.Remove(componentId, out var item) ||
                item.Component == null)
            {
                continue;
            }

            if (item.ReadyTime > now)
            {
                _queuedByComponentId[componentId] = item;
                _componentOrder.Enqueue(componentId);
                continue;
            }

            process(item);
            processed++;
        }

        stopwatch.Stop();
        _metrics?.RecordTextChangeQueueDrain(stopwatch.Elapsed, processed);
        return processed;
    }

    public void Clear()
    {
        _componentOrder.Clear();
        _queuedByComponentId.Clear();
    }
}

internal sealed record UnityTextChangeWorkItem(
    UnityEngine.Object Component,
    PropertyInfo TextProperty,
    UnityTextTargetKind TargetKind,
    string? ObservedText,
    float ReadyTime);

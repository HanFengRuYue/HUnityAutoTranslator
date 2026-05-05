using System.Reflection;
using HUnityAutoTranslator.Core.Control;
using UnityEngine;

namespace HUnityAutoTranslator.Plugin.Capture;

internal sealed class UnityTextTargetRegistry
{
    private const int MaxTargets = 4096;

    private readonly Dictionary<int, ReflectionTextTarget> _targets = new();
    private readonly Queue<int> _targetOrder = new();
    private readonly ControlPanelMetrics? _metrics;
    private int _metadataGeneration;

    public UnityTextTargetRegistry(ControlPanelMetrics? metrics = null)
    {
        _metrics = metrics;
    }

    public ReflectionTextTarget GetOrCreateTarget(UnityEngine.Object component, PropertyInfo textProperty)
    {
        var componentId = component.GetInstanceID();
        if (_targets.TryGetValue(componentId, out var existing) && existing.IsAlive)
        {
            existing.UpdateTextProperty(textProperty);
            existing.RefreshGeneration(_metadataGeneration);
            return existing;
        }

        var target = new ReflectionTextTarget(component, textProperty, _metadataGeneration);
        _targets[componentId] = target;
        _targetOrder.Enqueue(componentId);
        _metrics?.RecordTextTargetMetadataBuild();
        TrimDeadOrExcessTargets();
        return target;
    }

    public void InvalidateMetadata()
    {
        _metadataGeneration++;
    }

    private void TrimDeadOrExcessTargets()
    {
        while (_targets.Count > MaxTargets && _targetOrder.Count > 0)
        {
            var componentId = _targetOrder.Dequeue();
            if (!_targets.TryGetValue(componentId, out var target) || !target.IsAlive || _targets.Count > MaxTargets)
            {
                _targets.Remove(componentId);
            }
        }
    }
}

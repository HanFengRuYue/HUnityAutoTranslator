using System.Collections.Concurrent;
using BepInEx.Logging;
using HUnityAutoTranslator.Core.Control;
using UnityEngine;

namespace HUnityAutoTranslator.Plugin.Unity;

internal sealed class UnityTextHighlighter
{
    private const float HighlightDurationSeconds = 1.6f;
    private const float BorderThickness = 3f;
    private static readonly Color BorderColor = new(1f, 0.72f, 0.05f, 0.95f);

    private readonly UnityMainThreadResultApplier _targets;
    private readonly ManualLogSource _logger;
    private readonly ConcurrentQueue<string> _pendingTargetIds = new();
    private readonly List<ActiveHighlight> _active = new();
    private readonly object _snapshotGate = new();
    private IReadOnlyList<TranslationHighlightTarget> _targetSnapshot = Array.Empty<TranslationHighlightTarget>();
    private float _nextMissingLogTime;

    public UnityTextHighlighter(UnityMainThreadResultApplier targets, ManualLogSource logger)
    {
        _targets = targets;
        _logger = logger;
    }

    public void RefreshTargetSnapshot(IReadOnlyList<TranslationHighlightTarget> targets)
    {
        lock (_snapshotGate)
        {
            _targetSnapshot = targets.ToArray();
        }
    }

    public TranslationHighlightResult RequestHighlight(TranslationHighlightRequest request)
    {
        if (!TranslationHighlightMatcher.IsSupported(request))
        {
            return TranslationHighlightResult.UnsupportedTarget();
        }

        if (!TryResolveTargetId(request, out var targetId))
        {
            return TranslationHighlightResult.TargetNotFound();
        }

        _pendingTargetIds.Enqueue(targetId);
        return TranslationHighlightResult.Queued(targetId);
    }

    public bool TryResolveTargetId(TranslationHighlightRequest request, out string targetId)
    {
        targetId = string.Empty;
        if (!TranslationHighlightMatcher.IsSupported(request))
        {
            return false;
        }

        TranslationHighlightTarget? match;
        lock (_snapshotGate)
        {
            match = TranslationHighlightMatcher.FindBestMatch(request, _targetSnapshot);
        }

        if (match == null)
        {
            return false;
        }

        targetId = match.TargetId;
        return true;
    }

    public void Tick()
    {
        while (_pendingTargetIds.TryDequeue(out var targetId))
        {
            if (_targets.TryGetTarget(targetId, out var target))
            {
                _active.RemoveAll(highlight => highlight.Target.Id == target.Id);
                _active.Add(new ActiveHighlight(target, Time.unscaledTime + HighlightDurationSeconds));
            }
            else
            {
                LogMissingTarget(targetId);
            }
        }

        _active.RemoveAll(highlight => Time.unscaledTime >= highlight.ExpiresAt || !highlight.Target.IsAlive);
    }

    public void OnGUI()
    {
        foreach (var highlight in _active.ToArray())
        {
            if (!highlight.Target.TryGetScreenRect(out var rect))
            {
                continue;
            }

            DrawBorder(ClampRect(rect));
        }
    }

    private static void DrawBorder(Rect rect)
    {
        if (rect.width <= BorderThickness * 2f || rect.height <= BorderThickness * 2f)
        {
            return;
        }

        var previousColor = GUI.color;
        GUI.color = BorderColor;
        GUI.DrawTexture(new Rect(rect.xMin, rect.yMin, rect.width, BorderThickness), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(rect.xMin, rect.yMax - BorderThickness, rect.width, BorderThickness), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(rect.xMin, rect.yMin, BorderThickness, rect.height), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(rect.xMax - BorderThickness, rect.yMin, BorderThickness, rect.height), Texture2D.whiteTexture);
        GUI.color = previousColor;
    }

    private static Rect ClampRect(Rect rect)
    {
        var minX = Mathf.Clamp(rect.xMin, 0f, Screen.width);
        var minY = Mathf.Clamp(rect.yMin, 0f, Screen.height);
        var maxX = Mathf.Clamp(rect.xMax, 0f, Screen.width);
        var maxY = Mathf.Clamp(rect.yMax, 0f, Screen.height);
        return Rect.MinMaxRect(minX, minY, maxX, maxY);
    }

    private void LogMissingTarget(string targetId)
    {
        if (Time.unscaledTime < _nextMissingLogTime)
        {
            return;
        }

        _logger.LogWarning($"Highlight target disappeared before it could be drawn: {targetId}");
        _nextMissingLogTime = Time.unscaledTime + 5f;
    }

    private sealed class ActiveHighlight
    {
        public ActiveHighlight(IUnityTextTarget target, float expiresAt)
        {
            Target = target;
            ExpiresAt = expiresAt;
        }

        public IUnityTextTarget Target { get; }

        public float ExpiresAt { get; }
    }
}

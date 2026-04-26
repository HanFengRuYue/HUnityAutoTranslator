using HUnityAutoTranslator.Core.Dispatching;
using HUnityAutoTranslator.Core.Control;

namespace HUnityAutoTranslator.Plugin.Unity;

internal sealed class UnityMainThreadResultApplier
{
    private readonly Dictionary<string, IUnityTextTarget> _targets = new();
    private readonly TranslationWritebackTracker _writebacks = new();

    public void Register(IUnityTextTarget target)
    {
        _targets[target.Id] = target;
    }

    public IReadOnlyList<TranslationHighlightTarget> SnapshotTargets()
    {
        var targets = new List<TranslationHighlightTarget>();
        foreach (var target in _targets.Values.ToArray())
        {
            if (!target.IsAlive)
            {
                _targets.Remove(target.Id);
                _writebacks.Forget(target.Id);
                continue;
            }

            targets.Add(new TranslationHighlightTarget(
                target.Id,
                target.SceneName,
                target.HierarchyPath,
                target.ComponentType,
                IsAlive: true,
                target.IsVisible));
        }

        return targets;
    }

    public bool TryGetTarget(string targetId, out IUnityTextTarget target)
    {
        if (_targets.TryGetValue(targetId, out target!) && target.IsAlive)
        {
            return true;
        }

        if (target != null)
        {
            _targets.Remove(targetId);
            _writebacks.Forget(targetId);
        }

        target = null!;
        return false;
    }

    public bool RememberAndApply(IUnityTextTarget target, string sourceText, string translatedText)
    {
        Register(target);
        _writebacks.Remember(target.Id, sourceText, translatedText);
        return TryApplyRemembered(target);
    }

    public bool IsRememberedTranslation(string targetId, string? currentText)
    {
        return _writebacks.IsRememberedTranslation(targetId, currentText);
    }

    public int Apply(IReadOnlyList<TranslationResult> results)
    {
        var applied = 0;
        foreach (var result in results)
        {
            if (!_targets.TryGetValue(result.TargetId, out var target) || !target.IsAlive)
            {
                _writebacks.Forget(result.TargetId);
                continue;
            }

            if (_writebacks.IsRememberedTranslation(result.TargetId, result.SourceText))
            {
                continue;
            }

            _writebacks.Remember(
                result.TargetId,
                result.SourceText,
                result.TranslatedText,
                result.PreviousTranslatedText);
            if (TryApplyRemembered(target))
            {
                applied++;
            }
        }

        return applied;
    }

    public int ReapplyRemembered(int maxCount)
    {
        var applied = 0;
        foreach (var target in _targets.Values.ToArray())
        {
            if (applied >= maxCount)
            {
                break;
            }

            if (!target.IsAlive)
            {
                _targets.Remove(target.Id);
                _writebacks.Forget(target.Id);
                continue;
            }

            if (TryApplyRemembered(target))
            {
                applied++;
            }
        }

        return applied;
    }

    private bool TryApplyRemembered(IUnityTextTarget target)
    {
        if (!_writebacks.TryGetReplacement(target.Id, target.GetText(), out var replacement))
        {
            return false;
        }

        target.SetText(replacement);
        return true;
    }
}

using HUnityAutoTranslator.Core.Dispatching;

namespace HUnityAutoTranslator.Plugin.Unity;

internal sealed class UnityMainThreadResultApplier
{
    private readonly Dictionary<string, IUnityTextTarget> _targets = new();

    public void Register(IUnityTextTarget target)
    {
        _targets[target.Id] = target;
    }

    public int Apply(IReadOnlyList<TranslationResult> results)
    {
        var applied = 0;
        foreach (var result in results)
        {
            if (!_targets.TryGetValue(result.TargetId, out var target) || !target.IsAlive)
            {
                continue;
            }

            if (target.GetText() == result.SourceText)
            {
                target.SetText(result.TranslatedText);
                applied++;
            }
        }

        return applied;
    }
}

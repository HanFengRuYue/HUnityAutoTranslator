namespace HUnityAutoTranslator.Core.Control;

public static class TranslationHighlightMatcher
{
    public static bool IsSupported(TranslationHighlightRequest request)
    {
        return !string.IsNullOrWhiteSpace(request.ComponentHierarchy) &&
            !string.Equals(request.ComponentType, "IMGUI", StringComparison.OrdinalIgnoreCase);
    }

    public static TranslationHighlightTarget? FindBestMatch(
        TranslationHighlightRequest request,
        IEnumerable<TranslationHighlightTarget> targets)
    {
        if (!IsSupported(request))
        {
            return null;
        }

        var requestScene = Normalize(request.SceneName);
        var requestHierarchy = Normalize(request.ComponentHierarchy);
        var requestComponent = Normalize(request.ComponentType);

        return targets
            .Where(target => target.IsAlive)
            .Where(target => string.Equals(Normalize(target.ComponentHierarchy), requestHierarchy, StringComparison.Ordinal))
            .Where(target => requestScene.Length == 0 || string.Equals(Normalize(target.SceneName), requestScene, StringComparison.Ordinal))
            .Where(target => ComponentTypeMatches(requestComponent, target.ComponentType))
            .OrderByDescending(target => target.IsVisible)
            .FirstOrDefault();
    }

    private static bool ComponentTypeMatches(string requestComponent, string? targetComponent)
    {
        if (requestComponent.Length == 0)
        {
            return true;
        }

        var normalizedTarget = Normalize(targetComponent);
        return string.Equals(normalizedTarget, requestComponent, StringComparison.Ordinal) ||
            string.Equals(SimpleName(normalizedTarget), SimpleName(requestComponent), StringComparison.Ordinal);
    }

    private static string SimpleName(string value)
    {
        var index = value.LastIndexOf('.');
        return index < 0 || index == value.Length - 1 ? value : value.Substring(index + 1);
    }

    private static string Normalize(string? value)
    {
        return value?.Trim() ?? string.Empty;
    }
}

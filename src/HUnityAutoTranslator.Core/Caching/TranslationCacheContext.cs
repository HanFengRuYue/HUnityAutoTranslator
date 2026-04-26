namespace HUnityAutoTranslator.Core.Caching;

public sealed record TranslationCacheContext(
    string? SceneName = null,
    string? ComponentHierarchy = null,
    string? ComponentType = null)
{
    public static TranslationCacheContext Empty { get; } = new();
}

namespace HUnityAutoTranslator.Core.Caching;

public interface ITranslationCache
{
    bool TryGet(TranslationCacheKey key, out string translatedText);

    void Set(TranslationCacheKey key, string translatedText);

    int Count { get; }
}

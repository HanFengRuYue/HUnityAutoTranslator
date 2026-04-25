using System.Collections.Concurrent;

namespace HUnityAutoTranslator.Core.Caching;

public sealed class MemoryTranslationCache : ITranslationCache
{
    private readonly ConcurrentDictionary<TranslationCacheKey, string> _items = new();

    public int Count => _items.Count;

    public bool TryGet(TranslationCacheKey key, out string translatedText)
    {
        return _items.TryGetValue(key, out translatedText!);
    }

    public void Set(TranslationCacheKey key, string translatedText)
    {
        _items[key] = translatedText;
    }
}

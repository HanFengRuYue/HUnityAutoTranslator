using HUnityAutoTranslator.Core.Configuration;

namespace HUnityAutoTranslator.Core.Caching;

public interface ITranslationCache
{
    bool TryGet(TranslationCacheKey key, out string translatedText);

    bool TryGetReplacementFont(TranslationCacheKey key, TranslationCacheContext context, out string replacementFont);

    void RecordCaptured(TranslationCacheKey key, TranslationCacheContext? context = null);

    void Set(TranslationCacheKey key, string translatedText, TranslationCacheContext? context = null);

    IReadOnlyList<TranslationCacheEntry> GetPendingTranslations(
        string targetLanguage,
        ProviderProfile provider,
        string promptPolicyVersion,
        int limit);

    int Count { get; }

    TranslationCachePage Query(TranslationCacheQuery query);

    void Update(TranslationCacheEntry entry);

    void Delete(TranslationCacheEntry entry);

    string Export(string format);

    TranslationCacheImportResult Import(string content, string format);
}

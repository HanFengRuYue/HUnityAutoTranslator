namespace HUnityAutoTranslator.Core.Caching;

public interface ITranslationCache
{
    bool TryGet(TranslationCacheKey key, TranslationCacheContext? context, out string translatedText);

    bool TryGetReplacementFont(TranslationCacheKey key, TranslationCacheContext context, out string replacementFont);

    void RecordCaptured(TranslationCacheKey key, TranslationCacheContext? context = null);

    void Set(TranslationCacheKey key, string translatedText, TranslationCacheContext? context = null);

    IReadOnlyList<TranslationCacheEntry> GetPendingTranslations(
        string targetLanguage,
        string promptPolicyVersion,
        int limit);

    IReadOnlyList<TranslationContextExample> GetTranslationContextExamples(
        string currentSourceText,
        string targetLanguage,
        TranslationCacheContext? context,
        int maxExamples,
        int maxCharacters);

    int Count { get; }

    TranslationCachePage Query(TranslationCacheQuery query);

    TranslationCacheFilterOptionPage GetFilterOptions(TranslationCacheFilterOptionsQuery query);

    void Update(TranslationCacheEntry entry);

    void Delete(TranslationCacheEntry entry);

    string Export(string format);

    TranslationCacheImportResult Import(string content, string format);
}

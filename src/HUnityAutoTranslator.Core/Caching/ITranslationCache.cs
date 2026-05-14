namespace HUnityAutoTranslator.Core.Caching;

public interface ITranslationCache
{
    bool TryGet(TranslationCacheKey key, TranslationCacheContext? context, out string translatedText);

    bool TryGetReplacementFont(TranslationCacheKey key, TranslationCacheContext context, out string replacementFont);

    IReadOnlyList<TranslationCacheEntry> GetCompletedTranslationsBySource(
        TranslationCacheKey key,
        int limit);

    /// <summary>取所有「源文本包含指定子串」的已完成翻译行，用于术语跨上下文一致性检查。</summary>
    IReadOnlyList<TranslationCacheEntry> GetCompletedContainingSource(
        string sourceSubstring,
        string targetLanguage,
        int limit);

    /// <summary>取 updated_utc 大于水位线的已完成翻译行，按 updated_utc 升序，驱动术语提取的全量覆盖。</summary>
    IReadOnlyList<TranslationCacheEntry> GetCompletedSince(
        string targetLanguage,
        string? afterUpdatedUtc,
        int limit);

    /// <summary>取同一场景下、组件层级等于前缀或处于该前缀子树内的已完成翻译行，用于按父层级打包提取。</summary>
    IReadOnlyList<TranslationCacheEntry> GetCompletedInHierarchy(
        string targetLanguage,
        string sceneName,
        string componentHierarchyPrefix,
        int limit);

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

using HUnityAutoTranslator.Core.Caching;

namespace HUnityAutoTranslator.Core.Queueing;

public sealed record TranslationJob(
    string Id,
    string SourceText,
    TranslationPriority Priority,
    TranslationCacheContext Context,
    bool PublishResult,
    string? TargetLanguage)
{
    public static TranslationJob Create(
        string id,
        string sourceText,
        TranslationPriority priority,
        TranslationCacheContext? context = null,
        bool publishResult = true,
        string? targetLanguage = null)
    {
        return new TranslationJob(id, sourceText, priority, context ?? TranslationCacheContext.Empty, publishResult, targetLanguage);
    }
}

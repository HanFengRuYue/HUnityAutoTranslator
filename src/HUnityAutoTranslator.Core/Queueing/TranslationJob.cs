namespace HUnityAutoTranslator.Core.Queueing;

public sealed record TranslationJob(string Id, string SourceText, TranslationPriority Priority)
{
    public static TranslationJob Create(string id, string sourceText, TranslationPriority priority)
    {
        return new TranslationJob(id, sourceText, priority);
    }
}

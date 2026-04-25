namespace HUnityAutoTranslator.Core.Pipeline;

public enum PipelineDecisionKind
{
    Ignored,
    UseCachedTranslation,
    Queued
}

public sealed record PipelineDecision(PipelineDecisionKind Kind, string? TranslatedText)
{
    public static PipelineDecision Ignored() => new(PipelineDecisionKind.Ignored, null);

    public static PipelineDecision UseCachedTranslation(string translatedText)
    {
        return new PipelineDecision(PipelineDecisionKind.UseCachedTranslation, translatedText);
    }

    public static PipelineDecision Queued() => new(PipelineDecisionKind.Queued, null);
}

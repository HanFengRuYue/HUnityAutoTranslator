namespace HUnityAutoTranslator.Core.Glossary;

public enum GlossaryUpsertResult
{
    Created,
    Updated,
    SkippedInvalid,
    SkippedManualConflict,
    SkippedAutomaticConflict
}

namespace HUnityAutoTranslator.Core.Glossary;

public sealed record GlossaryTerm(
    string SourceTerm,
    string TargetTerm,
    string TargetLanguage,
    string NormalizedSourceTerm,
    string? Note,
    bool Enabled,
    GlossaryTermSource Source,
    int UsageCount,
    DateTimeOffset CreatedUtc,
    DateTimeOffset UpdatedUtc)
{
    public static GlossaryTerm CreateManual(string sourceTerm, string targetTerm, string targetLanguage, string? note)
    {
        return Create(sourceTerm, targetTerm, targetLanguage, note, enabled: true, GlossaryTermSource.Manual, usageCount: 0);
    }

    public static GlossaryTerm CreateAutomatic(string sourceTerm, string targetTerm, string targetLanguage, string? note)
    {
        return Create(sourceTerm, targetTerm, targetLanguage, note, enabled: true, GlossaryTermSource.Automatic, usageCount: 1);
    }

    public static GlossaryTerm Create(
        string sourceTerm,
        string targetTerm,
        string targetLanguage,
        string? note,
        bool enabled,
        GlossaryTermSource source,
        int usageCount = 0)
    {
        var now = DateTimeOffset.UtcNow;
        var cleanSource = Clean(sourceTerm);
        return new GlossaryTerm(
            cleanSource,
            Clean(targetTerm),
            string.IsNullOrWhiteSpace(targetLanguage) ? "zh-Hans" : targetLanguage.Trim(),
            NormalizeSourceTerm(cleanSource),
            string.IsNullOrWhiteSpace(note) ? null : note.Trim(),
            enabled,
            source,
            Math.Max(0, usageCount),
            now,
            now);
    }

    public static string NormalizeSourceTerm(string? value)
    {
        return Clean(value).ToUpperInvariant();
    }

    public static bool IsValid(GlossaryTerm term)
    {
        return !string.IsNullOrWhiteSpace(term.SourceTerm)
            && !string.IsNullOrWhiteSpace(term.TargetTerm)
            && !string.IsNullOrWhiteSpace(term.TargetLanguage)
            && !string.IsNullOrWhiteSpace(term.NormalizedSourceTerm);
    }

    public GlossaryTerm NormalizeForStorage()
    {
        var sourceTerm = Clean(SourceTerm);
        return this with
        {
            SourceTerm = sourceTerm,
            TargetTerm = Clean(TargetTerm),
            TargetLanguage = string.IsNullOrWhiteSpace(TargetLanguage) ? "zh-Hans" : TargetLanguage.Trim(),
            NormalizedSourceTerm = NormalizeSourceTerm(sourceTerm),
            Note = string.IsNullOrWhiteSpace(Note) ? null : Note.Trim(),
            UsageCount = Math.Max(0, UsageCount)
        };
    }

    private static string Clean(string? value)
    {
        return (value ?? string.Empty).Trim();
    }
}

namespace HUnityAutoTranslator.Core.Glossary;

public sealed record GlossaryPromptTerm(
    int TextIndex,
    string SourceTerm,
    string TargetTerm,
    string? Note);

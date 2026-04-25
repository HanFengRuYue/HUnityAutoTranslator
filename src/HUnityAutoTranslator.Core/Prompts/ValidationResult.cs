namespace HUnityAutoTranslator.Core.Prompts;

public sealed record ValidationResult(bool IsValid, string Reason)
{
    public static ValidationResult Valid() => new(true, string.Empty);

    public static ValidationResult Invalid(string reason) => new(false, reason);
}

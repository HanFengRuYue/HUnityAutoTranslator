namespace HUnityAutoTranslator.Core.Providers;

public sealed record TranslationResponse(
    bool Succeeded,
    IReadOnlyList<string> TranslatedTexts,
    string? ErrorMessage)
{
    public static TranslationResponse Success(IReadOnlyList<string> translatedTexts)
    {
        return new TranslationResponse(true, translatedTexts, null);
    }

    public static TranslationResponse Failure(string errorMessage)
    {
        return new TranslationResponse(false, Array.Empty<string>(), errorMessage);
    }
}

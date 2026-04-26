namespace HUnityAutoTranslator.Core.Providers;

public sealed record TranslationResponse(
    bool Succeeded,
    IReadOnlyList<string> TranslatedTexts,
    string? ErrorMessage,
    int TotalTokens = 0)
{
    public static TranslationResponse Success(IReadOnlyList<string> translatedTexts, int totalTokens = 0)
    {
        return new TranslationResponse(true, translatedTexts, null, totalTokens);
    }

    public static TranslationResponse Failure(string errorMessage)
    {
        return new TranslationResponse(false, Array.Empty<string>(), errorMessage);
    }
}

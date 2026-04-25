namespace HUnityAutoTranslator.Core.Text;

public sealed record ProtectedText(string Text, IReadOnlyDictionary<string, string> Tokens)
{
    public string Restore(string translatedText)
    {
        var restored = translatedText;
        foreach (var token in Tokens)
        {
            restored = restored.Replace(token.Key, token.Value);
        }

        return restored;
    }
}

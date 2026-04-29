namespace HUnityAutoTranslator.Core.Text;

public static class RichTextGuard
{
    public static bool HasSameTags(string source, string translated)
    {
        if (RichTextTranslationPreprocessor.IsCompatiblePerCharacterTranslation(source, translated))
        {
            return true;
        }

        return ExtractTags(source).SequenceEqual(ExtractTags(translated), StringComparer.OrdinalIgnoreCase);
    }

    public static string GetVisibleText(string value)
    {
        return RichTextMarkupParser.GetVisibleText(value);
    }

    public static IReadOnlyList<string> ExtractTags(string value)
    {
        return RichTextMarkupParser.ExtractTagNames(value);
    }
}

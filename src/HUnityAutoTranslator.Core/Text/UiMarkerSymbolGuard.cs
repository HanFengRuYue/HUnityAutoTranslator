namespace HUnityAutoTranslator.Core.Text;

internal static class UiMarkerSymbolGuard
{
    private static readonly string[] MarkerSymbols =
    {
        ">>",
        ">",
        "-",
        "*",
        "\u2022",
        "\u25b6",
        "\u2192"
    };

    public static string PreserveSourceMarkers(string sourceText, string translatedText)
    {
        var sourceMarkers = Capture(sourceText, requirePrefixWhitespace: true, requireSuffixWhitespace: true);
        if (!sourceMarkers.HasAny || string.IsNullOrEmpty(translatedText))
        {
            return translatedText;
        }

        var result = translatedText;
        if (sourceMarkers.Prefix != null)
        {
            result = PreservePrefix(sourceMarkers.Prefix.Value, result);
        }

        if (sourceMarkers.Suffix != null)
        {
            result = PreserveSuffix(sourceMarkers.Suffix.Value, result);
        }

        return result;
    }

    public static bool HasChangedMarkers(string sourceText, string translatedText)
    {
        var sourceMarkers = Capture(sourceText, requirePrefixWhitespace: true, requireSuffixWhitespace: true);
        var translatedMarkers = Capture(translatedText, requirePrefixWhitespace: true, requireSuffixWhitespace: true);
        return HasChanged(sourceMarkers.Prefix, translatedMarkers.Prefix) ||
            HasChanged(sourceMarkers.Suffix, translatedMarkers.Suffix);
    }

    private static string PreservePrefix(UiMarkerAffix sourcePrefix, string value)
    {
        var trimmed = value.TrimStart(Array.Empty<char>());
        var translatedPrefix = FindPrefix(trimmed, requireWhitespace: false);
        if (translatedPrefix != null)
        {
            if (!string.Equals(translatedPrefix.Value.Symbol, sourcePrefix.Symbol, StringComparison.Ordinal))
            {
                return value;
            }

            return sourcePrefix.Text + trimmed.Substring(translatedPrefix.Value.Text.Length).TrimStart(Array.Empty<char>());
        }

        return sourcePrefix.Text + trimmed;
    }

    private static string PreserveSuffix(UiMarkerAffix sourceSuffix, string value)
    {
        var trimmed = value.TrimEnd(Array.Empty<char>());
        var translatedSuffix = FindSuffix(trimmed, requireWhitespace: false);
        if (translatedSuffix != null)
        {
            if (!string.Equals(translatedSuffix.Value.Symbol, sourceSuffix.Symbol, StringComparison.Ordinal))
            {
                return value;
            }

            return trimmed.Substring(0, trimmed.Length - translatedSuffix.Value.Text.Length).TrimEnd(Array.Empty<char>()) + sourceSuffix.Text;
        }

        return trimmed + sourceSuffix.Text;
    }

    private static bool HasChanged(UiMarkerAffix? source, UiMarkerAffix? translated)
    {
        if (source == null && translated == null)
        {
            return false;
        }

        if (source == null || translated == null)
        {
            return true;
        }

        return !string.Equals(source.Value.Symbol, translated.Value.Symbol, StringComparison.Ordinal);
    }

    private static UiMarkerSnapshot Capture(string value, bool requirePrefixWhitespace, bool requireSuffixWhitespace)
    {
        var source = value ?? string.Empty;
        return new UiMarkerSnapshot(
            FindPrefix(source, requirePrefixWhitespace),
            FindSuffix(source, requireSuffixWhitespace));
    }

    private static UiMarkerAffix? FindPrefix(string value, bool requireWhitespace)
    {
        var trimmed = (value ?? string.Empty).TrimStart(Array.Empty<char>());
        foreach (var symbol in MarkerSymbols)
        {
            if (!trimmed.StartsWith(symbol, StringComparison.Ordinal) ||
                trimmed.Length <= symbol.Length)
            {
                continue;
            }

            var end = symbol.Length;
            if (IsHorizontalWhitespace(trimmed[end]))
            {
                while (end < trimmed.Length && IsHorizontalWhitespace(trimmed[end]))
                {
                    end++;
                }
            }
            else if (requireWhitespace)
            {
                continue;
            }

            if (end >= trimmed.Length)
            {
                continue;
            }

            return new UiMarkerAffix(symbol, trimmed.Substring(0, end));
        }

        return null;
    }

    private static UiMarkerAffix? FindSuffix(string value, bool requireWhitespace)
    {
        var trimmed = (value ?? string.Empty).TrimEnd(Array.Empty<char>());
        foreach (var symbol in MarkerSymbols)
        {
            if (!trimmed.EndsWith(symbol, StringComparison.Ordinal) ||
                trimmed.Length <= symbol.Length)
            {
                continue;
            }

            var start = trimmed.Length - symbol.Length;
            var whitespaceStart = start;
            while (whitespaceStart > 0 && IsHorizontalWhitespace(trimmed[whitespaceStart - 1]))
            {
                whitespaceStart--;
            }

            if (whitespaceStart == start && requireWhitespace)
            {
                continue;
            }

            if (whitespaceStart == 0)
            {
                continue;
            }

            return new UiMarkerAffix(symbol, trimmed.Substring(whitespaceStart));
        }

        return null;
    }

    private static bool IsHorizontalWhitespace(char value)
    {
        return value is ' ' or '\t' or '\u00a0';
    }

    private readonly struct UiMarkerSnapshot
    {
        public UiMarkerSnapshot(UiMarkerAffix? prefix, UiMarkerAffix? suffix)
        {
            Prefix = prefix;
            Suffix = suffix;
        }

        public UiMarkerAffix? Prefix { get; }

        public UiMarkerAffix? Suffix { get; }

        public bool HasAny => Prefix != null || Suffix != null;
    }

    private readonly struct UiMarkerAffix
    {
        public UiMarkerAffix(string symbol, string text)
        {
            Symbol = symbol;
            Text = text;
        }

        public string Symbol { get; }

        public string Text { get; }
    }
}

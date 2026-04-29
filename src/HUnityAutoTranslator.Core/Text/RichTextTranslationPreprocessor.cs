using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace HUnityAutoTranslator.Core.Text;

public sealed class RichTextTranslationPreparation
{
    private readonly PerCharacterRichTextTemplate? _template;

    internal RichTextTranslationPreparation(string sourceText, string providerText, PerCharacterRichTextTemplate? template)
    {
        SourceText = sourceText;
        ProviderText = providerText;
        _template = template;
    }

    public string SourceText { get; }

    public string ProviderText { get; }

    public bool RebuildsRichText => _template != null;

    public string RebuildTranslation(string translatedProviderText)
    {
        return _template == null
            ? translatedProviderText
            : _template.Rebuild(translatedProviderText);
    }
}

public static class RichTextTranslationPreprocessor
{
    public static RichTextTranslationPreparation Prepare(string sourceText)
    {
        return TryCreatePerCharacterTemplate(sourceText, out var template)
            ? new RichTextTranslationPreparation(sourceText, template.PlainText, template)
            : new RichTextTranslationPreparation(sourceText, sourceText, null);
    }

    internal static bool TryCreatePerCharacterTemplate(string sourceText, out PerCharacterRichTextTemplate template)
    {
        if (!RichTextMarkupParser.TryParse(sourceText, out var elements) ||
            elements.Count == 0)
        {
            template = default!;
            return false;
        }

        var styledElements = elements.Where(item => item.OpenTags.Count > 0).ToArray();
        if (styledElements.Length == 0 ||
            !styledElements.Any(item => item.TagNames.Contains("rotate", StringComparer.OrdinalIgnoreCase)))
        {
            template = default!;
            return false;
        }

        var plainText = string.Concat(elements.Select(item => item.Text));
        if (string.IsNullOrWhiteSpace(plainText))
        {
            template = default!;
            return false;
        }

        template = new PerCharacterRichTextTemplate(plainText, elements);
        return true;
    }

    internal static bool IsCompatiblePerCharacterTranslation(string sourceText, string translatedText)
    {
        if (!TryCreatePerCharacterTemplate(sourceText, out var template))
        {
            return false;
        }

        return template.IsCompatibleTranslation(translatedText);
    }
}

internal sealed class PerCharacterRichTextTemplate
{
    private readonly RichTextStyle _baseStyle;
    private readonly IReadOnlyDictionary<string, RichTextStyle> _exactStyles;
    private readonly RichTextStyle? _punctuationStyle;
    private readonly HashSet<string> _sourceTagNames;

    public PerCharacterRichTextTemplate(string plainText, IReadOnlyList<RichTextTextElement> elements)
    {
        PlainText = plainText;
        var styledElements = elements.Where(item => item.OpenTags.Count > 0).ToArray();
        _baseStyle = RichTextStyle.From(styledElements.First(item => item.TagNames.Contains("rotate", StringComparer.OrdinalIgnoreCase)));
        _exactStyles = styledElements
            .GroupBy(item => item.Text, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => RichTextStyle.From(group.First()), StringComparer.Ordinal);
        _punctuationStyle = styledElements
            .Where(item => IsPunctuation(item.Text))
            .OrderByDescending(item => item.OpenTags.Count)
            .Select(RichTextStyle.From)
            .FirstOrDefault();
        _sourceTagNames = new HashSet<string>(
            styledElements.SelectMany(item => item.TagNames),
            StringComparer.OrdinalIgnoreCase);
    }

    public string PlainText { get; }

    public string Rebuild(string translatedText)
    {
        var visibleText = RichTextGuard.GetVisibleText(translatedText);
        if (string.IsNullOrEmpty(visibleText))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(visibleText.Length * 3);
        foreach (var textElement in SplitTextElements(visibleText))
        {
            if (string.IsNullOrWhiteSpace(textElement))
            {
                builder.Append(textElement);
                continue;
            }

            var style = ResolveStyle(textElement);
            builder.Append(style.OpenText);
            builder.Append(textElement);
            builder.Append(style.CloseText);
        }

        return builder.ToString();
    }

    public bool IsCompatibleTranslation(string translatedText)
    {
        if (string.IsNullOrWhiteSpace(translatedText) ||
            RichTextMarkupParser.HasEmptyTagPair(translatedText) ||
            !RichTextMarkupParser.TryParse(translatedText, out var elements) ||
            elements.Count == 0)
        {
            return false;
        }

        var styledElements = elements.Where(item => item.OpenTags.Count > 0).ToArray();
        if (styledElements.Length == 0 ||
            !styledElements.Any(item => item.TagNames.Contains("rotate", StringComparer.OrdinalIgnoreCase)))
        {
            return false;
        }

        if (styledElements.Any(item => item.TagNames.Any(tag => !_sourceTagNames.Contains(tag))))
        {
            return false;
        }

        return !string.IsNullOrWhiteSpace(string.Concat(elements.Select(item => item.Text)));
    }

    private RichTextStyle ResolveStyle(string textElement)
    {
        if (_exactStyles.TryGetValue(textElement, out var exactStyle))
        {
            return exactStyle;
        }

        if (_punctuationStyle != null && IsPunctuation(textElement))
        {
            return _punctuationStyle;
        }

        return _baseStyle;
    }

    private static IReadOnlyList<string> SplitTextElements(string value)
    {
        if (value.Length == 0)
        {
            return Array.Empty<string>();
        }

        var indexes = StringInfo.ParseCombiningCharacters(value);
        var result = new string[indexes.Length];
        for (var i = 0; i < indexes.Length; i++)
        {
            var start = indexes[i];
            var end = i + 1 < indexes.Length ? indexes[i + 1] : value.Length;
            result[i] = value.Substring(start, end - start);
        }

        return result;
    }

    private static bool IsPunctuation(string textElement)
    {
        return textElement.Length > 0 &&
            textElement.All(character =>
            {
                var category = char.GetUnicodeCategory(character);
                return category is UnicodeCategory.OtherPunctuation
                    or UnicodeCategory.DashPunctuation
                    or UnicodeCategory.OpenPunctuation
                    or UnicodeCategory.ClosePunctuation
                    or UnicodeCategory.InitialQuotePunctuation
                    or UnicodeCategory.FinalQuotePunctuation;
            });
    }
}

internal sealed record RichTextTextElement(
    string Text,
    IReadOnlyList<string> OpenTags,
    IReadOnlyList<string> CloseTags,
    IReadOnlyList<string> TagNames);

internal sealed record RichTextStyle(string OpenText, string CloseText)
{
    public static RichTextStyle From(RichTextTextElement element)
    {
        return new RichTextStyle(
            string.Concat(element.OpenTags),
            string.Concat(element.CloseTags));
    }
}

internal static class RichTextMarkupParser
{
    private static readonly Regex TagRegex = new(@"<\s*(/?)\s*([A-Za-z][\w.-]*)(?:\s*=[^>]*)?\s*(/?)\s*>", RegexOptions.Compiled);
    private static readonly Regex EmptyTagPairRegex = new(
        @"<\s*([A-Za-z][\w.-]*)(?:\s*=[^>]*)?\s*>\s*</\s*\1\s*>",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static bool TryParse(string value, out IReadOnlyList<RichTextTextElement> elements)
    {
        var source = value ?? string.Empty;
        var parsed = new List<RichTextTextElement>();
        var stack = new List<OpenRichTextTag>();
        var index = 0;
        foreach (Match match in TagRegex.Matches(source))
        {
            if (match.Index > index)
            {
                AddTextElements(source.Substring(index, match.Index - index), stack, parsed);
            }

            var isClosing = match.Groups[1].Value == "/";
            var name = match.Groups[2].Value;
            var isSelfClosing = match.Groups[3].Value == "/";
            if (isClosing)
            {
                if (stack.Count == 0 ||
                    !string.Equals(stack[^1].Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    elements = Array.Empty<RichTextTextElement>();
                    return false;
                }

                stack.RemoveAt(stack.Count - 1);
            }
            else if (!isSelfClosing)
            {
                stack.Add(new OpenRichTextTag(match.Value, name));
            }

            index = match.Index + match.Length;
        }

        if (index < source.Length)
        {
            AddTextElements(source.Substring(index), stack, parsed);
        }

        if (stack.Count != 0)
        {
            elements = Array.Empty<RichTextTextElement>();
            return false;
        }

        elements = parsed;
        return true;
    }

    public static string GetVisibleText(string value)
    {
        return TagRegex.Replace(value ?? string.Empty, string.Empty);
    }

    public static bool HasEmptyTagPair(string value)
    {
        return EmptyTagPairRegex.IsMatch(value ?? string.Empty);
    }

    public static IReadOnlyList<string> ExtractTagNames(string value)
    {
        return TagRegex.Matches(value ?? string.Empty)
            .Select(match =>
            {
                var closePrefix = match.Groups[1].Value == "/" ? "/" : string.Empty;
                return closePrefix + match.Groups[2].Value.ToLowerInvariant();
            })
            .ToArray();
    }

    private static void AddTextElements(
        string text,
        IReadOnlyList<OpenRichTextTag> stack,
        List<RichTextTextElement> elements)
    {
        if (text.Length == 0)
        {
            return;
        }

        var indexes = StringInfo.ParseCombiningCharacters(text);
        for (var i = 0; i < indexes.Length; i++)
        {
            var start = indexes[i];
            var end = i + 1 < indexes.Length ? indexes[i + 1] : text.Length;
            elements.Add(new RichTextTextElement(
                text.Substring(start, end - start),
                stack.Select(item => item.RawText).ToArray(),
                stack.AsEnumerable().Reverse().Select(item => "</" + item.Name + ">").ToArray(),
                stack.Select(item => item.Name.ToLowerInvariant()).ToArray()));
        }
    }

    private sealed record OpenRichTextTag(string RawText, string Name);
}

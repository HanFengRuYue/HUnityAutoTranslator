using System.Text.RegularExpressions;
using HUnityAutoTranslator.Core.Text;
using Newtonsoft.Json.Linq;

namespace HUnityAutoTranslator.Core.Prompts;

public static class TranslationOutputValidator
{
    private static readonly Regex NumberedPrefixPattern = new(@"^\s*\d+\s*[:：.]\s+\S", RegexOptions.Compiled);

    private static readonly string[] ExplanatoryPrefixes =
    {
        "翻译如下",
        "译文",
        "以下是",
        "Translation:",
        "Translated:",
        "Here is"
    };

    public static ValidationResult ValidateSingle(string sourceText, string translatedText, bool requireSameRichTextTags)
    {
        if (string.IsNullOrWhiteSpace(translatedText))
        {
            return ValidationResult.Invalid("译文为空");
        }

        if (HasStructuredJsonResponseArtifact(translatedText))
        {
            return ValidationResult.Invalid("contains structured JSON response artifact");
        }

        if (PreservableTextClassifier.CanRemainUntranslated(sourceText))
        {
            var visibleSourceText = RichTextGuard.GetVisibleText(sourceText).Trim();
            var visibleTranslatedText = RichTextGuard.GetVisibleText(translatedText).Trim();
            if (!string.Equals(visibleSourceText, visibleTranslatedText, StringComparison.Ordinal))
            {
                return ValidationResult.Invalid("preservable technical text must remain unchanged");
            }
        }

        var trimmed = translatedText.TrimStart();
        if (ExplanatoryPrefixes.Any(prefix => trimmed.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
        {
            return ValidationResult.Invalid("包含解释性前缀");
        }

        if (HasGeneratedNumberedPrefix(sourceText, translatedText))
        {
            return ValidationResult.Invalid("包含编号前缀");
        }

        var sourcePlaceholders = PlaceholderProtector.ExtractPlaceholders(sourceText);
        var translatedPlaceholders = PlaceholderProtector.ExtractPlaceholders(translatedText);
        if (!sourcePlaceholders.SequenceEqual(translatedPlaceholders))
        {
            return ValidationResult.Invalid("占位符集合不一致");
        }

        if (requireSameRichTextTags && !RichTextGuard.HasSameTags(sourceText, translatedText))
        {
            return ValidationResult.Invalid("富文本标签不完整");
        }

        return ValidationResult.Valid();
    }

    private static bool HasGeneratedNumberedPrefix(string sourceText, string translatedText)
    {
        return NumberedPrefixPattern.IsMatch(translatedText) &&
            !NumberedPrefixPattern.IsMatch(sourceText);
    }

    private static bool HasStructuredJsonResponseArtifact(string translatedText)
    {
        var trimmed = translatedText.Trim();
        if (trimmed.Length == 0 ||
            !((trimmed.StartsWith("{", StringComparison.Ordinal) && trimmed.EndsWith("}", StringComparison.Ordinal)) ||
              (trimmed.StartsWith("[", StringComparison.Ordinal) && trimmed.EndsWith("]", StringComparison.Ordinal))))
        {
            return false;
        }

        try
        {
            return JToken.Parse(trimmed) is JObject or JArray;
        }
        catch
        {
            return false;
        }
    }

    public static ValidationResult ValidateBatch(IReadOnlyList<string> sourceTexts, IReadOnlyList<string> translatedTexts)
    {
        if (sourceTexts.Count != translatedTexts.Count)
        {
            return ValidationResult.Invalid("批量翻译数量不匹配");
        }

        for (var i = 0; i < sourceTexts.Count; i++)
        {
            var result = ValidateSingle(sourceTexts[i], translatedTexts[i], requireSameRichTextTags: true);
            if (!result.IsValid)
            {
                return ValidationResult.Invalid($"第 {i} 条无效：{result.Reason}");
            }
        }

        return ValidationResult.Valid();
    }
}

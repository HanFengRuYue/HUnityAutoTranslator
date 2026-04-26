using System.Text.RegularExpressions;
using HUnityAutoTranslator.Core.Text;

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

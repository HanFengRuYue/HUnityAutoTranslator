namespace HUnityAutoTranslator.Core.Prompts;

public static class PromptBuilder
{
    public static string BuildSystemPrompt(PromptOptions options)
    {
        var style = options.Style switch
        {
            TranslationStyle.Faithful => "风格：忠实保留原意，避免增删信息。",
            TranslationStyle.Natural => "风格：表达自然流畅，避免机器翻译腔。",
            TranslationStyle.Localized => "风格：允许自然本地化，符合游戏语境和角色口吻。",
            TranslationStyle.UiConcise => "风格：UI、菜单和按钮要短而清楚。",
            _ => "风格：自然准确。"
        };

        var custom = string.IsNullOrWhiteSpace(options.CustomInstruction)
            ? string.Empty
            : "\n附加风格要求：" + options.CustomInstruction.Trim();

        return $"""
你是游戏本地化翻译引擎。目标语言：{options.TargetLanguage}。
只输出译文，不要解释，不要寒暄，不要添加引号、Markdown 或“翻译如下”等前缀。
不要改变占位符、控制符、换行符、Unity 富文本标签或 TextMeshPro 标签。
允许自然本地化，避免机器翻译腔；菜单和按钮要短，对话要符合角色口吻。
{style}{custom}
""";
    }

    public static string BuildSingleUserPrompt(string protectedText)
    {
        return "翻译以下文本，只返回译文：\n" + protectedText;
    }

    public static string BuildBatchUserPrompt(IReadOnlyList<string> protectedTexts)
    {
        var lines = protectedTexts.Select((text, index) => $"{index}: {text}");
        return "翻译以下 JSON 数组语义的文本，必须只返回 JSON 字符串数组，数量和顺序必须一致：\n" + string.Join("\n", lines);
    }

    public static string BuildRepairPrompt(string sourceText, string invalidTranslation, string reason)
    {
        return $"""
上一次翻译结果无效，原因：{reason}
请重新翻译，只输出修复后的译文，不要解释。
原文：{sourceText}
无效译文：{invalidTranslation}
""";
    }
}

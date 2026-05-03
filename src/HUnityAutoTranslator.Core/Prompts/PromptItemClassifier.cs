using System.Text.RegularExpressions;
using HUnityAutoTranslator.Core.Text;

namespace HUnityAutoTranslator.Core.Prompts;

public static class PromptItemClassifier
{
    private static readonly Regex WhitespacePattern = new(@"\s+", RegexOptions.Compiled);

    public static IReadOnlyList<string> BuildHints(string sourceText, PromptItemContext? context, string? gameTitle)
    {
        var hints = new List<string>();
        var source = RichTextGuard.GetVisibleText(sourceText ?? string.Empty);
        var hierarchy = context?.ComponentHierarchy ?? string.Empty;
        var scene = context?.SceneName ?? string.Empty;
        var combined = (source + "\n" + hierarchy + "\n" + scene).ToLowerInvariant();

        if (ContainsGameTitle(source, gameTitle))
        {
            hints.Add("game_title");
        }

        if (ContainsAny(combined, "title", "/name"))
        {
            hints.Add("title_text");
        }

        if (ContainsAny(combined, "protanopia", "deuteranopia", "tritanopia", "colorblind", "colourblind", "color blind", "accessibility"))
        {
            hints.Add("accessibility_option");
        }

        if (IsToggleStateSource(source) || ContainsAny(combined, "toggle", "v-sync", "vsync", "fullscreen", "full screen"))
        {
            hints.Add("toggle_state");
        }

        if (IsSettingsContext(combined) && IsShortUiText(source))
        {
            hints.Add("settings_value");
        }

        if (ContainsAny(combined, "button", "/play/", "/main/", "main menu", "menu/") && IsShortUiText(source))
        {
            hints.Add("menu_action");
        }

        if (ContainsAny(combined, "warning", "disclaimer", "caution", "epilepsy", "headphones") || source.Length >= 120)
        {
            hints.Add("warning_text");
        }

        if (ContainsAny(combined, "anti-aliasing", "v-sync", "vsync", "fps", "resolution", "volumetric", "texture", "shadow"))
        {
            hints.Add("technical_label");
        }

        if (hints.Count == 0 && IsShortUiText(source))
        {
            hints.Add("short_ui_text");
        }

        return hints.Distinct(StringComparer.Ordinal).ToArray();
    }

    public static bool ContainsGameTitle(string? value, string? gameTitle)
    {
        var normalizedValue = NormalizeForMatch(value);
        var normalizedTitle = NormalizeForMatch(gameTitle);
        return normalizedTitle.Length > 0 &&
            normalizedValue.IndexOf(normalizedTitle, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    public static string NormalizeForMatch(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return WhitespacePattern.Replace(RichTextGuard.GetVisibleText(value).Trim(), " ");
    }

    public static string? GetParentHierarchy(string? componentHierarchy)
    {
        var normalized = string.IsNullOrWhiteSpace(componentHierarchy) ? null : componentHierarchy.Trim();
        if (normalized == null)
        {
            return null;
        }

        var index = normalized.LastIndexOf('/');
        return index <= 0 ? null : normalized[..index];
    }

    public static string? GetOptionContainerHierarchy(string? componentHierarchy)
    {
        var normalized = string.IsNullOrWhiteSpace(componentHierarchy) ? null : componentHierarchy.Trim();
        if (normalized == null)
        {
            return null;
        }

        var segments = normalized.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length <= 1)
        {
            return null;
        }

        var switchIndex = Array.FindIndex(segments, segment => string.Equals(segment.Trim(), "Switch", StringComparison.OrdinalIgnoreCase));
        if (switchIndex > 0)
        {
            return JoinSegments(segments, switchIndex);
        }

        return JoinSegments(segments, segments.Length - 1);
    }

    public static string? GetSettingGroupHierarchy(string? componentHierarchy)
    {
        var optionContainer = GetOptionContainerHierarchy(componentHierarchy);
        return GetParentHierarchy(optionContainer);
    }

    public static bool IsSimplifiedChineseTarget(string targetLanguage)
    {
        var normalized = (targetLanguage ?? string.Empty).Trim().ToLowerInvariant();
        return normalized is "zh-hans" or "zh-cn" or "zh-sg" or "simplified chinese";
    }

    private static bool IsShortUiText(string sourceText)
    {
        return !string.IsNullOrWhiteSpace(sourceText) && sourceText.Trim().Length <= 32;
    }

    private static bool IsSettingsContext(string value)
    {
        return ContainsAny(
            value,
            "settings",
            "options",
            "gameplay panel",
            "screen",
            "graphics",
            "quality",
            "texture",
            "shadow",
            "volume",
            "resolution",
            "reticle",
            "subtitles");
    }

    private static bool IsToggleStateSource(string value)
    {
        var normalized = NormalizeForMatch(value).ToLowerInvariant();
        return normalized is "on" or "off" or "activated" or "active" or "inactive" or "enabled" or "disabled";
    }

    internal static bool IsToggleStateText(string value)
    {
        return IsToggleStateSource(value);
    }

    private static string JoinSegments(IReadOnlyList<string> segments, int count)
    {
        return count <= 0 ? string.Empty : string.Join("/", segments.Take(count).Select(segment => segment.Trim()));
    }

    private static bool ContainsAny(string value, params string[] needles)
    {
        return needles.Any(needle => value.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0);
    }
}

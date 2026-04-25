using System.Text;

namespace HUnityAutoTranslator.Core.Text;

public static class TextNormalizer
{
    public static string NormalizeForCache(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = value
            .Replace("\\r\\n", "\n")
            .Replace("\\r", "\n")
            .Replace("\\n", "\n")
            .Replace("\r\n", "\n")
            .Replace('\r', '\n')
            .Replace('\u00A0', ' ');

        var builder = new StringBuilder(normalized.Length);
        var previousWasHorizontalSpace = false;

        foreach (var ch in normalized.Trim())
        {
            if (ch == '\n')
            {
                builder.Append('\n');
                previousWasHorizontalSpace = false;
                continue;
            }

            if (ch == ' ' || ch == '\t')
            {
                if (!previousWasHorizontalSpace)
                {
                    builder.Append(' ');
                    previousWasHorizontalSpace = true;
                }

                continue;
            }

            builder.Append(ch);
            previousWasHorizontalSpace = false;
        }

        return builder.ToString().Trim();
    }
}

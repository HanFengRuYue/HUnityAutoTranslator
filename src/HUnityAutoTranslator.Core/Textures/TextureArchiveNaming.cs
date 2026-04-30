using System.Text;

namespace HUnityAutoTranslator.Core.Textures;

public static class TextureArchiveNaming
{
    public static string BuildTextureEntryName(string sourceHash, string? textureName)
    {
        var hash = SanitizeHash(sourceHash);
        var name = SanitizeFileNamePart(textureName);
        return $"textures/{hash}-{name}.png";
    }

    public static string BuildOverrideFileName(string sourceHash)
    {
        return SanitizeHash(sourceHash) + ".png";
    }

    public static bool IsSafeArchivePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var normalized = path.Replace('\\', '/');
        if (normalized.StartsWith("/", StringComparison.Ordinal) ||
            normalized.Contains(':', StringComparison.Ordinal) ||
            !normalized.StartsWith("textures/", StringComparison.OrdinalIgnoreCase) ||
            !normalized.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var parts = normalized.Split('/');
        return parts.All(part => part.Length > 0 && part != "." && part != "..");
    }

    private static string SanitizeHash(string? value)
    {
        var hash = new string((value ?? string.Empty)
            .Where(char.IsLetterOrDigit)
            .Take(64)
            .ToArray())
            .ToLowerInvariant();
        return hash.Length == 0 ? "unknown" : hash;
    }

    private static string SanitizeFileNamePart(string? value)
    {
        var builder = new StringBuilder();
        var previousDash = false;
        foreach (var ch in (value ?? string.Empty).Trim().ToLowerInvariant())
        {
            if ((ch >= 'a' && ch <= 'z') || (ch >= '0' && ch <= '9'))
            {
                builder.Append(ch);
                previousDash = false;
            }
            else if (!previousDash && (char.IsWhiteSpace(ch) || ch == '-' || ch == '_'))
            {
                builder.Append('-');
                previousDash = true;
            }

            if (builder.Length >= 48)
            {
                break;
            }
        }

        var name = builder.ToString().Trim('-');
        return name.Length == 0 ? "texture" : name;
    }
}

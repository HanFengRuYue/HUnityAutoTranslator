using System.Security.Cryptography;
using System.Text;
using HUnityAutoTranslator.Core.Configuration;
using HUnityAutoTranslator.Core.Text;

namespace HUnityAutoTranslator.Core.Caching;

public sealed record TranslationCacheKey(string Value)
{
    public static TranslationCacheKey Create(string sourceText, string targetLanguage, ProviderProfile provider, string promptPolicyVersion)
    {
        var normalized = TextNormalizer.NormalizeForCache(sourceText);
        var raw = string.Join(
            "\u001f",
            normalized,
            targetLanguage.Trim(),
            provider.Kind.ToString(),
            provider.BaseUrl.TrimEnd('/'),
            provider.Endpoint,
            provider.Model,
            promptPolicyVersion);

        using var sha = SHA256.Create();
        var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(raw));
        return new TranslationCacheKey(ToHex(hash));
    }

    private static string ToHex(byte[] bytes)
    {
        var builder = new StringBuilder(bytes.Length * 2);
        foreach (var value in bytes)
        {
            builder.Append(value.ToString("x2"));
        }

        return builder.ToString();
    }
}

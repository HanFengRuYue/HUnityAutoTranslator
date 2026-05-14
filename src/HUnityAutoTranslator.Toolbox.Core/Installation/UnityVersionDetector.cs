using System.Text;
using System.Text.RegularExpressions;

namespace HUnityAutoTranslator.Toolbox.Core.Installation;

/// <summary>
/// Reads the Unity engine version string out of a game's serialized data files.
/// BepInEx 6 IL2CPP needs this version to fetch the matching Unity base libraries on first run;
/// the Toolbox pre-stages those libraries so the user never sees a network call at launch time.
/// </summary>
public static class UnityVersionDetector
{
    // Unity SerializedFile / AssetBundle headers stamp the engine version as ASCII bytes near the
    // start of the file. The version always looks like e.g. "2022.3.21f1" -- four digit year,
    // dot, digits, dot, digits, then one of [abfp] + digits.
    private static readonly Regex UnityVersionRegex = new(
        @"\b(\d{4}\.\d+\.\d+[abfp]\d*)\b",
        RegexOptions.Compiled);

    private const int HeaderReadBytes = 4096;

    public sealed record UnityVersionInfo(string Version, string SourceFile);

    public static UnityVersionInfo? TryDetect(string gameRoot)
    {
        if (string.IsNullOrWhiteSpace(gameRoot) || !Directory.Exists(gameRoot))
        {
            return null;
        }

        var dataDir = Directory.EnumerateDirectories(gameRoot, "*_Data", SearchOption.TopDirectoryOnly)
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
        if (dataDir is null)
        {
            return null;
        }

        // Try the canonical sources in order. globalgamemanagers is the standard Unity 5+ file.
        // data.unity3d is the AssetBundle-style archive used by some packaged builds.
        var candidates = new[]
        {
            Path.Combine(dataDir, "globalgamemanagers"),
            Path.Combine(dataDir, "data.unity3d"),
            Path.Combine(dataDir, "mainData")
        };

        foreach (var path in candidates)
        {
            if (!File.Exists(path)) continue;
            var version = TryReadVersionFromFile(path);
            if (version is not null)
            {
                return new UnityVersionInfo(version, path);
            }
        }

        return null;
    }

    private static string? TryReadVersionFromFile(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            var buffer = new byte[Math.Min(HeaderReadBytes, (int)Math.Min(stream.Length, HeaderReadBytes))];
            var read = stream.Read(buffer, 0, buffer.Length);
            if (read <= 0) return null;

            // Decode as ASCII (Latin1-safe) so binary bytes don't blow up the regex.
            var ascii = Encoding.GetEncoding("ISO-8859-1").GetString(buffer, 0, read);
            var match = UnityVersionRegex.Match(ascii);
            return match.Success ? match.Groups[1].Value : null;
        }
        catch
        {
            return null;
        }
    }
}

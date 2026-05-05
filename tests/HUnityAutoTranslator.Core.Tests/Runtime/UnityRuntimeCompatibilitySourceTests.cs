using System.Text.RegularExpressions;
using FluentAssertions;

namespace HUnityAutoTranslator.Core.Tests.Runtime;

public sealed partial class UnityRuntimeCompatibilitySourceTests
{
    [Fact]
    public void Runtime_sources_do_not_call_netstandard_string_overloads_missing_from_unity_mono()
    {
        var sourceRoots = new[]
        {
            FindRepositoryDirectory("src", "HUnityAutoTranslator.Core"),
            FindRepositoryDirectory("src", "HUnityAutoTranslator.Plugin")
        };

        var patterns = new[]
        {
            (Pattern: ParameterlessTrimStartEndPattern(), Reason: "parameterless TrimStart/TrimEnd overload"),
            (Pattern: CharStringOverloadPattern(), Reason: "char-based string overload"),
            (Pattern: StringComparisonReplaceContainsPattern(), Reason: "StringComparison Replace/Contains overload"),
            (Pattern: KnownMissingBclHelperPattern(), Reason: "newer BCL helper"),
            (Pattern: RegexMatchCollectionLinqPattern(), Reason: "LINQ over Regex MatchCollection")
        };

        var matches = sourceRoots
            .SelectMany(root => Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
            .SelectMany(path =>
            {
                var source = File.ReadAllText(path);
                var relativePath = Path.GetRelativePath(FindRepositoryRoot(), path);
                return patterns.SelectMany(pattern => pattern.Pattern
                    .Matches(source)
                    .Select(match => $"{relativePath}:{LineNumber(source, match.Index)} {pattern.Reason}: {match.Value}"));
            })
            .ToArray();

        matches.Should().BeEmpty("Unity Mono used by some games does not expose these netstandard2.1 APIs");
    }

    private static string FindRepositoryDirectory(params string[] segments)
    {
        var path = Path.Combine(new[] { FindRepositoryRoot() }.Concat(segments).ToArray());
        Directory.Exists(path).Should().BeTrue("repository directory should exist: {0}", path);
        return path;
    }

    private static string FindRepositoryRoot()
    {
        var directory = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(directory))
        {
            if (Directory.Exists(Path.Combine(directory, ".git")))
            {
                return directory;
            }

            directory = Directory.GetParent(directory)?.FullName ?? string.Empty;
        }

        throw new InvalidOperationException("Repository root was not found.");
    }

    private static int LineNumber(string source, int index)
    {
        var line = 1;
        for (var i = 0; i < index; i++)
        {
            if (source[i] == '\n')
            {
                line++;
            }
        }

        return line;
    }

    [GeneratedRegex(@"\.Trim(?:Start|End)\s*\(\s*\)", RegexOptions.CultureInvariant)]
    private static partial Regex ParameterlessTrimStartEndPattern();

    [GeneratedRegex(@"\.(?:Contains|StartsWith|EndsWith|Split|Trim|TrimStart|TrimEnd)\s*\(\s*'[^']*'(?:\s*,[^)]*)?\)", RegexOptions.CultureInvariant)]
    private static partial Regex CharStringOverloadPattern();

    [GeneratedRegex(@"\.(?:Replace|Contains)\s*\([^;\r\n]*StringComparison\.", RegexOptions.CultureInvariant)]
    private static partial Regex StringComparisonReplaceContainsPattern();

    [GeneratedRegex(@"\b(?:Math\.Clamp|Path\.GetRelativePath|Random\.Shared|Task\.WaitAsync|Convert\.ToHexString)\b", RegexOptions.CultureInvariant)]
    private static partial Regex KnownMissingBclHelperPattern();

    [GeneratedRegex(@"\.Matches\s*\([^;]*?\)\s*\.\s*(?:Select|Cast\s*<\s*Match\s*>|OfType\s*<\s*Match\s*>)\s*\(", RegexOptions.CultureInvariant)]
    private static partial Regex RegexMatchCollectionLinqPattern();
}

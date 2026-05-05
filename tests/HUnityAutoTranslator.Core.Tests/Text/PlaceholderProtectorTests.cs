using FluentAssertions;
using HUnityAutoTranslator.Core.Text;

namespace HUnityAutoTranslator.Core.Tests.Text;

public sealed class PlaceholderProtectorTests
{
    [Fact]
    public void Extract_placeholders_uses_runtime_safe_manual_match_copy()
    {
        PlaceholderProtector.ExtractPlaceholders("Line\\n{playerName} %04d")
            .Should().Equal("\\n", "{playerName}", "%04d");

        var source = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.Core", "Text", "PlaceholderProtector.cs"));
        source.Should().NotContain(".Select(match => match.Value).ToArray()");
        source.Should().Contain("for (var i = 0; i < matches.Count; i++)");
    }

    private static string FindRepositoryFile(params string[] relativeSegments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(new[] { directory.FullName }.Concat(relativeSegments).ToArray());
            if (File.Exists(candidate) || Directory.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate " + Path.Combine(relativeSegments));
    }
}

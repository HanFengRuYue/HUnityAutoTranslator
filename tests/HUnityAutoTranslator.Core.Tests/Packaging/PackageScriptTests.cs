using FluentAssertions;

namespace HUnityAutoTranslator.Core.Tests.Packaging;

public sealed class PackageScriptTests
{
    [Fact]
    public void Package_script_writes_outputs_next_to_the_script()
    {
        var script = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "build", "package.ps1"));

        script.Should().Contain("$outputRoot = Resolve-Path -LiteralPath $PSScriptRoot");
        script.Should().Contain("$packageRoot = Join-Path $outputRoot \"HUnityAutoTranslator\"");
        script.Should().Contain("$zipPath = Join-Path $outputRoot \"HUnityAutoTranslator-0.1.0.zip\"");
        script.Should().NotContain("Join-Path $root \"artifacts\"");
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "HUnityAutoTranslator.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate HUnityAutoTranslator.sln from test output directory.");
    }
}

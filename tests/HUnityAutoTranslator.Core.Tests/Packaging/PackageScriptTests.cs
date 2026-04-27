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

    [Fact]
    public void Package_script_owns_control_panel_generation()
    {
        var root = FindRepositoryRoot();
        var script = File.ReadAllText(Path.Combine(root, "build", "package.ps1"));

        script.Should().Contain("[switch]$GeneratePanelOnly");
        script.Should().Contain("function Write-ControlPanelHtml");
        script.Should().Contain("if ($GeneratePanelOnly)");
        script.Should().NotContain("generate-control-panel.ps1");
        File.Exists(Path.Combine(root, "build", "generate-control-panel.ps1")).Should().BeFalse();
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

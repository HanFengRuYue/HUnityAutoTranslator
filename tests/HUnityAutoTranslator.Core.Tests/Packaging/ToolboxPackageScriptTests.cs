using FluentAssertions;

namespace HUnityAutoTranslator.Core.Tests.Packaging;

public sealed class ToolboxPackageScriptTests
{
    [Fact]
    public void Solution_includes_toolbox_projects()
    {
        var root = FindRepositoryRoot();
        var solution = File.ReadAllText(Path.Combine(root, "HUnityAutoTranslator.sln"));

        solution.Should().Contain("HUnityAutoTranslator.Toolbox.Core");
        solution.Should().Contain("HUnityAutoTranslator.Toolbox");
    }

    [Fact]
    public void Toolbox_package_script_builds_single_exe_and_embeds_vue_html()
    {
        var root = FindRepositoryRoot();
        var script = File.ReadAllText(Path.Combine(root, "build", "package-toolbox.ps1"));

        script.Should().Contain("PublishSingleFile=true");
        script.Should().Contain("--self-contained");
        script.Should().Contain("-r");
        script.Should().Contain("win-x64");
        script.Should().Contain("Write-ToolboxHtml");
        script.Should().Contain("src\\HUnityAutoTranslator.Toolbox.Ui");
        script.Should().Contain("HUnityAutoTranslator.Toolbox.exe");
    }

    [Fact]
    public void Toolbox_uses_existing_project_branding_assets()
    {
        var root = FindRepositoryRoot();
        var project = File.ReadAllText(Path.Combine(root, "src", "HUnityAutoTranslator.Toolbox", "HUnityAutoTranslator.Toolbox.csproj"));
        var ui = File.ReadAllText(Path.Combine(root, "src", "HUnityAutoTranslator.Toolbox.Ui", "src", "App.vue"));

        project.Should().Contain("hunity-icon-blue-white.ico");
        project.Should().Contain("HUnityAutoTranslator.ControlPanel");
        ui.Should().Contain("hunity-icon-blue-white-128.png");
        ui.Should().NotContain("<div class=\"logo\">H</div>");
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

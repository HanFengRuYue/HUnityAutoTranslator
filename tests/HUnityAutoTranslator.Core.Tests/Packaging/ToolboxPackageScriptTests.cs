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
    public void Toolbox_package_script_removes_publish_directory_after_copying_single_exe()
    {
        var root = FindRepositoryRoot();
        var script = File.ReadAllText(Path.Combine(root, "build", "package-toolbox.ps1"));

        const string copySingleExe = "Copy-Item -LiteralPath $publishedExe -Destination $singleExePath -Force";
        const string cleanupPublishDirectory = "Remove-BuildSubdirectory -Path $publishRoot";

        var copyIndex = script.IndexOf(copySingleExe, StringComparison.Ordinal);
        var cleanupIndex = script.IndexOf(cleanupPublishDirectory, copyIndex + copySingleExe.Length, StringComparison.Ordinal);

        copyIndex.Should().BeGreaterThanOrEqualTo(0);
        cleanupIndex.Should().BeGreaterThan(copyIndex);
    }

    [Fact]
    public void Toolbox_package_script_removes_stale_webview_cache_next_to_single_exe()
    {
        var root = FindRepositoryRoot();
        var script = File.ReadAllText(Path.Combine(root, "build", "package-toolbox.ps1"));

        script.Should().Contain("$singleExeCachePath = \"$singleExePath.WebView2\"");
        script.Should().Contain("Remove-BuildSubdirectory -Path $singleExeCachePath");
    }

    [Fact]
    public void Toolbox_package_script_moves_inlined_vue_script_after_app_root()
    {
        var root = FindRepositoryRoot();
        var script = File.ReadAllText(Path.Combine(root, "build", "package-toolbox.ps1"));

        script.Should().Contain("$scriptBlocks");
        script.Should().Contain("</body>");
        script.Should().Contain("<script>`n$script`n</script>");
    }

    [Fact]
    public void Generated_toolbox_html_uses_webview_compatible_script_position()
    {
        var root = FindRepositoryRoot();
        var generated = File.ReadAllText(Path.Combine(root, "src", "HUnityAutoTranslator.Toolbox", "Web", "ToolboxHtml.cs"));

        generated.Should().NotContain("<script type=\"module\"");

        var appRootIndex = generated.IndexOf("<div id=\"app\"></div>", StringComparison.Ordinal);
        var scriptIndex = generated.IndexOf("<script>", StringComparison.Ordinal);
        var bodyEndIndex = generated.IndexOf("</body>", StringComparison.Ordinal);

        appRootIndex.Should().BeGreaterThanOrEqualTo(0);
        scriptIndex.Should().BeGreaterThan(appRootIndex);
        scriptIndex.Should().BeLessThan(bodyEndIndex);
    }

    [Fact]
    public void Generated_toolbox_html_has_no_trailing_whitespace()
    {
        var root = FindRepositoryRoot();
        var generated = File.ReadAllText(Path.Combine(root, "src", "HUnityAutoTranslator.Toolbox", "Web", "ToolboxHtml.cs"));

        generated.Split('\n')
            .Should()
            .OnlyContain(line => line.TrimEnd('\r', ' ', '\t').Length == line.TrimEnd('\r').Length);
    }

    [Fact]
    public void Toolbox_theme_storage_is_guarded_for_webview_string_documents()
    {
        var root = FindRepositoryRoot();
        var ui = File.ReadAllText(Path.Combine(root, "src", "HUnityAutoTranslator.Toolbox.Ui", "src", "App.vue"));

        ui.Should().Contain("function readStoredTheme");
        ui.Should().Contain("function writeStoredTheme");
        ui.Should().Contain("const saved = window.localStorage.getItem(themeStorageKey);");
        ui.Should().Contain("window.localStorage.setItem(themeStorageKey, value);");
        ui.Should().Contain("catch");
        ui.Should().NotContain("const saved = localStorage.getItem(themeStorageKey);");
        ui.Should().NotContain("localStorage.setItem(themeStorageKey, theme.value);");
    }

    [Fact]
    public void Toolbox_webview_uses_windows_local_app_data_for_runtime_cache()
    {
        var root = FindRepositoryRoot();
        var windowCode = File.ReadAllText(Path.Combine(root, "src", "HUnityAutoTranslator.Toolbox", "MainWindow.xaml.cs"));

        windowCode.Should().Contain("CoreWebView2Environment.CreateAsync(null, userDataFolder)");
        windowCode.Should().Contain("WebView.EnsureCoreWebView2Async(environment)");
        windowCode.Should().Contain("Environment.SpecialFolder.LocalApplicationData");
        windowCode.Should().Contain("\"HUnityAutoTranslator\", \"Toolbox\", \"WebView2\"");
        windowCode.Should().NotContain("WebView.EnsureCoreWebView2Async().ConfigureAwait");
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

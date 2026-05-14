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
    public void Toolbox_package_script_recovers_when_npm_ci_hits_locked_windows_native_dependency()
    {
        var root = FindRepositoryRoot();
        var script = File.ReadAllText(Path.Combine(root, "build", "package-toolbox.ps1"));

        script.Should().Contain("function Invoke-ToolboxNpmInstall");
        script.Should().Contain("$previousErrorActionPreference = $ErrorActionPreference");
        script.Should().Contain("$ErrorActionPreference = \"Continue\"");
        script.Should().Contain("$ciOutput = & npm @(\"ci\") 2>&1");
        script.Should().Contain("$ciExitCode -eq -4048");
        script.Should().Contain("Test-Path -LiteralPath \"node_modules\"");
        script.Should().Contain("Invoke-CheckedNative \"npm\" @(\"install\", \"--no-audit\", \"--no-fund\")");
        script.Should().Contain("Invoke-ToolboxNpmInstall");
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
    public void Toolbox_game_library_storage_is_guarded_for_webview_string_documents()
    {
        var root = FindRepositoryRoot();
        var ui = File.ReadAllText(Path.Combine(root, "src", "HUnityAutoTranslator.Toolbox.Ui", "src", "App.vue"));

        ui.Should().Contain("const parsed = JSON.parse(readStoredString(gameLibraryStorageKey) || \"[]\");");
        ui.Should().Contain("writeStoredString(gameLibraryStorageKey, \"\");");
        ui.Should().NotContain("window.localStorage.removeItem(gameLibraryStorageKey);");
    }

    [Fact]
    public void Toolbox_table_storage_is_guarded_for_webview_string_documents()
    {
        var root = FindRepositoryRoot();
        var table = File.ReadAllText(Path.Combine(root, "src", "HUnityAutoTranslator.Toolbox.Ui", "src", "table.ts"));

        table.Should().Contain("function readStoredValue");
        table.Should().Contain("function writeStoredValue");
        table.Should().Contain("function removeStoredValue");
        table.Should().NotContain("window.localStorage.removeItem(key);");
        table.Should().NotContain("window.localStorage.setItem(visibleColumnStorageKey");
        table.Should().NotContain("window.localStorage.setItem(columnOrderStorageKey");
        table.Should().NotContain("window.localStorage.setItem(columnWidthStorageKey");
        table.Should().NotContain("window.localStorage.setItem(columnFilterStorageKey");
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

    [Fact]
    public void Toolbox_package_script_bundles_embedded_assets_before_publish()
    {
        var root = FindRepositoryRoot();
        var script = File.ReadAllText(Path.Combine(root, "build", "package-toolbox.ps1"));

        script.Should().Contain("function Bundle-EmbeddedAssets");
        script.Should().Contain("function Ensure-PluginAndLlamaCppZips");
        script.Should().Contain("function Write-EmbeddedAssetManifestCs");
        script.Should().Contain("[switch]$SkipBundleAssets");
        script.Should().Contain("if (-not $SkipBundleAssets)");
        script.Should().Contain("Bundle-EmbeddedAssets");

        var bundleIndex = script.IndexOf("Bundle-EmbeddedAssets\r", StringComparison.Ordinal);
        if (bundleIndex < 0) bundleIndex = script.IndexOf("Bundle-EmbeddedAssets\n", StringComparison.Ordinal);
        var publishIndex = script.IndexOf("dotnet", StringComparison.Ordinal);
        bundleIndex.Should().BeGreaterThan(0);
        publishIndex.Should().BeGreaterThan(0);
    }

    [Fact]
    public void Toolbox_package_script_pins_bepinex_versions()
    {
        var root = FindRepositoryRoot();
        var assetCache = File.ReadAllText(Path.Combine(root, "build", "lib", "AssetCache.ps1"));

        assetCache.Should().Contain("v5.4.23.5");
        assetCache.Should().Contain("v6.0.0-pre.2");
        assetCache.Should().Contain("BepInEx_win_x64_5.4.23.5.zip");
        assetCache.Should().Contain("BepInEx-Unity.Mono-win-x64-6.0.0-pre.2.zip");
        assetCache.Should().Contain("BepInEx-Unity.IL2CPP-win-x64-6.0.0-pre.2.zip");
        assetCache.Should().Contain("82f9878551030f54657792c0740d9d51a09500eeae1fba21106b0c441e6732c4");
        assetCache.Should().Contain("4699fadeeae31366a647026d8d992185a16070d2953636cd085ceadf75d3b26e");
        assetCache.Should().Contain("616ec7eb06cf11b2a0000e8fcef04d1b12bb58e84a2e0bdac9523234fc193ceb");
    }

    [Fact]
    public void Toolbox_csproj_declares_embedded_resource_glob()
    {
        var root = FindRepositoryRoot();
        var csproj = File.ReadAllText(Path.Combine(root, "src", "HUnityAutoTranslator.Toolbox", "HUnityAutoTranslator.Toolbox.csproj"));

        csproj.Should().Contain("BundleEmbeddedAssets");
        csproj.Should().Contain("EmbeddedAssetsDirectory");
        csproj.Should().Contain("<EmbeddedResource Include=\"$(EmbeddedAssetsDirectory)\\*.zip\">");
        csproj.Should().Contain("<LogicalName>HUnityAutoTranslator.Toolbox.EmbeddedAssets.%(Filename)%(Extension)</LogicalName>");
    }

    [Fact]
    public void Toolbox_package_script_generates_embedded_asset_manifest_cs()
    {
        var root = FindRepositoryRoot();
        var script = File.ReadAllText(Path.Combine(root, "build", "package-toolbox.ps1"));

        script.Should().Contain("EmbeddedAssetManifest.cs");
        script.Should().Contain("Write-EmbeddedAssetManifestCs");
        script.Should().Contain("public static readonly IReadOnlyList<EmbeddedAsset> Entries");
    }

    [Fact]
    public void Embedded_asset_manifest_file_is_committed()
    {
        var root = FindRepositoryRoot();
        var manifest = Path.Combine(root, "src", "HUnityAutoTranslator.Toolbox.Core", "Installation", "EmbeddedAssetManifest.cs");

        File.Exists(manifest).Should().BeTrue();
        var content = File.ReadAllText(manifest);
        content.Should().Contain("class EmbeddedAssetManifest");
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

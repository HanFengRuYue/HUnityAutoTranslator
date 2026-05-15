using System.Diagnostics;
using FluentAssertions;

namespace HUnityAutoTranslator.Core.Tests.Packaging;

public sealed class PackageScriptTests
{
    [Fact]
    public void Package_script_dot_sources_shared_asset_cache_lib()
    {
        var root = FindRepositoryRoot();
        var script = File.ReadAllText(Path.Combine(root, "build", "package.ps1"));
        var assetCache = Path.Combine(root, "build", "lib", "AssetCache.ps1");

        File.Exists(assetCache).Should().BeTrue();
        script.Should().Contain(". (Join-Path $PSScriptRoot \"lib\\AssetCache.ps1\")");
        // Backward compat: existing tests assert on Invoke-CheckedNative and Get-CheckedAsset wrapper.
        script.Should().Contain("function Get-CheckedAsset");
    }

    [Fact]
    public void Package_script_writes_outputs_next_to_the_script()
    {
        var script = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "build", "package.ps1"));

        script.Should().Contain("[string]$PackageVersion = \"0.2.0\"");
        script.Should().Contain("$outputRoot = Resolve-Path -LiteralPath $PSScriptRoot");
        script.Should().Contain("$packageRoot = Join-Path $outputRoot \"HUnityAutoTranslator\"");
        script.Should().Contain("$bepInEx5PackageRoot = Join-Path $outputRoot \"HUnityAutoTranslator-bepinex5\"");
        script.Should().Contain("$bepInEx5Project = Join-Path $root \"src\\HUnityAutoTranslator.Plugin.BepInEx5\\HUnityAutoTranslator.Plugin.BepInEx5.csproj\"");
        script.Should().Contain("$zipPath = Join-Path $outputRoot \"HUnityAutoTranslator-$PackageVersion.zip\"");
        script.Should().Contain("$bepInEx5ZipPath = Join-Path $outputRoot \"HUnityAutoTranslator-$PackageVersion-bepinex5.zip\"");
        script.Should().Contain("$il2CppZipPath = Join-Path $outputRoot \"HUnityAutoTranslator-$PackageVersion-il2cpp.zip\"");
        script.Should().NotContain("Join-Path $root \"artifacts\"");
    }

    [Fact]
    public void Package_script_builds_separate_mono_and_il2cpp_plugin_packages()
    {
        var script = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "build", "package.ps1"));

        script.Should().Contain("[ValidateSet(\"BepInEx5\", \"Mono\", \"IL2CPP\", \"All\")]");
        script.Should().Contain("[string]$Runtime = \"All\"");
        script.Should().Contain("function Build-PluginPackage");
        script.Should().Contain("Get-PluginRuntimeBuilds -Runtime $Runtime");
        script.Should().Contain("Project = $bepInEx5Project");
        // BepInEx5 必须打成 net462 才能在 Unity 2019.4 LTS 上加载，Mono(BepInEx 6) 仍是 netstandard2.1，IL2CPP 是 net6.0。
        script.Should().Contain("TargetFramework = \"net462\"");
        script.Should().Contain("TargetFramework = \"netstandard2.1\"");
        script.Should().Contain("TargetFramework = \"net6.0\"");
        script.Should().Contain("HUnityAutoTranslator.Plugin.BepInEx5.dll");
        script.Should().Contain("HUnityAutoTranslator.Plugin.IL2CPP.dll");
        script.Should().Contain("HUnityAutoTranslator-$PackageVersion-bepinex5.zip");
        script.Should().Contain("HUnityAutoTranslator-$PackageVersion-il2cpp.zip");
        script.Should().Contain("$runtimeProject = $Build.Project");
        script.Should().Contain("$runtimeProjectDirectory = Split-Path -Parent $runtimeProject");
        script.Should().Contain("$_.Name -ne \"BepInEx.dll\"");
    }

    [Fact]
    public void Package_script_passes_release_version_to_msbuild()
    {
        var script = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "build", "package.ps1"));

        script.Should().Contain("\"-p:Version=$PackageVersion\"");
        script.Should().Contain("\"-p:PackageVersion=$PackageVersion\"");
        script.Should().Contain("\"-p:BepInExPluginVersion=$PackageVersion\"");
        script.Should().Contain("\"-p:FileVersion=$PackageVersion\"");
        script.Should().Contain("\"-p:InformationalVersion=$PackageVersion\"");
    }

    [Fact]
    public void Package_script_owns_control_panel_generation()
    {
        var root = FindRepositoryRoot();
        var script = File.ReadAllText(Path.Combine(root, "build", "package.ps1"));

        script.Should().Contain("[switch]$GeneratePanelOnly");
        script.Should().Contain("[switch]$SkipNpmInstall");
        script.Should().Contain("function Write-ControlPanelHtml");
        script.Should().Contain("$controlPanelBuildRoot = Join-Path $outputRoot \".control-panel-build\"");
        script.Should().Contain("function Copy-ControlPanelSource");
        script.Should().Contain("node_modules");
        script.Should().Contain("if ($GeneratePanelOnly)");
        script.Should().NotContain("generate-control-panel.ps1");
        File.Exists(Path.Combine(root, "build", "generate-control-panel.ps1")).Should().BeFalse();
    }

    [Fact]
    public void Package_script_checks_native_command_exit_codes()
    {
        var script = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "build", "package.ps1"));

        script.Should().Contain("function Invoke-CheckedNative");
        script.Should().Contain("if ($LASTEXITCODE -ne 0)");
        script.Should().Contain("Invoke-CheckedNative \"npm\" @(\"ci\")");
        script.Should().Contain("Invoke-CheckedNative \"npm\" @(\"run\", \"build\")");
        script.Should().Contain("Invoke-CheckedNative \"dotnet\" @(");
        script.Should().Contain("\"build\",");
        script.Should().Contain("if (-not $SkipNpmInstall)");
    }

    [Fact]
    public void Package_script_embeds_favicon_without_copying_branding_assets()
    {
        var script = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "build", "package.ps1"));

        script.Should().Contain("Convert-LocalIconLinksToDataUris");
        script.Should().Contain("data:image/x-icon;base64,");
        script.Should().NotContain("$brandingSource = Join-Path $controlPanelRoot \"public\\branding\"");
        script.Should().NotContain("function Copy-BrandingAssets");
        script.Should().NotContain("assets\\branding");
        script.Should().NotContain("Copy-BrandingAssets -TargetRoot");
    }

    [Fact]
    public void Package_script_can_build_fixed_separate_llamacpp_cuda_and_vulkan_packages()
    {
        var script = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "build", "package.ps1"));

        script.Should().Contain("[ValidateSet(\"None\", \"Cuda13\", \"Vulkan\", \"All\")]");
        script.Should().Contain("[string]$LlamaCppVariant = \"All\"");
        script.Should().Contain("$LlamaCppReleaseTag = \"b9139\"");
        script.Should().Contain("llama-b9139-bin-win-cuda-13.1-x64.zip");
        script.Should().Contain("cudart-llama-bin-win-cuda-13.1-x64.zip");
        script.Should().Contain("llama-b9139-bin-win-vulkan-x64.zip");
        script.Should().Contain("7b0a01faa98b4384d34555983c9c6f9ea0bdc05c1b83882e9057c819c86ba2cc");
        script.Should().Contain("f96935e7e385e3b2d0189239077c10fe8fd7e95690fea4afec455b1b6c7e3f18");
        script.Should().Contain("36100aa2a925b3f9452096f4b820aa43d57a35b37713fc300e15d96e589f3970");
        script.Should().Contain("HUnityAutoTranslator-$PackageVersion-llamacpp-cuda13.zip");
        script.Should().Contain("HUnityAutoTranslator-$PackageVersion-llamacpp-vulkan.zip");
        script.Should().Contain("function Build-LlamaCppPackage");
        script.Should().Contain("$llamaTargetRoot = Join-Path $llamaPackageRoot \"BepInEx\\plugins\\HUnityAutoTranslator\"");
        script.Should().Contain("Add-LlamaCppBackend -Variant $Variant -TargetRoot $llamaTargetRoot");
        script.Should().Contain("foreach ($variant in Get-LlamaCppPackageVariants -Variant $LlamaCppVariant)");
        script.Should().NotContain("HUnityAutoTranslator-0.1.1-cuda13.zip");
        script.Should().NotContain("HUnityAutoTranslator-0.1.1-vulkan.zip");
        script.Should().NotContain("Add-LlamaCppBackend -Variant $LlamaCppVariant -PluginRoot $pluginRoot");
        script.Should().Contain("if ($GeneratePanelOnly)");
    }

    [Fact]
    public void Plugin_only_package_script_delegates_without_llamacpp_packages()
    {
        var root = FindRepositoryRoot();
        var scriptPath = Path.Combine(root, "build", "package-plugin.ps1");

        File.Exists(scriptPath).Should().BeTrue();

        var script = File.ReadAllText(scriptPath);
        script.Should().Contain("$packageScript = Join-Path $PSScriptRoot \"package.ps1\"");
        script.Should().Contain("$parameters = @{");
        script.Should().Contain("Configuration = $Configuration");
        script.Should().Contain("PackageVersion = $PackageVersion");
        script.Should().Contain("Runtime = $Runtime");
        script.Should().Contain("LlamaCppVariant = \"None\"");
        script.Should().Contain("& $packageScript @parameters");
        script.Should().NotContain("$arguments = @(");
        script.Should().NotContain("Build-LlamaCppPackage");
        script.Should().NotContain("Cuda13");
        script.Should().NotContain("Vulkan");
    }

    [Fact]
    public void Plugin_only_package_script_passes_named_parameters_to_package_script()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var root = FindRepositoryRoot();
        var tempRoot = Path.Combine(Path.GetTempPath(), "HUnityAutoTranslatorPackageScriptTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        try
        {
            File.Copy(
                Path.Combine(root, "build", "package-plugin.ps1"),
                Path.Combine(tempRoot, "package-plugin.ps1"));

            File.WriteAllText(
                Path.Combine(tempRoot, "package.ps1"),
                """
                param(
                    [string]$Configuration = "Release",
                    [string]$PackageVersion = "0.1.1",
                    [ValidateSet("BepInEx5", "Mono", "IL2CPP", "All")]
                    [string]$Runtime = "All",
                    [ValidateSet("None", "Cuda13", "Vulkan", "All")]
                    [string]$LlamaCppVariant = "All",
                    [switch]$SkipNpmInstall
                )

                Write-Output "$Configuration|$PackageVersion|$Runtime|$LlamaCppVariant|$($SkipNpmInstall.IsPresent)"
                """);

            var result = RunPowerShell(
                tempRoot,
                "-NoProfile",
                "-ExecutionPolicy", "Bypass",
                "-File", "package-plugin.ps1",
                "-PackageVersion", "1.2.3",
                "-Runtime", "Mono",
                "-SkipNpmInstall");

            result.ExitCode.Should().Be(0, result.StandardError);
            result.StandardOutput.Trim().Should().Be("Release|1.2.3|Mono|None|True");
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public void Release_workflow_publishes_semver_tagged_packages()
    {
        var root = FindRepositoryRoot();
        var workflowPath = Path.Combine(root, ".github", "workflows", "release.yml");

        File.Exists(workflowPath).Should().BeTrue();
        var workflow = File.ReadAllText(workflowPath);

        workflow.Should().Contain("tags:");
        workflow.Should().Contain("- 'v*.*.*'");
        workflow.Should().Contain(@"^v\d+\.\d+\.\d+$");
        workflow.Should().Contain("contents: write");
        workflow.Should().Contain("runs-on: windows-latest");
        workflow.Should().Contain("uses: actions/checkout@v6");
        workflow.Should().Contain("uses: actions/setup-dotnet@v5");
        workflow.Should().Contain("global-json-file: global.json");
        workflow.Should().Contain("uses: actions/setup-node@v6");
        workflow.Should().Contain("node-version: 24.x");
        workflow.Should().Contain("cache-dependency-path: src/HUnityAutoTranslator.ControlPanel/package-lock.json");
        workflow.Should().Contain("dotnet test");
        workflow.Should().Contain(".\\build\\package.ps1 -PackageVersion $env:PACKAGE_VERSION -Runtime All -LlamaCppVariant All");
        workflow.Should().Contain("gh release create");
        workflow.Should().Contain("$LASTEXITCODE -eq 0");
        workflow.Should().Contain("--verify-tag");
        workflow.Should().Contain("--generate-notes");
        workflow.Should().Contain("gh release upload");
        workflow.Should().Contain("--clobber");
        workflow.Should().Contain("HUnityAutoTranslator-$env:PACKAGE_VERSION-bepinex5.zip");
        workflow.Should().Contain("HUnityAutoTranslator-$env:PACKAGE_VERSION.zip");
        workflow.Should().Contain("HUnityAutoTranslator-$env:PACKAGE_VERSION-il2cpp.zip");
        workflow.Should().Contain("HUnityAutoTranslator-$env:PACKAGE_VERSION-llamacpp-cuda13.zip");
        workflow.Should().Contain("HUnityAutoTranslator-$env:PACKAGE_VERSION-llamacpp-vulkan.zip");
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

    private static ProcessResult RunPowerShell(string workingDirectory, params string[] arguments)
    {
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = "powershell",
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var argument in arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        process.Start();
        var standardOutput = process.StandardOutput.ReadToEnd();
        var standardError = process.StandardError.ReadToEnd();
        if (!process.WaitForExit(30_000))
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException("PowerShell package script test did not exit within 30 seconds.");
        }

        return new ProcessResult(process.ExitCode, standardOutput, standardError);
    }

    private sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError);
}

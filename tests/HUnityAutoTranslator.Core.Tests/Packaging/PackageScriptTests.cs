using System.Diagnostics;
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
        script.Should().Contain("$bepInEx5PackageRoot = Join-Path $outputRoot \"HUnityAutoTranslator-bepinex5\"");
        script.Should().Contain("$bepInEx5Project = Join-Path $root \"src\\HUnityAutoTranslator.Plugin.BepInEx5\\HUnityAutoTranslator.Plugin.BepInEx5.csproj\"");
        script.Should().Contain("$zipPath = Join-Path $outputRoot \"HUnityAutoTranslator-0.1.0.zip\"");
        script.Should().Contain("$bepInEx5ZipPath = Join-Path $outputRoot \"HUnityAutoTranslator-0.1.0-bepinex5.zip\"");
        script.Should().Contain("$il2CppZipPath = Join-Path $outputRoot \"HUnityAutoTranslator-0.1.0-il2cpp.zip\"");
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
        script.Should().Contain("TargetFramework = \"netstandard2.1\"");
        script.Should().Contain("TargetFramework = \"net6.0\"");
        script.Should().Contain("HUnityAutoTranslator.Plugin.BepInEx5.dll");
        script.Should().Contain("HUnityAutoTranslator.Plugin.IL2CPP.dll");
        script.Should().Contain("HUnityAutoTranslator-0.1.0-bepinex5.zip");
        script.Should().Contain("HUnityAutoTranslator-0.1.0-il2cpp.zip");
        script.Should().Contain("$runtimeProject = $Build.Project");
        script.Should().Contain("$runtimeProjectDirectory = Split-Path -Parent $runtimeProject");
        script.Should().Contain("$_.Name -ne \"BepInEx.dll\"");
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
        script.Should().Contain("Invoke-CheckedNative \"dotnet\" @(\"build\"");
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
        script.Should().Contain("$LlamaCppReleaseTag = \"b8943\"");
        script.Should().Contain("llama-b8943-bin-win-cuda-13.1-x64.zip");
        script.Should().Contain("cudart-llama-bin-win-cuda-13.1-x64.zip");
        script.Should().Contain("llama-b8943-bin-win-vulkan-x64.zip");
        script.Should().Contain("b4a53f4fe822320357bc45b14d46bde1beadf6cc912a148d33b09b78482f20d7");
        script.Should().Contain("f96935e7e385e3b2d0189239077c10fe8fd7e95690fea4afec455b1b6c7e3f18");
        script.Should().Contain("cb7bf6f828afd15885f5a0d9e279f6d6a988662e6ca2296308b31818a91d1534");
        script.Should().Contain("HUnityAutoTranslator-0.1.0-llamacpp-cuda13.zip");
        script.Should().Contain("HUnityAutoTranslator-0.1.0-llamacpp-vulkan.zip");
        script.Should().Contain("function Build-LlamaCppPackage");
        script.Should().Contain("$llamaTargetRoot = Join-Path $llamaPackageRoot \"BepInEx\\plugins\\HUnityAutoTranslator\"");
        script.Should().Contain("Add-LlamaCppBackend -Variant $Variant -TargetRoot $llamaTargetRoot");
        script.Should().Contain("foreach ($variant in Get-LlamaCppPackageVariants -Variant $LlamaCppVariant)");
        script.Should().NotContain("HUnityAutoTranslator-0.1.0-cuda13.zip");
        script.Should().NotContain("HUnityAutoTranslator-0.1.0-vulkan.zip");
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
                    [ValidateSet("BepInEx5", "Mono", "IL2CPP", "All")]
                    [string]$Runtime = "All",
                    [ValidateSet("None", "Cuda13", "Vulkan", "All")]
                    [string]$LlamaCppVariant = "All",
                    [switch]$SkipNpmInstall
                )

                Write-Output "$Configuration|$Runtime|$LlamaCppVariant|$($SkipNpmInstall.IsPresent)"
                """);

            var result = RunPowerShell(
                tempRoot,
                "-NoProfile",
                "-ExecutionPolicy", "Bypass",
                "-File", "package-plugin.ps1",
                "-Runtime", "Mono",
                "-SkipNpmInstall");

            result.ExitCode.Should().Be(0, result.StandardError);
            result.StandardOutput.Trim().Should().Be("Release|Mono|None|True");
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
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

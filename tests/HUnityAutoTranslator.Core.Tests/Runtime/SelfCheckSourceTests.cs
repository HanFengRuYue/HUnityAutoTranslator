using FluentAssertions;

namespace HUnityAutoTranslator.Core.Tests.Runtime;

public sealed class SelfCheckSourceTests
{
    [Fact]
    public void Plugin_runtime_starts_self_check_and_exposes_it_to_http_server()
    {
        var runtimeSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.Plugin", "PluginRuntime.cs"));
        var serverSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.Plugin", "Web", "LocalHttpServer.cs"));

        runtimeSource.Should().Contain("new SelfCheckService");
        runtimeSource.Should().Contain("StartAutomaticAsync");
        runtimeSource.Should().Contain("_selfCheck");
        serverSource.Should().Contain("SelfCheckService selfCheck");
        serverSource.Should().Contain("/api/self-check");
        serverSource.Should().Contain("/api/self-check/run");
    }

    [Fact]
    public void Self_check_service_keeps_external_api_and_llama_runtime_calls_out_of_probe_path()
    {
        var servicePath = FindRepositoryFile("src", "HUnityAutoTranslator.Plugin", "Diagnostics", "SelfCheckService.cs");
        File.Exists(servicePath).Should().BeTrue("the plugin should own the Unity/runtime diagnostics probe");

        var source = File.ReadAllText(servicePath);
        source.Should().Contain("按自检策略跳过外部 API 调用");
        source.Should().Contain("不启动 llama.cpp");
        source.Should().Contain("CountCurrentTextureTargetsDryRun");
        source.Should().Contain("texture-catalog");
        source.Should().NotContain("ProviderUtilityClient");
        source.Should().NotContain("FetchModelsAsync");
        source.Should().NotContain("FetchBalanceAsync");
        source.Should().NotContain("TranslateAsync");
        source.Should().NotContain("TextureImageEditClient");
        source.Should().NotContain("TextureVisionTextClient");
        source.Should().NotContain(".StartAsync(");
        source.Should().NotContain(".IsReadyAsync(");
        source.Should().NotContain("BenchmarkAsync");
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

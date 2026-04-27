using FluentAssertions;
using HUnityAutoTranslator.Core.Configuration;
using HUnityAutoTranslator.Core.Providers;

namespace HUnityAutoTranslator.Core.Tests.Providers;

public sealed class LlamaCppServerCommandBuilderTests
{
    [Fact]
    public void BuildArguments_uses_loopback_model_alias_and_runtime_limits()
    {
        var config = new LlamaCppConfig(
            ModelPath: @"D:\Models\game ui.gguf",
            ContextSize: 8192,
            GpuLayers: 80,
            ParallelSlots: 2);
        var profile = ProviderProfile.DefaultLlamaCpp() with { Model = "qwen-game-ui" };

        var arguments = LlamaCppServerCommandBuilder.BuildArguments(config, profile, port: 51234);

        arguments.Should().Contain("--host 127.0.0.1");
        arguments.Should().Contain("--port 51234");
        arguments.Should().Contain("-m \"D:\\Models\\game ui.gguf\"");
        arguments.Should().Contain("--alias qwen-game-ui");
        arguments.Should().Contain("-c 8192");
        arguments.Should().Contain("-ngl 80");
        arguments.Should().Contain("-np 2");
        arguments.Should().Contain("--no-webui");
    }
}

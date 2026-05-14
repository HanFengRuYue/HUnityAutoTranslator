using FluentAssertions;
using HUnityAutoTranslator.Core.Configuration;
using HUnityAutoTranslator.Core.Providers;

namespace HUnityAutoTranslator.Core.Tests.Providers;

public sealed class LlamaCppServerCommandBuilderTests
{
    [Fact]
    public void BuildArguments_preserves_per_slot_context_and_adds_performance_flags()
    {
        var config = new LlamaCppConfig(
            ModelPath: @"D:\Models\game ui.gguf",
            ContextSize: 8192,
            GpuLayers: 80,
            ParallelSlots: 2,
            BatchSize: 2048,
            UBatchSize: 512,
            FlashAttentionMode: "on");
        var profile = ProviderProfile.DefaultLlamaCpp() with { Model = "qwen-game-ui" };

        var arguments = LlamaCppServerCommandBuilder.BuildArguments(config, profile, port: 51234);

        arguments.Should().Contain("--host 127.0.0.1");
        arguments.Should().Contain("--port 51234");
        arguments.Should().Contain("-m \"D:\\Models\\game ui.gguf\"");
        arguments.Should().Contain("--alias local-model");
        arguments.Should().Contain("-c 16384");
        arguments.Should().Contain("-ngl 80");
        arguments.Should().Contain("-np 2");
        arguments.Should().Contain("-b 2048");
        arguments.Should().Contain("-ub 512");
        arguments.Should().Contain("-fa on");
        arguments.Should().Contain("--metrics");
        arguments.Should().Contain("--no-webui");
        arguments.Should().Contain("--cache-reuse 256");
    }

    [Fact]
    public void BuildArguments_omits_cache_reuse_when_disabled()
    {
        var config = new LlamaCppConfig(
            ModelPath: @"D:\Models\game.gguf",
            ContextSize: 4096,
            GpuLayers: 80,
            ParallelSlots: 1,
            CacheReuseTokens: 0);
        var profile = ProviderProfile.DefaultLlamaCpp();

        var arguments = LlamaCppServerCommandBuilder.BuildArguments(config, profile, port: 51234);

        arguments.Should().NotContain("--cache-reuse");
    }

    [Fact]
    public void BuildArguments_clamps_cache_reuse_above_max()
    {
        var config = new LlamaCppConfig(
            ModelPath: @"D:\Models\game.gguf",
            ContextSize: 4096,
            GpuLayers: 80,
            ParallelSlots: 1,
            CacheReuseTokens: 999999);
        var profile = ProviderProfile.DefaultLlamaCpp();

        var arguments = LlamaCppServerCommandBuilder.BuildArguments(config, profile, port: 51234);

        arguments.Should().Contain("--cache-reuse 8192");
    }
}

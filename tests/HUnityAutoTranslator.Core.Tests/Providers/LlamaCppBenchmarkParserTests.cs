using FluentAssertions;
using HUnityAutoTranslator.Core.Configuration;
using HUnityAutoTranslator.Core.Providers;

namespace HUnityAutoTranslator.Core.Tests.Providers;

public sealed class LlamaCppBenchmarkParserTests
{
    [Fact]
    public void ParseLlamaBenchJsonLines_combines_prompt_and_generation_rows()
    {
        var output = """
            {"model_filename":"qwen.gguf","n_batch":2048,"n_ubatch":512,"flash_attn":true,"n_prompt":256,"n_gen":0,"avg_ts":612.5}
            {"model_filename":"qwen.gguf","n_batch":2048,"n_ubatch":512,"flash_attn":true,"n_prompt":0,"n_gen":64,"avg_ts":28.75}
            """;

        var result = LlamaCppBenchmarkParser.ParseLlamaBenchJsonLines(output);

        result.Errors.Should().BeEmpty();
        result.Candidates.Should().ContainSingle();
        var candidate = result.Candidates[0];
        candidate.Tool.Should().Be("llama-bench");
        candidate.BatchSize.Should().Be(2048);
        candidate.UBatchSize.Should().Be(512);
        candidate.FlashAttentionMode.Should().Be("on");
        candidate.PromptTokensPerSecond.Should().Be(612.5);
        candidate.GenerationTokensPerSecond.Should().Be(28.75);
    }

    [Fact]
    public void ParseBatchedBenchJsonLines_reads_parallel_slots_and_total_context()
    {
        var output = """
            {"n_kv_max":4096,"n_batch":2048,"n_ubatch":512,"flash_attn":1,"pp":256,"tg":64,"pl":1,"t_pp":0.42,"speed_pp":610.0,"t_tg":2.7,"speed_tg":24.0,"t":3.12,"speed":102.0}
            {"n_kv_max":16384,"n_batch":2048,"n_ubatch":512,"flash_attn":1,"pp":256,"tg":64,"pl":4,"t_pp":0.62,"speed_pp":1650.0,"t_tg":3.1,"speed_tg":84.0,"t":3.72,"speed":326.0}
            """;

        var result = LlamaCppBenchmarkParser.ParseBatchedBenchJsonLines(output);

        result.Errors.Should().BeEmpty();
        result.Candidates.Should().HaveCount(2);
        result.Candidates[1].Tool.Should().Be("llama-batched-bench");
        result.Candidates[1].ParallelSlots.Should().Be(4);
        result.Candidates[1].TotalContextSize.Should().Be(16384);
        result.Candidates[1].GenerationTokensPerSecond.Should().Be(84.0);
        result.Candidates[1].TotalTokensPerSecond.Should().Be(326.0);
    }

    [Fact]
    public void Recommend_prefers_successful_smaller_config_with_clear_parallel_gain()
    {
        var current = new LlamaCppConfig(
            ModelPath: @"D:\Models\qwen.gguf",
            ContextSize: 4096,
            GpuLayers: 999,
            ParallelSlots: 1);
        var bench = LlamaCppBenchmarkParser.ParseLlamaBenchJsonLines("""
            {"n_batch":512,"n_ubatch":256,"flash_attn":false,"n_prompt":256,"n_gen":0,"avg_ts":590.0}
            {"n_batch":512,"n_ubatch":256,"flash_attn":false,"n_prompt":0,"n_gen":64,"avg_ts":24.0}
            {"n_batch":2048,"n_ubatch":512,"flash_attn":true,"n_prompt":256,"n_gen":0,"avg_ts":612.0}
            {"n_batch":2048,"n_ubatch":512,"flash_attn":true,"n_prompt":0,"n_gen":64,"avg_ts":29.0}
            """);
        var parallel = LlamaCppBenchmarkParser.ParseBatchedBenchJsonLines("""
            {"n_kv_max":4096,"n_batch":2048,"n_ubatch":512,"flash_attn":1,"pp":256,"tg":64,"pl":1,"speed_pp":610.0,"speed_tg":24.0,"t":3.0,"speed":101.0}
            {"n_kv_max":8192,"n_batch":2048,"n_ubatch":512,"flash_attn":1,"pp":256,"tg":64,"pl":2,"speed_pp":1110.0,"speed_tg":46.0,"t":3.2,"speed":198.0}
            {"n_kv_max":16384,"n_batch":2048,"n_ubatch":512,"flash_attn":1,"pp":256,"tg":64,"pl":4,"speed_pp":1650.0,"speed_tg":84.0,"t":3.6,"speed":326.0}
            """);
        var candidates = bench.Candidates.Concat(parallel.Candidates).ToArray();

        var recommended = LlamaCppBenchmarkAdvisor.Recommend(current, candidates);

        recommended.Should().NotBeNull();
        recommended!.ModelPath.Should().Be(current.ModelPath);
        recommended.ContextSize.Should().Be(4096);
        recommended.GpuLayers.Should().Be(999);
        recommended.BatchSize.Should().Be(2048);
        recommended.UBatchSize.Should().Be(512);
        recommended.FlashAttentionMode.Should().Be("on");
        recommended.ParallelSlots.Should().Be(4);
    }

    [Fact]
    public void Parser_reports_invalid_json_lines_as_errors()
    {
        var result = LlamaCppBenchmarkParser.ParseLlamaBenchJsonLines("{not json");

        result.Candidates.Should().BeEmpty();
        result.Errors.Should().ContainSingle(error => error.Contains("not json", StringComparison.Ordinal));
    }

    [Fact]
    public void Parser_ignores_normal_llama_cpp_diagnostics_between_json_rows()
    {
        var output = """
            ggml_cuda_init: found 1 CUDA devices (Total VRAM: 16379 MiB):
            llama_model_loader: loaded meta data with 46 key-value pairs
            load_tensors: loading model tensors, this can take a while...
            {"n_kv_max":16384,"n_batch":2048,"n_ubatch":256,"flash_attn":1,"pl":4,"speed_pp":2072.3,"speed_tg":73.6,"t":6.7,"speed":320.0}
            llama_perf_context_print: prompt eval time = 4036.10 ms / 1296 tokens
            """;

        var result = LlamaCppBenchmarkParser.ParseBatchedBenchJsonLines(output);

        result.Errors.Should().BeEmpty();
        result.Candidates.Should().ContainSingle();
        result.Candidates[0].ParallelSlots.Should().Be(4);
    }

    [Fact]
    public void Parser_reports_error_diagnostics()
    {
        var result = LlamaCppBenchmarkParser.ParseBatchedBenchJsonLines("CUDA error: out of memory");

        result.Candidates.Should().BeEmpty();
        result.Errors.Should().ContainSingle(error => error.Contains("out of memory", StringComparison.OrdinalIgnoreCase));
    }
}

using HUnityAutoTranslator.Core.Configuration;

namespace HUnityAutoTranslator.Core.Providers;

public sealed record LlamaCppBenchmarkCandidate(
    string Tool,
    int BatchSize,
    int UBatchSize,
    string FlashAttentionMode,
    int ParallelSlots,
    int TotalContextSize,
    double? PromptTokensPerSecond,
    double? GenerationTokensPerSecond,
    double? TotalTokensPerSecond,
    double? TotalSeconds,
    bool Succeeded,
    string? Error);

public sealed record LlamaCppBenchmarkParseResult(
    IReadOnlyList<LlamaCppBenchmarkCandidate> Candidates,
    IReadOnlyList<string> Errors);

public sealed record LlamaCppBenchmarkResult(
    bool Succeeded,
    bool Saved,
    string Message,
    LlamaCppConfig CurrentConfig,
    LlamaCppConfig? RecommendedConfig,
    IReadOnlyList<LlamaCppBenchmarkCandidate> Candidates,
    IReadOnlyList<string> Errors,
    string? LastOutput)
{
    public static LlamaCppBenchmarkResult Failure(
        LlamaCppConfig currentConfig,
        string message,
        string? lastOutput = null,
        IReadOnlyList<string>? errors = null)
    {
        return new LlamaCppBenchmarkResult(
            Succeeded: false,
            Saved: false,
            Message: message,
            CurrentConfig: currentConfig,
            RecommendedConfig: null,
            Candidates: Array.Empty<LlamaCppBenchmarkCandidate>(),
            Errors: errors ?? Array.Empty<string>(),
            LastOutput: lastOutput);
    }
}

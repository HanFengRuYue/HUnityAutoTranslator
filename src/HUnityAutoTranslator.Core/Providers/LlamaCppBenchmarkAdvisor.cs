using HUnityAutoTranslator.Core.Configuration;

namespace HUnityAutoTranslator.Core.Providers;

public static class LlamaCppBenchmarkAdvisor
{
    private const double MeaningfulParallelGain = 1.20;
    private const double NearBestThroughputRatio = 0.95;
    private const double MaxElapsedTimeMultiplier = 2.0;

    public static LlamaCppConfig? Recommend(
        LlamaCppConfig current,
        IReadOnlyList<LlamaCppBenchmarkCandidate> candidates)
    {
        var bestKernel = SelectBestKernelCandidate(candidates);
        if (bestKernel == null)
        {
            return null;
        }

        var recommended = current with
        {
            BatchSize = RuntimeConfigLimits.ClampLlamaCppBatchSize(bestKernel.BatchSize),
            UBatchSize = RuntimeConfigLimits.ClampLlamaCppUBatchSize(bestKernel.UBatchSize, bestKernel.BatchSize),
            FlashAttentionMode = RuntimeConfigLimits.NormalizeLlamaCppFlashAttentionMode(bestKernel.FlashAttentionMode)
        };

        var bestParallel = SelectBestParallelCandidate(current, candidates);
        if (bestParallel != null)
        {
            recommended = recommended with
            {
                ParallelSlots = RuntimeConfigLimits.ClampLlamaCppParallelSlots(bestParallel.ParallelSlots)
            };
        }

        return recommended with
        {
            UBatchSize = RuntimeConfigLimits.ClampLlamaCppUBatchSize(recommended.UBatchSize, recommended.BatchSize)
        };
    }

    private static LlamaCppBenchmarkCandidate? SelectBestKernelCandidate(IReadOnlyList<LlamaCppBenchmarkCandidate> candidates)
    {
        return candidates
            .Where(candidate =>
                candidate.Succeeded &&
                string.Equals(candidate.Tool, "llama-bench", StringComparison.Ordinal) &&
                candidate.GenerationTokensPerSecond.HasValue &&
                candidate.BatchSize > 0 &&
                candidate.UBatchSize > 0)
            .OrderByDescending(candidate => candidate.GenerationTokensPerSecond!.Value)
            .ThenByDescending(candidate => candidate.PromptTokensPerSecond ?? 0)
            .ThenBy(candidate => candidate.BatchSize)
            .ThenBy(candidate => candidate.UBatchSize)
            .FirstOrDefault();
    }

    private static LlamaCppBenchmarkCandidate? SelectBestParallelCandidate(
        LlamaCppConfig current,
        IReadOnlyList<LlamaCppBenchmarkCandidate> candidates)
    {
        var valid = candidates
            .Where(candidate =>
                candidate.Succeeded &&
                string.Equals(candidate.Tool, "llama-batched-bench", StringComparison.Ordinal) &&
                candidate.ParallelSlots > 0 &&
                candidate.TotalContextSize >= current.ContextSize * candidate.ParallelSlots &&
                ReadParallelThroughput(candidate).HasValue)
            .ToArray();
        if (valid.Length == 0)
        {
            return null;
        }

        var baseline = valid
            .Where(candidate => candidate.ParallelSlots == 1)
            .OrderBy(candidate => candidate.TotalSeconds ?? double.MaxValue)
            .FirstOrDefault()
            ?? valid.OrderBy(candidate => candidate.ParallelSlots).First();
        var baselineThroughput = ReadParallelThroughput(baseline);
        if (!baselineThroughput.HasValue || baselineThroughput.Value <= 0)
        {
            return null;
        }

        var maxAllowedSeconds = (baseline.TotalSeconds ?? 0) > 0
            ? baseline.TotalSeconds!.Value * MaxElapsedTimeMultiplier
            : double.MaxValue;
        var eligible = valid
            .Where(candidate =>
            {
                var throughput = ReadParallelThroughput(candidate) ?? 0;
                var elapsed = candidate.TotalSeconds ?? 0;
                return throughput >= baselineThroughput.Value * MeaningfulParallelGain &&
                    (elapsed <= 0 || elapsed <= maxAllowedSeconds);
            })
            .ToArray();
        if (eligible.Length == 0)
        {
            return baseline.ParallelSlots == current.ParallelSlots ? null : baseline;
        }

        var bestThroughput = eligible.Max(candidate => ReadParallelThroughput(candidate) ?? 0);
        return eligible
            .Where(candidate => (ReadParallelThroughput(candidate) ?? 0) >= bestThroughput * NearBestThroughputRatio)
            .OrderBy(candidate => candidate.ParallelSlots)
            .ThenBy(candidate => candidate.TotalSeconds ?? double.MaxValue)
            .FirstOrDefault();
    }

    private static double? ReadParallelThroughput(LlamaCppBenchmarkCandidate candidate)
    {
        return candidate.TotalTokensPerSecond ?? candidate.GenerationTokensPerSecond;
    }
}

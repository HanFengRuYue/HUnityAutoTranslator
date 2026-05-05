using System.Globalization;
using HUnityAutoTranslator.Core.Configuration;
using Newtonsoft.Json.Linq;

namespace HUnityAutoTranslator.Core.Providers;

public static class LlamaCppBenchmarkParser
{
    public static LlamaCppBenchmarkParseResult ParseLlamaBenchJsonLines(string output)
    {
        var rows = new Dictionary<string, MutableBenchCandidate>(StringComparer.Ordinal);
        var errors = new List<string>();
        foreach (var line in EnumerateLines(output))
        {
            if (!TryParseObject(line, errors, out var json))
            {
                continue;
            }

            var batch = ReadInt(json, "n_batch") ?? 0;
            var ubatch = ReadInt(json, "n_ubatch") ?? 0;
            var flash = ReadFlashMode(json, "flash_attn");
            var key = string.Join("|", batch.ToString(CultureInfo.InvariantCulture), ubatch.ToString(CultureInfo.InvariantCulture), flash);
            if (!rows.TryGetValue(key, out var candidate))
            {
                candidate = new MutableBenchCandidate(batch, ubatch, flash);
                rows[key] = candidate;
            }

            var avgTokensPerSecond = ReadDouble(json, "avg_ts");
            if ((ReadInt(json, "n_prompt") ?? 0) > 0)
            {
                candidate.PromptTokensPerSecond = avgTokensPerSecond;
            }

            if ((ReadInt(json, "n_gen") ?? 0) > 0)
            {
                candidate.GenerationTokensPerSecond = avgTokensPerSecond;
            }
        }

        return new LlamaCppBenchmarkParseResult(
            rows.Values
                .Where(candidate => candidate.PromptTokensPerSecond.HasValue || candidate.GenerationTokensPerSecond.HasValue)
                .Select(candidate => new LlamaCppBenchmarkCandidate(
                    Tool: "llama-bench",
                    BatchSize: candidate.BatchSize,
                    UBatchSize: candidate.UBatchSize,
                    FlashAttentionMode: candidate.FlashAttentionMode,
                    ParallelSlots: 1,
                    TotalContextSize: 0,
                    PromptTokensPerSecond: candidate.PromptTokensPerSecond,
                    GenerationTokensPerSecond: candidate.GenerationTokensPerSecond,
                    TotalTokensPerSecond: null,
                    TotalSeconds: null,
                    Succeeded: true,
                    Error: null))
                .ToArray(),
            errors.ToArray());
    }

    public static LlamaCppBenchmarkParseResult ParseBatchedBenchJsonLines(string output)
    {
        var candidates = new List<LlamaCppBenchmarkCandidate>();
        var errors = new List<string>();
        foreach (var line in EnumerateLines(output))
        {
            if (!TryParseObject(line, errors, out var json))
            {
                continue;
            }

            candidates.Add(new LlamaCppBenchmarkCandidate(
                Tool: "llama-batched-bench",
                BatchSize: ReadInt(json, "n_batch") ?? 0,
                UBatchSize: ReadInt(json, "n_ubatch") ?? 0,
                FlashAttentionMode: ReadFlashMode(json, "flash_attn"),
                ParallelSlots: Math.Max(1, ReadInt(json, "pl") ?? 1),
                TotalContextSize: ReadInt(json, "n_kv_max") ?? 0,
                PromptTokensPerSecond: ReadDouble(json, "speed_pp"),
                GenerationTokensPerSecond: ReadDouble(json, "speed_tg"),
                TotalTokensPerSecond: ReadDouble(json, "speed"),
                TotalSeconds: ReadDouble(json, "t"),
                Succeeded: true,
                Error: null));
        }

        return new LlamaCppBenchmarkParseResult(candidates.ToArray(), errors.ToArray());
    }

    private static IEnumerable<string> EnumerateLines(string output)
    {
        using var reader = new StringReader(output ?? string.Empty);
        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            line = line.Trim();
            if (line.Length > 0)
            {
                yield return line;
            }
        }
    }

    private static bool TryParseObject(string line, List<string> errors, out JObject json)
    {
        if (!line.StartsWith("{", StringComparison.Ordinal))
        {
            json = new JObject();
            if (LooksLikeErrorLine(line))
            {
                errors.Add("benchmark 输出错误: " + line);
            }

            return false;
        }

        try
        {
            json = JObject.Parse(line);
            return true;
        }
        catch
        {
            json = new JObject();
            errors.Add("无法解析 benchmark JSONL 行: " + line);
            return false;
        }
    }

    private static bool LooksLikeErrorLine(string line)
    {
        return line.IndexOf("error", StringComparison.OrdinalIgnoreCase) >= 0
            || line.IndexOf("failed", StringComparison.OrdinalIgnoreCase) >= 0
            || line.IndexOf("failure", StringComparison.OrdinalIgnoreCase) >= 0
            || line.IndexOf("exception", StringComparison.OrdinalIgnoreCase) >= 0
            || line.IndexOf("fatal", StringComparison.OrdinalIgnoreCase) >= 0
            || line.IndexOf("out of memory", StringComparison.OrdinalIgnoreCase) >= 0
            || line.IndexOf("oom", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static int? ReadInt(JObject json, string property)
    {
        var token = json[property];
        if (token == null)
        {
            return null;
        }

        if (token.Type == JTokenType.Integer)
        {
            return token.Value<int>();
        }

        return int.TryParse(token.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
    }

    private static double? ReadDouble(JObject json, string property)
    {
        var token = json[property];
        if (token == null)
        {
            return null;
        }

        if (token.Type == JTokenType.Float || token.Type == JTokenType.Integer)
        {
            return token.Value<double>();
        }

        return double.TryParse(token.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
    }

    private static string ReadFlashMode(JObject json, string property)
    {
        var token = json[property];
        if (token == null)
        {
            return "auto";
        }

        if (token.Type == JTokenType.Boolean)
        {
            return token.Value<bool>() ? "on" : "off";
        }

        var value = token.ToString();
        if (value == "1" || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase))
        {
            return "on";
        }

        if (value == "0" || string.Equals(value, "false", StringComparison.OrdinalIgnoreCase))
        {
            return "off";
        }

        return RuntimeConfigLimits.NormalizeLlamaCppFlashAttentionMode(value);
    }

    private sealed class MutableBenchCandidate
    {
        public MutableBenchCandidate(int batchSize, int ubatchSize, string flashAttentionMode)
        {
            BatchSize = batchSize;
            UBatchSize = ubatchSize;
            FlashAttentionMode = flashAttentionMode;
        }

        public int BatchSize { get; }

        public int UBatchSize { get; }

        public string FlashAttentionMode { get; }

        public double? PromptTokensPerSecond { get; set; }

        public double? GenerationTokensPerSecond { get; set; }
    }
}

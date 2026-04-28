using System.Globalization;
using HUnityAutoTranslator.Core.Configuration;

namespace HUnityAutoTranslator.Core.Providers;

public static class LlamaCppServerCommandBuilder
{
    public static string BuildArguments(LlamaCppConfig config, ProviderProfile profile, int port)
    {
        if (string.IsNullOrWhiteSpace(config.ModelPath))
        {
            throw new ArgumentException("llama.cpp model path is required.", nameof(config));
        }

        var totalContextSize = Math.Max(1, config.ContextSize) * Math.Max(1, config.ParallelSlots);
        return string.Join(" ", new[]
        {
            "--host",
            "127.0.0.1",
            "--port",
            port.ToString(CultureInfo.InvariantCulture),
            "-m",
            Quote(config.ModelPath),
            "--alias",
            "local-model",
            "-c",
            totalContextSize.ToString(CultureInfo.InvariantCulture),
            "-ngl",
            config.GpuLayers.ToString(CultureInfo.InvariantCulture),
            "-np",
            config.ParallelSlots.ToString(CultureInfo.InvariantCulture),
            "-b",
            config.BatchSize.ToString(CultureInfo.InvariantCulture),
            "-ub",
            config.UBatchSize.ToString(CultureInfo.InvariantCulture),
            "-fa",
            config.FlashAttentionMode,
            "--metrics",
            "--no-webui"
        });
    }

    private static string QuoteIfNeeded(string value)
    {
        return value.IndexOfAny(new[] { ' ', '\t', '"' }) >= 0 ? Quote(value) : value;
    }

    private static string Quote(string value)
    {
        return "\"" + value.Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
    }
}

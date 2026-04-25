using HUnityAutoTranslator.Core.Prompts;

namespace HUnityAutoTranslator.Core.Configuration;

public sealed record RuntimeConfig(
    bool Enabled,
    string TargetLanguage,
    TranslationStyle Style,
    ProviderProfile Provider,
    string HttpHost,
    int HttpPort,
    int MaxConcurrentRequests,
    int RequestsPerMinute,
    int MaxBatchCharacters,
    TimeSpan ScanInterval,
    int MaxScanTargetsPerTick,
    int MaxWritebacksPerFrame,
    bool EnableUgui,
    bool EnableTmp,
    bool EnableImgui)
{
    public static RuntimeConfig CreateDefault()
    {
        return new RuntimeConfig(
            Enabled: true,
            TargetLanguage: "zh-Hans",
            Style: TranslationStyle.Localized,
            Provider: ProviderProfile.DefaultOpenAi(),
            HttpHost: "127.0.0.1",
            HttpPort: 48110,
            MaxConcurrentRequests: 4,
            RequestsPerMinute: 60,
            MaxBatchCharacters: 1800,
            ScanInterval: TimeSpan.FromMilliseconds(750),
            MaxScanTargetsPerTick: 256,
            MaxWritebacksPerFrame: 32,
            EnableUgui: true,
            EnableTmp: true,
            EnableImgui: true);
    }
}

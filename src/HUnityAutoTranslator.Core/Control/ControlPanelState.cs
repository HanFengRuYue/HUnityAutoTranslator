using HUnityAutoTranslator.Core.Configuration;

namespace HUnityAutoTranslator.Core.Control;

public sealed record ControlPanelState(
    bool Enabled,
    string TargetLanguage,
    ProviderKind ProviderKind,
    string BaseUrl,
    string Endpoint,
    string Model,
    bool ApiKeyConfigured,
    string? ApiKeyPreview,
    int QueueCount,
    int CacheCount,
    int MaxConcurrentRequests,
    int RequestsPerMinute,
    int MaxBatchCharacters,
    int ScanIntervalMilliseconds,
    int MaxScanTargetsPerTick,
    int MaxWritebacksPerFrame,
    bool EnableUgui,
    bool EnableTmp,
    bool EnableImgui,
    string? LastError);

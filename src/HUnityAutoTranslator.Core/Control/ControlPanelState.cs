using HUnityAutoTranslator.Core.Configuration;

namespace HUnityAutoTranslator.Core.Control;

public sealed record ControlPanelState(
    bool Enabled,
    string TargetLanguage,
    ProviderKind ProviderKind,
    string Model,
    bool ApiKeyConfigured,
    string? ApiKeyPreview,
    int QueueCount,
    int CacheCount,
    int MaxConcurrentRequests,
    int RequestsPerMinute,
    string? LastError);

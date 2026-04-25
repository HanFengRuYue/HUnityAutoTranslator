using HUnityAutoTranslator.Core.Configuration;
using HUnityAutoTranslator.Core.Prompts;

namespace HUnityAutoTranslator.Core.Control;

public sealed record UpdateConfigRequest(
    string? TargetLanguage = null,
    int? MaxConcurrentRequests = null,
    int? RequestsPerMinute = null,
    bool? Enabled = null,
    ProviderKind? ProviderKind = null,
    string? BaseUrl = null,
    string? Endpoint = null,
    string? Model = null,
    TranslationStyle? Style = null,
    int? MaxBatchCharacters = null,
    int? ScanIntervalMilliseconds = null,
    int? MaxScanTargetsPerTick = null,
    int? MaxWritebacksPerFrame = null,
    bool? EnableUgui = null,
    bool? EnableTmp = null,
    bool? EnableImgui = null);

using HUnityAutoTranslator.Core.Configuration;
using HUnityAutoTranslator.Core.Prompts;

namespace HUnityAutoTranslator.Core.Control;

public sealed record UpdateConfigRequest(
    string? TargetLanguage = null,
    int? MaxConcurrentRequests = null,
    int? RequestsPerMinute = null,
    bool? Enabled = null,
    bool? AutoOpenControlPanel = null,
    ProviderKind? ProviderKind = null,
    string? BaseUrl = null,
    string? Endpoint = null,
    string? Model = null,
    TranslationStyle? Style = null,
    int? MaxBatchCharacters = null,
    int? ScanIntervalMilliseconds = null,
    int? MaxScanTargetsPerTick = null,
    int? MaxWritebacksPerFrame = null,
    int? RequestTimeoutSeconds = null,
    string? ReasoningEffort = null,
    string? OutputVerbosity = null,
    string? DeepSeekThinkingMode = null,
    double? Temperature = null,
    string? CustomInstruction = null,
    string? CustomPrompt = null,
    int? MaxSourceTextLength = null,
    bool? IgnoreInvisibleText = null,
    bool? SkipNumericSymbolText = null,
    bool? EnableCacheLookup = null,
    bool? ManualEditsOverrideAi = null,
    bool? ReapplyRememberedTranslations = null,
    int? CacheRetentionDays = null,
    bool? EnableUgui = null,
    bool? EnableTmp = null,
    bool? EnableImgui = null);

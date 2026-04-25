namespace HUnityAutoTranslator.Core.Control;

public sealed record UpdateConfigRequest(
    string? TargetLanguage,
    int? MaxConcurrentRequests,
    int? RequestsPerMinute);

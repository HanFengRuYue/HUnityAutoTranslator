namespace HUnityAutoTranslator.Core.Control;

public sealed record ProviderStatus(
    string State,
    string Message,
    DateTimeOffset? CheckedUtc);

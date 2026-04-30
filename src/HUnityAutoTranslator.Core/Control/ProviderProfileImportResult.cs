namespace HUnityAutoTranslator.Core.Control;

public sealed record ProviderProfileImportResult(
    bool Succeeded,
    string Message,
    ProviderProfileState? Profile);

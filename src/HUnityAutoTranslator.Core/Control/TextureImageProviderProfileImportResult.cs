namespace HUnityAutoTranslator.Core.Control;

public sealed record TextureImageProviderProfileImportResult(
    bool Succeeded,
    string Message,
    TextureImageProviderProfileState? Profile);

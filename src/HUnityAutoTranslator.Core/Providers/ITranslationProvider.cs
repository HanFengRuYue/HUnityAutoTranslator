using HUnityAutoTranslator.Core.Configuration;

namespace HUnityAutoTranslator.Core.Providers;

public interface ITranslationProvider
{
    ProviderKind Kind { get; }

    Task<TranslationResponse> TranslateAsync(TranslationRequest request, CancellationToken cancellationToken);
}

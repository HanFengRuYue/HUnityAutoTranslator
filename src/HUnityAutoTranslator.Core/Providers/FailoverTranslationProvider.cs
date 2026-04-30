using HUnityAutoTranslator.Core.Configuration;

namespace HUnityAutoTranslator.Core.Providers;

public sealed class FailoverTranslationProvider : ITranslationProvider
{
    private readonly Func<IReadOnlyList<ProviderRuntimeProfile>> _candidateProvider;
    private readonly Func<ProviderRuntimeProfile, CancellationToken, Task<ITranslationProvider>> _providerFactory;
    private readonly Func<ProviderRuntimeProfile, string, bool> _failureReporter;
    private readonly Action<ProviderRuntimeProfile> _successReporter;
    private readonly Action<ProviderRuntimeProfile> _attemptReporter;

    public FailoverTranslationProvider(
        Func<IReadOnlyList<ProviderRuntimeProfile>> candidateProvider,
        Func<ProviderRuntimeProfile, ITranslationProvider> providerFactory,
        Func<ProviderRuntimeProfile, string, bool> failureReporter,
        Action<ProviderRuntimeProfile> successReporter,
        Action<ProviderRuntimeProfile>? attemptReporter = null)
        : this(
            candidateProvider,
            (profile, _) => Task.FromResult(providerFactory(profile)),
            failureReporter,
            successReporter,
            attemptReporter)
    {
    }

    public FailoverTranslationProvider(
        Func<IReadOnlyList<ProviderRuntimeProfile>> candidateProvider,
        Func<ProviderRuntimeProfile, CancellationToken, Task<ITranslationProvider>> providerFactory,
        Func<ProviderRuntimeProfile, string, bool> failureReporter,
        Action<ProviderRuntimeProfile> successReporter,
        Action<ProviderRuntimeProfile>? attemptReporter = null)
    {
        _candidateProvider = candidateProvider;
        _providerFactory = providerFactory;
        _failureReporter = failureReporter;
        _successReporter = successReporter;
        _attemptReporter = attemptReporter ?? (_ => { });
    }

    public ProviderKind Kind => _candidateProvider().FirstOrDefault()?.Profile.Kind ?? ProviderKind.OpenAI;

    public async Task<TranslationResponse> TranslateAsync(TranslationRequest request, CancellationToken cancellationToken)
    {
        var attempted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        TranslationResponse? lastFailure = null;
        while (true)
        {
            var candidate = _candidateProvider()
                .FirstOrDefault(item => !attempted.Contains(item.Id));
            if (candidate == null)
            {
                return lastFailure ?? TranslationResponse.Failure("没有可用的服务商配置。");
            }

            attempted.Add(candidate.Id);
            try
            {
                _attemptReporter(candidate);
                var provider = await _providerFactory(candidate, cancellationToken).ConfigureAwait(false);
                var response = await provider.TranslateAsync(request, cancellationToken).ConfigureAwait(false);
                if (response.Succeeded)
                {
                    _successReporter(candidate);
                    return response with
                    {
                        Provider = response.Provider ?? candidate.Profile,
                        ProviderProfileId = candidate.Id,
                        ProviderProfileName = candidate.Name
                    };
                }

                lastFailure = response with
                {
                    Provider = response.Provider ?? candidate.Profile,
                    ProviderProfileId = candidate.Id,
                    ProviderProfileName = candidate.Name
                };
                if (!_failureReporter(candidate, response.ErrorMessage ?? "未知错误"))
                {
                    return lastFailure;
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                lastFailure = TranslationResponse.Failure(ex.Message, candidate.Profile, candidate.Id, candidate.Name);
                if (!_failureReporter(candidate, ex.Message))
                {
                    return lastFailure;
                }
            }
        }
    }
}

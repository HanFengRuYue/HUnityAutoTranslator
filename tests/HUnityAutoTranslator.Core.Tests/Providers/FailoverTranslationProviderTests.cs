using FluentAssertions;
using HUnityAutoTranslator.Core.Configuration;
using HUnityAutoTranslator.Core.Providers;

namespace HUnityAutoTranslator.Core.Tests.Providers;

public sealed class FailoverTranslationProviderTests
{
    [Fact]
    public async Task TranslateAsync_switches_to_next_profile_when_second_failure_triggers_cooldown()
    {
        var primary = ProviderRuntimeProfile.Create(
            ProviderProfileDefinition.CreateDefault("主服务商", ProviderKind.OpenAI, priority: 0) with
            {
                Id = "primary",
                ApiKey = "primary-key"
            });
        var fallback = ProviderRuntimeProfile.Create(
            ProviderProfileDefinition.CreateDefault("备用服务商", ProviderKind.DeepSeek, priority: 1) with
            {
                Id = "fallback",
                ApiKey = "fallback-key"
            });
        var failures = new List<string>();
        var cooldownProfiles = new HashSet<string>();
        var provider = new FailoverTranslationProvider(
            () => new[] { primary, fallback }.Where(item => !cooldownProfiles.Contains(item.Id)).ToArray(),
            candidate => candidate.Id == "primary"
                ? new FailureProvider("primary failed")
                : new SuccessProvider(candidate.Profile, "fallback translated"),
            (candidate, error) =>
            {
                failures.Add($"{candidate.Id}:{error}");
                if (candidate.Id == "primary" && failures.Count(item => item.StartsWith("primary:", StringComparison.Ordinal)) >= 2)
                {
                    cooldownProfiles.Add(candidate.Id);
                    return true;
                }

                return false;
            },
            _ => { });
        var request = new TranslationRequest(new[] { "Start" }, "zh-Hans", "system", "user");

        var first = await provider.TranslateAsync(request, CancellationToken.None);
        var second = await provider.TranslateAsync(request, CancellationToken.None);

        first.Succeeded.Should().BeFalse();
        second.Succeeded.Should().BeTrue();
        second.TranslatedTexts.Should().Equal("fallback translated");
        second.Provider.Should().Be(fallback.Profile);
        second.ProviderProfileId.Should().Be("fallback");
    }

    [Fact]
    public async Task TranslateAsync_awaits_async_provider_factory_for_llamacpp_profile()
    {
        var local = ProviderRuntimeProfile.Create(
            ProviderProfileDefinition.CreateDefault("本地模型", ProviderKind.LlamaCpp, priority: 0) with
            {
                Id = "local",
                LlamaCpp = LlamaCppConfig.Default() with { ModelPath = @"D:\Models\local.gguf", ParallelSlots = 2 }
            });
        var factoryCalled = false;
        var provider = new FailoverTranslationProvider(
            () => new[] { local },
            async (candidate, cancellationToken) =>
            {
                await Task.Yield();
                cancellationToken.ThrowIfCancellationRequested();
                candidate.LlamaCpp!.ModelPath.Should().Be(@"D:\Models\local.gguf");
                factoryCalled = true;
                return new SuccessProvider(
                    candidate.Profile with { BaseUrl = "http://127.0.0.1:51234" },
                    "本地译文");
            },
            (_, _) => false,
            _ => { });
        var request = new TranslationRequest(new[] { "Start" }, "zh-Hans", "system", "user");

        var result = await provider.TranslateAsync(request, CancellationToken.None);

        factoryCalled.Should().BeTrue();
        result.Succeeded.Should().BeTrue();
        result.TranslatedTexts.Should().Equal("本地译文");
        result.ProviderProfileId.Should().Be("local");
        result.Provider!.Kind.Should().Be(ProviderKind.LlamaCpp);
        result.Provider.BaseUrl.Should().Be("http://127.0.0.1:51234");
    }

    private sealed class FailureProvider : ITranslationProvider
    {
        private readonly string _message;

        public FailureProvider(string message) => _message = message;

        public ProviderKind Kind => ProviderKind.OpenAI;

        public Task<TranslationResponse> TranslateAsync(TranslationRequest request, CancellationToken cancellationToken)
        {
            return Task.FromResult(TranslationResponse.Failure(_message));
        }
    }

    private sealed class SuccessProvider : ITranslationProvider
    {
        private readonly ProviderProfile _profile;
        private readonly string _translation;

        public SuccessProvider(ProviderProfile profile, string translation)
        {
            _profile = profile;
            _translation = translation;
        }

        public ProviderKind Kind => _profile.Kind;

        public Task<TranslationResponse> TranslateAsync(TranslationRequest request, CancellationToken cancellationToken)
        {
            return Task.FromResult(TranslationResponse.Success(new[] { _translation }, provider: _profile));
        }
    }
}

using FluentAssertions;
using HUnityAutoTranslator.Core.Configuration;
using HUnityAutoTranslator.Core.Providers;
using HUnityAutoTranslator.Core.Tests.Http;
using Newtonsoft.Json.Linq;

namespace HUnityAutoTranslator.Core.Tests.Providers;

public sealed class ProviderRequestOptionsTests
{
    [Fact]
    public async Task OpenAiResponsesProvider_sends_reasoning_and_verbosity()
    {
        var transport = new FakeHttpTransport(_ => FakeHttpTransport.Json(
            """{"output":[{"content":[{"text":"[\"Start translated\"]"}]}],"usage":{"input_tokens":10,"output_tokens":5,"total_tokens":15}}"""));
        var profile = ProviderProfile.DefaultOpenAi() with { ApiKeyConfigured = true };
        var provider = new OpenAiResponsesProvider(transport, profile, () => "key", "low", "low", TimeSpan.FromSeconds(30));

        var response = await provider.TranslateAsync(new TranslationRequest(
            new[] { "Start" },
            "zh-Hans",
            "system",
            "user"), CancellationToken.None);

        var body = JObject.Parse(transport.LastStringBody);
        body["reasoning"]!["effort"]!.Value<string>().Should().Be("low");
        body["text"]!["verbosity"]!.Value<string>().Should().Be("low");
        response.TotalTokens.Should().Be(15);
    }

    [Fact]
    public async Task OpenAiResponsesProvider_omits_reasoning_when_effort_is_none()
    {
        var transport = new FakeHttpTransport(_ => FakeHttpTransport.Json(
            """{"output":[{"content":[{"text":"[\"Start translated\"]"}]}]}"""));
        var profile = ProviderProfile.DefaultOpenAi() with { ApiKeyConfigured = true };
        var provider = new OpenAiResponsesProvider(transport, profile, () => "key", "none", "low", TimeSpan.FromSeconds(30));

        await provider.TranslateAsync(new TranslationRequest(
            new[] { "Start" },
            "zh-Hans",
            "system",
            "user"), CancellationToken.None);

        var body = JObject.Parse(transport.LastStringBody);
        body.ContainsKey("reasoning").Should().BeFalse();
        body["text"]!["verbosity"]!.Value<string>().Should().Be("low");
    }

    [Fact]
    public async Task ChatCompletionsProvider_sends_deepseek_thinking_options()
    {
        var transport = new FakeHttpTransport(_ => FakeHttpTransport.Json(
            """{"choices":[{"message":{"content":"[\"Start translated\"]"}}],"usage":{"total_tokens":21}}"""));
        var profile = ProviderProfile.DefaultDeepSeek() with { ApiKeyConfigured = true };
        var provider = new ChatCompletionsProvider(transport, profile, () => "key", "high", "enabled", 0.2, TimeSpan.FromSeconds(30));

        var response = await provider.TranslateAsync(new TranslationRequest(
            new[] { "Start" },
            "zh-Hans",
            "system",
            "user"), CancellationToken.None);

        var body = JObject.Parse(transport.LastStringBody);
        body["reasoning_effort"]!.Value<string>().Should().Be("high");
        body["thinking"]!["type"]!.Value<string>().Should().Be("enabled");
        body["temperature"]!.Value<double>().Should().Be(0.2);
        response.TotalTokens.Should().Be(21);
    }

    [Fact]
    public async Task ChatCompletionsProvider_maps_deepseek_xhigh_effort_to_max()
    {
        var transport = new FakeHttpTransport(_ => FakeHttpTransport.Json(
            """{"choices":[{"message":{"content":"[\"Start translated\"]"}}]}"""));
        var profile = ProviderProfile.DefaultDeepSeek() with { ApiKeyConfigured = true };
        var provider = new ChatCompletionsProvider(transport, profile, () => "key", "xhigh", "enabled", null, TimeSpan.FromSeconds(30));

        await provider.TranslateAsync(new TranslationRequest(
            new[] { "Start" },
            "zh-Hans",
            "system",
            "user"), CancellationToken.None);

        var body = JObject.Parse(transport.LastStringBody);
        body["reasoning_effort"]!.Value<string>().Should().Be("max");
    }

    [Fact]
    public async Task ChatCompletionsProvider_omits_deepseek_effort_when_thinking_disabled()
    {
        var transport = new FakeHttpTransport(_ => FakeHttpTransport.Json(
            """{"choices":[{"message":{"content":"[\"Start translated\"]"}}]}"""));
        var profile = ProviderProfile.DefaultDeepSeek() with { ApiKeyConfigured = true };
        var provider = new ChatCompletionsProvider(transport, profile, () => "key", "high", "disabled", null, TimeSpan.FromSeconds(30));

        await provider.TranslateAsync(new TranslationRequest(
            new[] { "Start" },
            "zh-Hans",
            "system",
            "user"), CancellationToken.None);

        var body = JObject.Parse(transport.LastStringBody);
        body["thinking"]!["type"]!.Value<string>().Should().Be("disabled");
        body.ContainsKey("reasoning_effort").Should().BeFalse();
    }

    [Fact]
    public async Task ChatCompletionsProvider_sends_llamacpp_request_without_api_key_and_disables_thinking()
    {
        var transport = new FakeHttpTransport(_ => FakeHttpTransport.Json(
            """{"choices":[{"message":{"content":"[\"Start translated\"]"}}],"usage":{"total_tokens":9}}"""));
        var profile = ProviderProfile.DefaultLlamaCpp() with { BaseUrl = "http://127.0.0.1:51234", Model = "qwen-game-ui", ApiKeyConfigured = true };
        var provider = new ChatCompletionsProvider(transport, profile, () => null, "high", "enabled", 0.1, TimeSpan.FromSeconds(30));

        var response = await provider.TranslateAsync(new TranslationRequest(
            new[] { "Start" },
            "zh-Hans",
            "system",
            "user"), CancellationToken.None);

        var body = JObject.Parse(transport.LastStringBody);
        transport.LastPath.Should().Be("/v1/chat/completions");
        transport.AuthorizationHeader.Should().BeNull();
        body["model"]!.Value<string>().Should().Be("qwen-game-ui");
        body.ContainsKey("thinking").Should().BeFalse();
        body.ContainsKey("reasoning_effort").Should().BeFalse();
        body["chat_template_kwargs"]!["enable_thinking"]!.Value<bool>().Should().BeFalse();
        body["temperature"]!.Value<double>().Should().Be(0.1);
        response.TotalTokens.Should().Be(9);
    }

    [Fact]
    public async Task ChatCompletionsProvider_uses_low_default_temperature_for_llamacpp_when_unset()
    {
        var transport = new FakeHttpTransport(_ => FakeHttpTransport.Json(
            """{"choices":[{"message":{"content":"[\"Start translated\"]"}}]}"""));
        var profile = ProviderProfile.DefaultLlamaCpp() with { BaseUrl = "http://127.0.0.1:51234", Model = "qwen-game-ui", ApiKeyConfigured = true };
        var provider = new ChatCompletionsProvider(transport, profile, () => null, "none", "disabled", null, TimeSpan.FromSeconds(30));

        await provider.TranslateAsync(new TranslationRequest(
            new[] { "Start" },
            "zh-Hans",
            "system",
            "user"), CancellationToken.None);

        var body = JObject.Parse(transport.LastStringBody);
        body["temperature"]!.Value<double>().Should().Be(0.1);
        body["chat_template_kwargs"]!["enable_thinking"]!.Value<bool>().Should().BeFalse();
    }

    [Fact]
    public async Task ChatCompletionsProvider_applies_openai_compatible_headers_and_extra_body_without_overriding_core_fields()
    {
        var transport = new FakeHttpTransport(_ => FakeHttpTransport.Json(
            """{"choices":[{"message":{"content":"[\"Start translated\"]"}}]}"""));
        var profile = new ProviderProfile(
            ProviderKind.OpenAICompatible,
            "http://127.0.0.1:9000",
            "/v1/chat/completions",
            "proxy-model",
            true,
            """
            X-App-Title: HUnity
            Authorization: Bearer ignored
            Content-Type: text/plain
            X-Feature: translation
            """,
            """{"stream":false,"model":"ignored-model","messages":[{"role":"user","content":"ignored"}],"metadata":{"source":"panel"}}""");
        var provider = new ChatCompletionsProvider(transport, profile, () => "key", "none", "disabled", null, TimeSpan.FromSeconds(30));

        await provider.TranslateAsync(new TranslationRequest(
            new[] { "Start" },
            "zh-Hans",
            "system",
            "user"), CancellationToken.None);

        var body = JObject.Parse(transport.LastStringBody);
        transport.AuthorizationHeader.Should().Be("Bearer key");
        transport.Header("X-App-Title").Should().Be("HUnity");
        transport.Header("X-Feature").Should().Be("translation");
        transport.Header("Content-Type").Should().BeNull();
        transport.LastStringBodyContentType.Should().Be("application/json");
        body["model"]!.Value<string>().Should().Be("proxy-model");
        body["messages"]!.Should().HaveCount(2);
        body["stream"]!.Value<bool>().Should().BeFalse();
        body["metadata"]!["source"]!.Value<string>().Should().Be("panel");
    }
}

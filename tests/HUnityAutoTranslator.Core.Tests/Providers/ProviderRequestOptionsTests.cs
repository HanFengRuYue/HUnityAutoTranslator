using System.Net;
using System.Text;
using FluentAssertions;
using HUnityAutoTranslator.Core.Configuration;
using HUnityAutoTranslator.Core.Providers;
using Newtonsoft.Json.Linq;

namespace HUnityAutoTranslator.Core.Tests.Providers;

public sealed class ProviderRequestOptionsTests
{
    [Fact]
    public async Task OpenAiResponsesProvider_sends_reasoning_and_verbosity()
    {
        var handler = new CaptureHandler("""{"output":[{"content":[{"text":"[\"Start translated\"]"}]}],"usage":{"input_tokens":10,"output_tokens":5,"total_tokens":15}}""");
        var client = new HttpClient(handler);
        var profile = ProviderProfile.DefaultOpenAi() with { ApiKeyConfigured = true };
        var provider = new OpenAiResponsesProvider(client, profile, () => "key", "low", "low", TimeSpan.FromSeconds(30));

        var response = await provider.TranslateAsync(new TranslationRequest(
            new[] { "Start" },
            "zh-Hans",
            "system",
            "user"), CancellationToken.None);

        var body = JObject.Parse(handler.Body);
        body["reasoning"]!["effort"]!.Value<string>().Should().Be("low");
        body["text"]!["verbosity"]!.Value<string>().Should().Be("low");
        response.TotalTokens.Should().Be(15);
    }

    [Fact]
    public async Task ChatCompletionsProvider_sends_deepseek_thinking_options()
    {
        var handler = new CaptureHandler("""{"choices":[{"message":{"content":"[\"Start translated\"]"}}],"usage":{"total_tokens":21}}""");
        var client = new HttpClient(handler);
        var profile = ProviderProfile.DefaultDeepSeek() with { ApiKeyConfigured = true };
        var provider = new ChatCompletionsProvider(client, profile, () => "key", "high", "enabled", 0.2, TimeSpan.FromSeconds(30));

        var response = await provider.TranslateAsync(new TranslationRequest(
            new[] { "Start" },
            "zh-Hans",
            "system",
            "user"), CancellationToken.None);

        var body = JObject.Parse(handler.Body);
        body["reasoning_effort"]!.Value<string>().Should().Be("high");
        body["thinking"]!["type"]!.Value<string>().Should().Be("enabled");
        body["temperature"]!.Value<double>().Should().Be(0.2);
        response.TotalTokens.Should().Be(21);
    }

    [Fact]
    public async Task ChatCompletionsProvider_maps_deepseek_xhigh_effort_to_max()
    {
        var handler = new CaptureHandler("""{"choices":[{"message":{"content":"[\"Start translated\"]"}}]}""");
        var client = new HttpClient(handler);
        var profile = ProviderProfile.DefaultDeepSeek() with { ApiKeyConfigured = true };
        var provider = new ChatCompletionsProvider(client, profile, () => "key", "xhigh", "enabled", null, TimeSpan.FromSeconds(30));

        await provider.TranslateAsync(new TranslationRequest(
            new[] { "Start" },
            "zh-Hans",
            "system",
            "user"), CancellationToken.None);

        var body = JObject.Parse(handler.Body);
        body["reasoning_effort"]!.Value<string>().Should().Be("max");
    }

    [Fact]
    public async Task ChatCompletionsProvider_omits_deepseek_effort_when_thinking_disabled()
    {
        var handler = new CaptureHandler("""{"choices":[{"message":{"content":"[\"Start translated\"]"}}]}""");
        var client = new HttpClient(handler);
        var profile = ProviderProfile.DefaultDeepSeek() with { ApiKeyConfigured = true };
        var provider = new ChatCompletionsProvider(client, profile, () => "key", "high", "disabled", null, TimeSpan.FromSeconds(30));

        await provider.TranslateAsync(new TranslationRequest(
            new[] { "Start" },
            "zh-Hans",
            "system",
            "user"), CancellationToken.None);

        var body = JObject.Parse(handler.Body);
        body["thinking"]!["type"]!.Value<string>().Should().Be("disabled");
        body.ContainsKey("reasoning_effort").Should().BeFalse();
    }

    private sealed class CaptureHandler : HttpMessageHandler
    {
        private readonly string _json;

        public CaptureHandler(string json)
        {
            _json = json;
        }

        public string Body { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Body = request.Content == null ? string.Empty : await request.Content.ReadAsStringAsync();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_json, Encoding.UTF8, "application/json")
            };
        }
    }
}

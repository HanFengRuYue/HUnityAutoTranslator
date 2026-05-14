using HUnityAutoTranslator.Core.Configuration;
using HUnityAutoTranslator.Core.Http;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace HUnityAutoTranslator.Core.Providers;

public sealed class ChatCompletionsProvider : ITranslationProvider
{
    private readonly IHttpTransport _transport;
    private readonly ProviderProfile _profile;
    private readonly Func<string?> _apiKeyProvider;
    private readonly string _reasoningEffort;
    private readonly string _deepSeekThinkingMode;
    private readonly double? _temperature;
    private readonly TimeSpan _timeout;

    public ChatCompletionsProvider(
        IHttpTransport transport,
        ProviderProfile profile,
        Func<string?> apiKeyProvider,
        string reasoningEffort = "none",
        string deepSeekThinkingMode = "disabled",
        double? temperature = null,
        TimeSpan? timeout = null)
    {
        _transport = transport;
        _profile = profile;
        _apiKeyProvider = apiKeyProvider;
        _reasoningEffort = reasoningEffort;
        _deepSeekThinkingMode = deepSeekThinkingMode;
        _temperature = temperature;
        _timeout = timeout ?? TimeSpan.FromSeconds(30);
    }

    public ProviderKind Kind => _profile.Kind;

    public async Task<TranslationResponse> TranslateAsync(TranslationRequest request, CancellationToken cancellationToken)
    {
        var body = new JObject
        {
            ["model"] = _profile.Model,
            ["messages"] = new JArray
            {
                new JObject
                {
                    ["role"] = "system",
                    ["content"] = request.SystemPrompt
                },
                new JObject
                {
                    ["role"] = "user",
                    ["content"] = request.UserPrompt
                }
            }
        };
        var effectiveTemperature = _temperature ?? (_profile.Kind == ProviderKind.LlamaCpp ? 0.1 : (double?)null);
        if (effectiveTemperature.HasValue)
        {
            body["temperature"] = effectiveTemperature.Value;
        }

        if (_profile.Kind == ProviderKind.DeepSeek)
        {
            body["thinking"] = new JObject { ["type"] = _deepSeekThinkingMode };
            if (!string.Equals(_deepSeekThinkingMode, "disabled", StringComparison.OrdinalIgnoreCase))
            {
                body["reasoning_effort"] = NormalizeDeepSeekReasoningEffort(_reasoningEffort);
            }
        }
        else if (_profile.Kind == ProviderKind.LlamaCpp)
        {
            body["chat_template_kwargs"] = new JObject
            {
                ["enable_thinking"] = false
            };
        }

        OpenAICompatibleRequestOptions.ApplyExtraBody(body, _profile);

        var httpRequest = CreateRequest(body);
        var response = await _transport.SendAsync(httpRequest, cancellationToken).ConfigureAwait(false);
        if (response.Error != null)
        {
            return TranslationResponse.Failure($"Chat completions request failed: {response.Error.Message}");
        }

        if (!response.IsSuccessStatusCode)
        {
            return TranslationResponse.Failure($"Chat completions request failed: {response.StatusCode}");
        }

        var text = ProviderJsonParsers.ParseChatCompletionsText(response.Body);
        return TranslationResponse.Success(
            ProviderJsonParsers.ParseAssistantTextAsList(text, request.ProtectedTexts.Count),
            ProviderJsonParsers.ParseTotalTokens(response.Body),
            _profile);
    }

    private HttpTransportRequest CreateRequest(JObject body)
    {
        var uri = new Uri(new Uri(_profile.BaseUrl.TrimEnd(new[] { '/' }) + "/"), _profile.Endpoint.TrimStart(new[] { '/' }));
        var headers = new List<HttpHeaderEntry>();
        var apiKey = _apiKeyProvider();
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            headers.Add(new HttpHeaderEntry("Authorization", "Bearer " + apiKey));
        }

        headers.AddRange(OpenAICompatibleRequestOptions.GetCustomHeaders(_profile));

        return new HttpTransportRequest
        {
            Method = HttpTransportMethod.Post,
            Uri = uri,
            Headers = headers,
            StringBody = new HttpTransportStringBody
            {
                Content = JsonConvert.SerializeObject(body, Formatting.None),
                ContentType = "application/json",
            },
            Timeout = _timeout,
        };
    }

    private static string NormalizeDeepSeekReasoningEffort(string reasoningEffort)
    {
        return string.Equals(reasoningEffort, "xhigh", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(reasoningEffort, "max", StringComparison.OrdinalIgnoreCase)
                ? "max"
                : "high";
    }
}

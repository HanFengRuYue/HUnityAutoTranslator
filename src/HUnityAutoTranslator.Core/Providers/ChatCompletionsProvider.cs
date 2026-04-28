using System.Net.Http;
using System.Text;
using HUnityAutoTranslator.Core.Configuration;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace HUnityAutoTranslator.Core.Providers;

public sealed class ChatCompletionsProvider : ITranslationProvider
{
    private readonly HttpClient _httpClient;
    private readonly ProviderProfile _profile;
    private readonly Func<string?> _apiKeyProvider;
    private readonly string _reasoningEffort;
    private readonly string _deepSeekThinkingMode;
    private readonly double? _temperature;
    private readonly TimeSpan _timeout;

    public ChatCompletionsProvider(
        HttpClient httpClient,
        ProviderProfile profile,
        Func<string?> apiKeyProvider,
        string reasoningEffort = "none",
        string deepSeekThinkingMode = "disabled",
        double? temperature = null,
        TimeSpan? timeout = null)
    {
        _httpClient = httpClient;
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

        using var httpRequest = CreateRequest(body);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_timeout);
        using var response = await _httpClient.SendAsync(httpRequest, timeout.Token).ConfigureAwait(false);
        var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return TranslationResponse.Failure($"Chat completions request failed: {(int)response.StatusCode}");
        }

        var text = ProviderJsonParsers.ParseChatCompletionsText(json);
        return TranslationResponse.Success(
            ProviderJsonParsers.ParseAssistantTextAsList(text),
            ProviderJsonParsers.ParseTotalTokens(json));
    }

    private HttpRequestMessage CreateRequest(JObject body)
    {
        var uri = new Uri(new Uri(_profile.BaseUrl.TrimEnd('/') + "/"), _profile.Endpoint.TrimStart('/'));
        var message = new HttpRequestMessage(HttpMethod.Post, uri)
        {
            Content = new StringContent(JsonConvert.SerializeObject(body, Formatting.None), Encoding.UTF8, "application/json")
        };

        var apiKey = _apiKeyProvider();
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            message.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
        }

        return message;
    }

    private static string NormalizeDeepSeekReasoningEffort(string reasoningEffort)
    {
        return string.Equals(reasoningEffort, "xhigh", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(reasoningEffort, "max", StringComparison.OrdinalIgnoreCase)
                ? "max"
                : "high";
    }
}

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

    public ChatCompletionsProvider(HttpClient httpClient, ProviderProfile profile, Func<string?> apiKeyProvider)
    {
        _httpClient = httpClient;
        _profile = profile;
        _apiKeyProvider = apiKeyProvider;
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

        using var httpRequest = CreateRequest(body);
        using var response = await _httpClient.SendAsync(httpRequest, cancellationToken).ConfigureAwait(false);
        var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return TranslationResponse.Failure($"Chat completions request failed: {(int)response.StatusCode}");
        }

        var text = ProviderJsonParsers.ParseChatCompletionsText(json);
        return TranslationResponse.Success(ProviderJsonParsers.ParseAssistantTextAsList(text));
    }

    private HttpRequestMessage CreateRequest(JObject body)
    {
        var uri = new Uri(new Uri(_profile.BaseUrl.TrimEnd('/') + "/"), _profile.Endpoint.TrimStart('/'));
        var message = new HttpRequestMessage(HttpMethod.Post, uri)
        {
            Content = new StringContent(body.ToString(Formatting.None), Encoding.UTF8, "application/json")
        };

        var apiKey = _apiKeyProvider();
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            message.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
        }

        return message;
    }
}

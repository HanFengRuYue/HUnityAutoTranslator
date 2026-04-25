using System.Net.Http;
using System.Text;
using HUnityAutoTranslator.Core.Configuration;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace HUnityAutoTranslator.Core.Providers;

public sealed class OpenAiResponsesProvider : ITranslationProvider
{
    private readonly HttpClient _httpClient;
    private readonly ProviderProfile _profile;
    private readonly Func<string?> _apiKeyProvider;

    public OpenAiResponsesProvider(HttpClient httpClient, ProviderProfile profile, Func<string?> apiKeyProvider)
    {
        _httpClient = httpClient;
        _profile = profile;
        _apiKeyProvider = apiKeyProvider;
    }

    public ProviderKind Kind => ProviderKind.OpenAI;

    public async Task<TranslationResponse> TranslateAsync(TranslationRequest request, CancellationToken cancellationToken)
    {
        var body = new JObject
        {
            ["model"] = _profile.Model,
            ["instructions"] = request.SystemPrompt,
            ["input"] = request.UserPrompt
        };

        using var httpRequest = CreateRequest(body);
        using var response = await _httpClient.SendAsync(httpRequest, cancellationToken).ConfigureAwait(false);
        var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return TranslationResponse.Failure($"OpenAI request failed: {(int)response.StatusCode}");
        }

        var text = ProviderJsonParsers.ParseOpenAiResponsesText(json);
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

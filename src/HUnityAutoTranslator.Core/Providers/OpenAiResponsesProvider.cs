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
    private readonly string _reasoningEffort;
    private readonly string _outputVerbosity;
    private readonly TimeSpan _timeout;

    public OpenAiResponsesProvider(
        HttpClient httpClient,
        ProviderProfile profile,
        Func<string?> apiKeyProvider,
        string reasoningEffort = "low",
        string outputVerbosity = "low",
        TimeSpan? timeout = null)
    {
        _httpClient = httpClient;
        _profile = profile;
        _apiKeyProvider = apiKeyProvider;
        _reasoningEffort = reasoningEffort;
        _outputVerbosity = outputVerbosity;
        _timeout = timeout ?? TimeSpan.FromSeconds(30);
    }

    public ProviderKind Kind => ProviderKind.OpenAI;

    public async Task<TranslationResponse> TranslateAsync(TranslationRequest request, CancellationToken cancellationToken)
    {
        var body = new JObject
        {
            ["model"] = _profile.Model,
            ["instructions"] = request.SystemPrompt,
            ["input"] = request.UserPrompt,
            ["text"] = new JObject { ["verbosity"] = _outputVerbosity }
        };
        if (!string.Equals(_reasoningEffort, "none", StringComparison.OrdinalIgnoreCase))
        {
            body["reasoning"] = new JObject { ["effort"] = _reasoningEffort };
        }

        using var httpRequest = CreateRequest(body);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_timeout);
        using var response = await _httpClient.SendAsync(httpRequest, timeout.Token).ConfigureAwait(false);
        var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return TranslationResponse.Failure($"OpenAI request failed: {(int)response.StatusCode}");
        }

        var text = ProviderJsonParsers.ParseOpenAiResponsesText(json);
        return TranslationResponse.Success(
            ProviderJsonParsers.ParseAssistantTextAsList(text, request.ProtectedTexts.Count),
            ProviderJsonParsers.ParseTotalTokens(json),
            _profile);
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
}

using HUnityAutoTranslator.Core.Configuration;
using HUnityAutoTranslator.Core.Http;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace HUnityAutoTranslator.Core.Providers;

public sealed class OpenAiResponsesProvider : ITranslationProvider
{
    private readonly IHttpTransport _transport;
    private readonly ProviderProfile _profile;
    private readonly Func<string?> _apiKeyProvider;
    private readonly string _reasoningEffort;
    private readonly string _outputVerbosity;
    private readonly TimeSpan _timeout;

    public OpenAiResponsesProvider(
        IHttpTransport transport,
        ProviderProfile profile,
        Func<string?> apiKeyProvider,
        string reasoningEffort = "low",
        string outputVerbosity = "low",
        TimeSpan? timeout = null)
    {
        _transport = transport;
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

        var httpRequest = CreateRequest(body);
        var response = await _transport.SendAsync(httpRequest, cancellationToken).ConfigureAwait(false);
        if (response.Error != null)
        {
            return TranslationResponse.Failure($"OpenAI request failed: {response.Error.Message}");
        }

        if (!response.IsSuccessStatusCode)
        {
            return TranslationResponse.Failure($"OpenAI request failed: {response.StatusCode}");
        }

        var text = ProviderJsonParsers.ParseOpenAiResponsesText(response.Body);
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
}

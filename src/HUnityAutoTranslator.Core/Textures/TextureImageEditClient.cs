using System.Net.Http;
using System.Net.Http.Headers;
using HUnityAutoTranslator.Core.Configuration;
using Newtonsoft.Json.Linq;

namespace HUnityAutoTranslator.Core.Textures;

public sealed record TextureImageEditResult(byte[] PngBytes, string? RevisedPrompt);

public sealed class TextureImageEditClient
{
    private readonly HttpClient _httpClient;
    private readonly Func<string?> _apiKeyProvider;

    public TextureImageEditClient(HttpClient httpClient, Func<string?> apiKeyProvider)
    {
        _httpClient = httpClient;
        _apiKeyProvider = apiKeyProvider;
    }

    public async Task<TextureImageEditResult> EditAsync(
        TextureImageTranslationConfig config,
        string prompt,
        byte[] sourcePngBytes,
        string size,
        CancellationToken cancellationToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, BuildUri(config.BaseUrl, config.EditEndpoint));
        var apiKey = _apiKeyProvider();
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        }

        var content = new MultipartFormDataContent();
        content.Add(new StringContent(config.ImageModel), "model");
        content.Add(new StringContent(prompt), "prompt");
        content.Add(new StringContent(size), "size");
        content.Add(new StringContent(config.Quality), "quality");
        content.Add(new StringContent("png"), "output_format");
        content.Add(new StringContent("b64_json"), "response_format");
        var image = new ByteArrayContent(sourcePngBytes);
        image.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        content.Add(image, "image", "texture.png");
        request.Content = content;

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(Math.Max(30, config.TimeoutSeconds)));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        using var response = await _httpClient.SendAsync(request, linked.Token).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"贴图图片生成失败：HTTP {(int)response.StatusCode} {response.ReasonPhrase}。{ExtractError(body)}");
        }

        var json = JObject.Parse(body);
        var item = json["data"]?.FirstOrDefault()
            ?? throw new InvalidOperationException("贴图图片生成响应缺少 data。");
        var base64 = item.Value<string>("b64_json");
        if (string.IsNullOrWhiteSpace(base64))
        {
            throw new InvalidOperationException("贴图图片生成响应缺少 b64_json。");
        }

        return new TextureImageEditResult(Convert.FromBase64String(base64), item.Value<string>("revised_prompt"));
    }

    public static Uri BuildUri(string baseUrl, string endpoint)
    {
        var normalizedBase = string.IsNullOrWhiteSpace(baseUrl)
            ? TextureImageTranslationConfig.Default().BaseUrl
            : baseUrl.TrimEnd('/');
        var normalizedEndpoint = string.IsNullOrWhiteSpace(endpoint)
            ? TextureImageTranslationConfig.Default().EditEndpoint
            : endpoint.StartsWith("/", StringComparison.Ordinal) ? endpoint : "/" + endpoint;
        return new Uri(normalizedBase + normalizedEndpoint, UriKind.Absolute);
    }

    private static string ExtractError(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return string.Empty;
        }

        try
        {
            var json = JObject.Parse(body);
            return json["error"]?.Type == JTokenType.String
                ? json.Value<string>("error") ?? body
                : json["error"]?["message"]?.Value<string>() ?? body;
        }
        catch
        {
            return body;
        }
    }
}

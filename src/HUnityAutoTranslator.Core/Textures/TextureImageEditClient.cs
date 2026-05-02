using System.Net.Http;
using System.Net.Http.Headers;
using HUnityAutoTranslator.Core.Configuration;
using HUnityAutoTranslator.Core.Providers;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace HUnityAutoTranslator.Core.Textures;

public sealed record TextureImageEditResult(byte[] PngBytes, string? RevisedPrompt);

public sealed class TextureImageEditClient
{
    private const string ConnectionTestPrompt = "HUnityAutoTranslator 贴图翻译连接测试：请保持这张测试图片简单清晰，返回 PNG 图片即可。";
    private static readonly byte[] ConnectionTestPngBytes = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+/p9sAAAAASUVORK5CYII=");

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

    public async Task<ProviderTestResult> TestConnectionAsync(
        TextureImageTranslationConfig config,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await EditAsync(
                config,
                ConnectionTestPrompt,
                ConnectionTestPngBytes,
                "1024x1024",
                cancellationToken).ConfigureAwait(false);
            return LooksLikePng(result.PngBytes)
                ? new ProviderTestResult(true, "贴图翻译测试成功：图片编辑接口返回了 PNG。")
                : new ProviderTestResult(false, "贴图翻译测试失败：图片编辑接口已响应，但没有返回 PNG。");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException or JsonException or FormatException)
        {
            return new ProviderTestResult(false, $"贴图翻译测试失败：{ex.Message}");
        }
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

    private static bool LooksLikePng(byte[] bytes)
    {
        return bytes.Length >= 8 &&
            bytes[0] == 0x89 &&
            bytes[1] == 0x50 &&
            bytes[2] == 0x4E &&
            bytes[3] == 0x47 &&
            bytes[4] == 0x0D &&
            bytes[5] == 0x0A &&
            bytes[6] == 0x1A &&
            bytes[7] == 0x0A;
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

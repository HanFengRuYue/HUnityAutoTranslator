using HUnityAutoTranslator.Core.Configuration;
using HUnityAutoTranslator.Core.Http;
using HUnityAutoTranslator.Core.Providers;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace HUnityAutoTranslator.Core.Textures;

public sealed record TextureImageEditResult(byte[] PngBytes, string? RevisedPrompt);

public sealed class TextureImageEditClient
{
    private const string ConnectionTestPrompt = "HUnityAutoTranslator 贴图翻译连接测试：请保持这张测试图片简单清晰，返回 PNG 图片即可。";
    private static readonly byte[] ConnectionTestPngBytes = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+/p9sAAAAASUVORK5CYII=");

    private readonly IHttpTransport _transport;
    private readonly Func<string?> _apiKeyProvider;

    public TextureImageEditClient(IHttpTransport transport, Func<string?> apiKeyProvider)
    {
        _transport = transport;
        _apiKeyProvider = apiKeyProvider;
    }

    public async Task<TextureImageEditResult> EditAsync(
        TextureImageTranslationConfig config,
        string prompt,
        byte[] sourcePngBytes,
        string size,
        CancellationToken cancellationToken)
    {
        var headers = new List<HttpHeaderEntry>();
        var apiKey = _apiKeyProvider();
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            headers.Add(new HttpHeaderEntry("Authorization", "Bearer " + apiKey));
        }

        var parts = new List<HttpMultipartPart>
        {
            HttpMultipartPart.Text("model", config.ImageModel),
            HttpMultipartPart.Text("prompt", prompt),
            HttpMultipartPart.Text("size", size),
            HttpMultipartPart.Text("quality", config.Quality),
            HttpMultipartPart.Text("output_format", "png"),
            HttpMultipartPart.Text("response_format", "b64_json"),
            HttpMultipartPart.File("image", sourcePngBytes, "texture.png", "image/png"),
        };

        var request = new HttpTransportRequest
        {
            Method = HttpTransportMethod.Post,
            Uri = BuildUri(config.BaseUrl, config.EditEndpoint),
            Headers = headers,
            MultipartParts = parts,
            Timeout = TimeSpan.FromSeconds(Math.Max(30, config.TimeoutSeconds)),
        };

        var response = await _transport.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (response.Error != null)
        {
            throw new InvalidOperationException($"贴图图片生成失败：{response.Error.Message}");
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"贴图图片生成失败：HTTP {response.StatusCode} {response.ReasonPhrase}。{ExtractError(response.Body)}");
        }

        var json = JObject.Parse(response.Body);
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
        catch (Exception ex) when (ex is InvalidOperationException or JsonException or FormatException)
        {
            return new ProviderTestResult(false, $"贴图翻译测试失败：{ex.Message}");
        }
    }

    public static Uri BuildUri(string baseUrl, string endpoint)
    {
        var normalizedBase = string.IsNullOrWhiteSpace(baseUrl)
            ? TextureImageTranslationConfig.Default().BaseUrl
            : baseUrl.TrimEnd(new[] { '/' });
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

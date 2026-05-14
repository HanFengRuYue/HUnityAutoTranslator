using HUnityAutoTranslator.Core.Configuration;
using HUnityAutoTranslator.Core.Http;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace HUnityAutoTranslator.Core.Textures;

public sealed record TextureVisionTextResult(
    bool HasText,
    double Confidence,
    string? DetectedText,
    string? Reason,
    bool NeedsManualReview);

public sealed class TextureVisionTextClient
{
    private readonly IHttpTransport _transport;
    private readonly Func<string?> _apiKeyProvider;

    public TextureVisionTextClient(IHttpTransport transport, Func<string?> apiKeyProvider)
    {
        _transport = transport;
        _apiKeyProvider = apiKeyProvider;
    }

    public async Task<TextureVisionTextResult> DetectAsync(
        TextureImageTranslationConfig config,
        TextureCatalogItem item,
        byte[] sourcePngBytes,
        CancellationToken cancellationToken)
    {
        var headers = new List<HttpHeaderEntry>();
        var apiKey = _apiKeyProvider();
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            headers.Add(new HttpHeaderEntry("Authorization", "Bearer " + apiKey));
        }

        var payload = new JObject
        {
            ["model"] = config.VisionModel,
            ["input"] = new JArray
            {
                new JObject
                {
                    ["role"] = "user",
                    ["content"] = new JArray
                    {
                        new JObject
                        {
                            ["type"] = "input_text",
                            ["text"] = BuildPrompt(item)
                        },
                        new JObject
                        {
                            ["type"] = "input_image",
                            ["image_url"] = "data:image/png;base64," + Convert.ToBase64String(sourcePngBytes),
                            ["detail"] = "high"
                        }
                    }
                }
            }
        };

        var request = new HttpTransportRequest
        {
            Method = HttpTransportMethod.Post,
            Uri = TextureImageEditClient.BuildUri(config.BaseUrl, config.VisionEndpoint),
            Headers = headers,
            StringBody = new HttpTransportStringBody
            {
                Content = JsonConvert.SerializeObject(payload, Formatting.None),
                ContentType = "application/json",
            },
            Timeout = TimeSpan.FromSeconds(Math.Max(30, config.TimeoutSeconds)),
        };

        var response = await _transport.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (response.Error != null)
        {
            throw new InvalidOperationException($"贴图文字视觉确认失败：{response.Error.Message}");
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"贴图文字视觉确认失败：HTTP {response.StatusCode} {response.ReasonPhrase}。{response.Body}");
        }

        return ParseResult(ExtractOutputText(response.Body));
    }

    internal static TextureVisionTextResult ParseResult(string outputText)
    {
        var jsonStart = outputText.IndexOf('{');
        var jsonEnd = outputText.LastIndexOf('}');
        if (jsonStart >= 0 && jsonEnd > jsonStart)
        {
            outputText = outputText.Substring(jsonStart, jsonEnd - jsonStart + 1);
        }

        var json = JObject.Parse(outputText);
        return new TextureVisionTextResult(
            json.Value<bool?>("has_text") ?? false,
            Math.Max(0, Math.Min(1, json.Value<double?>("confidence") ?? 0)),
            json.Value<string>("detected_text"),
            json.Value<string>("reason"),
            json.Value<bool?>("needs_manual_review") ?? false);
    }

    private static string BuildPrompt(TextureCatalogItem item)
    {
        return "判断这张游戏贴图是否包含需要翻译的可见文字。艺术字、海报、招牌、标题图、按钮文字都算有文字；纯材质、图标、法线图、噪声贴图算无文字。只返回 JSON：" +
            "{\"has_text\":true|false,\"confidence\":0到1,\"detected_text\":\"能读出的原文，读不清就为空\",\"reason\":\"简短原因\",\"needs_manual_review\":true|false}。" +
            $"贴图名：{item.TextureName}，尺寸：{item.Width}x{item.Height}，引用：{string.Join(" / ", item.References.Select(reference => reference.ComponentHierarchy ?? reference.ComponentType ?? reference.SceneName).Where(value => !string.IsNullOrWhiteSpace(value)).Take(3))}";
    }

    private static string ExtractOutputText(string body)
    {
        var json = JObject.Parse(body);
        var direct = json.Value<string>("output_text");
        if (!string.IsNullOrWhiteSpace(direct))
        {
            return direct;
        }

        if (json["output"] is JArray output)
        {
            foreach (var item in output)
            {
                if (item["content"] is not JArray contentItems)
                {
                    continue;
                }

                foreach (var content in contentItems)
                {
                    var text = content.Value<string>("text");
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        return text;
                    }
                }
            }
        }

        throw new InvalidOperationException("贴图文字视觉确认响应缺少 output_text。");
    }
}

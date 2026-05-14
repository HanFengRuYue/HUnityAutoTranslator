using System.Globalization;
using HUnityAutoTranslator.Core.Configuration;
using HUnityAutoTranslator.Core.Http;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace HUnityAutoTranslator.Core.Providers;

public sealed class ProviderUtilityClient
{
    private readonly IHttpTransport _transport;
    private readonly Func<string?> _apiKeyProvider;

    public ProviderUtilityClient(IHttpTransport transport, Func<string?> apiKeyProvider)
    {
        _transport = transport;
        _apiKeyProvider = apiKeyProvider;
    }

    public async Task<ProviderModelsResult> FetchModelsAsync(ProviderProfile profile, CancellationToken cancellationToken)
    {
        if (profile.Kind == ProviderKind.LlamaCpp)
        {
            return new ProviderModelsResult(
                true,
                "本地模型使用当前 GGUF 文件。",
                new[] { new ProviderModelInfo(profile.Model, "llama.cpp") });
        }

        var path = profile.Kind == ProviderKind.DeepSeek ? "/models" : "/v1/models";
        var request = CreateGet(profile, path);
        var response = await _transport.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return new ProviderModelsResult(
                false,
                BuildFailureMessage("获取模型列表失败", response, request.Uri.AbsolutePath, HasSavedApiKey()),
                Array.Empty<ProviderModelInfo>());
        }

        var data = JObject.Parse(response.Body)["data"] as JArray ?? new JArray();
        var models = data
            .OfType<JObject>()
            .Select(item => new ProviderModelInfo(
                item.Value<string>("id") ?? string.Empty,
                item.Value<string>("owned_by")))
            .Where(item => !string.IsNullOrWhiteSpace(item.Id))
            .ToArray();

        return new ProviderModelsResult(true, $"已获取 {models.Length} 个模型。", models);
    }

    public async Task<ProviderTestResult> TestConnectionAsync(ProviderProfile profile, CancellationToken cancellationToken)
    {
        if (profile.Kind == ProviderKind.LlamaCpp)
        {
            return new ProviderTestResult(true, "本地模型使用当前 GGUF 文件。");
        }

        var request = CreateTestRequest(profile);
        var response = await _transport.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var path = request.Uri.AbsolutePath;
        if (!response.IsSuccessStatusCode)
        {
            return new ProviderTestResult(
                false,
                BuildFailureMessage("连接测试失败", response, path, HasSavedApiKey()));
        }

        var reply = ParseTestReply(profile, response.Body);
        if (string.IsNullOrWhiteSpace(reply))
        {
            return new ProviderTestResult(false, $"连接测试失败：接口已响应，但没有返回模型文本（路径 {path}）。");
        }

        return new ProviderTestResult(true, $"模型已返回测试回复：{TrimForMessage(reply)}");
    }

    public async Task<ProviderBalanceResult> FetchBalanceAsync(ProviderProfile profile, CancellationToken cancellationToken)
    {
        if (profile.Kind == ProviderKind.LlamaCpp)
        {
            return new ProviderBalanceResult(true, "本地模型不适用账户余额查询。", Array.Empty<ProviderBalanceInfo>());
        }

        var path = profile.Kind == ProviderKind.DeepSeek
            ? "/user/balance"
            : $"/v1/organization/costs?start_time={DateTimeOffset.UtcNow.AddDays(-7).ToUnixTimeSeconds()}&limit=7";
        var request = CreateGet(profile, path);
        var response = await _transport.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return new ProviderBalanceResult(
                false,
                BuildFailureMessage("查询账户余额失败", response, request.Uri.AbsolutePath, HasSavedApiKey()),
                Array.Empty<ProviderBalanceInfo>());
        }

        if (profile.Kind == ProviderKind.DeepSeek)
        {
            var data = JObject.Parse(response.Body)["balance_infos"] as JArray ?? new JArray();
            var balances = data
                .OfType<JObject>()
                .Select(item => new ProviderBalanceInfo(
                    item.Value<string>("currency") ?? string.Empty,
                    item.Value<string>("total_balance") ?? string.Empty,
                    item.Value<string>("granted_balance"),
                    item.Value<string>("topped_up_balance")))
                .Where(item => !string.IsNullOrWhiteSpace(item.Currency))
                .ToArray();
            return new ProviderBalanceResult(true, $"已获取 {balances.Length} 条余额信息。", balances);
        }

        var costRows = JObject.Parse(response.Body)["data"] as JArray ?? new JArray();
        var costs = costRows
            .OfType<JObject>()
            .SelectMany(bucket => (bucket["results"] as JArray ?? new JArray()).OfType<JObject>())
            .Select(item => new
            {
                Currency = item["amount"]?["currency"]?.Value<string>() ?? string.Empty,
                Value = item["amount"]?["value"]?.Value<decimal>() ?? 0m
            })
            .Where(item => !string.IsNullOrWhiteSpace(item.Currency))
            .GroupBy(item => item.Currency, StringComparer.OrdinalIgnoreCase)
            .Select(group => new ProviderBalanceInfo(
                group.Key.ToUpperInvariant(),
                group.Sum(item => item.Value).ToString("0.####", CultureInfo.InvariantCulture),
                null,
                null))
            .ToArray();

        return new ProviderBalanceResult(true, "已获取最近 7 天成本。OpenAI 成本接口通常需要管理员密钥。", costs);
    }

    private HttpTransportRequest CreateTestRequest(ProviderProfile profile)
    {
        var body = profile.Kind == ProviderKind.OpenAI
            ? CreateOpenAiResponsesTestBody(profile)
            : CreateChatCompletionsTestBody(profile);
        var uri = new Uri(
            new Uri(profile.BaseUrl.TrimEnd(new[] { '/' }) + "/"),
            profile.Endpoint.TrimStart(new[] { '/' }));
        var headers = new List<HttpHeaderEntry>();
        var apiKey = _apiKeyProvider();
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            headers.Add(new HttpHeaderEntry("Authorization", "Bearer " + apiKey));
        }

        headers.AddRange(OpenAICompatibleRequestOptions.GetCustomHeaders(profile));

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
        };
    }

    private static JObject CreateOpenAiResponsesTestBody(ProviderProfile profile)
    {
        return new JObject
        {
            ["model"] = profile.Model,
            ["instructions"] = "You are a connectivity test endpoint. Reply with a short answer only.",
            ["input"] = "Reply with ok.",
            ["text"] = new JObject { ["verbosity"] = "low" }
        };
    }

    private static JObject CreateChatCompletionsTestBody(ProviderProfile profile)
    {
        var body = new JObject
        {
            ["model"] = profile.Model,
            ["messages"] = new JArray
            {
                new JObject
                {
                    ["role"] = "system",
                    ["content"] = "You are a connectivity test endpoint."
                },
                new JObject
                {
                    ["role"] = "user",
                    ["content"] = "Reply with ok."
                }
            },
            ["stream"] = false
        };
        OpenAICompatibleRequestOptions.ApplyExtraBody(body, profile);
        return body;
    }

    private static string ParseTestReply(ProviderProfile profile, string json)
    {
        return profile.Kind == ProviderKind.OpenAI
            ? ProviderJsonParsers.ParseOpenAiResponsesText(json)
            : ProviderJsonParsers.ParseChatCompletionsText(json);
    }

    private static string TrimForMessage(string value)
    {
        var normalized = value.Replace("\r", " ").Replace("\n", " ").Trim();
        return normalized.Length <= 80 ? normalized : normalized[..80] + "...";
    }

    private HttpTransportRequest CreateGet(ProviderProfile profile, string path)
    {
        var uri = new Uri(
            new Uri(profile.BaseUrl.TrimEnd(new[] { '/' }) + "/"),
            path.TrimStart(new[] { '/' }));
        var headers = new List<HttpHeaderEntry>();
        var apiKey = _apiKeyProvider();
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            headers.Add(new HttpHeaderEntry("Authorization", "Bearer " + apiKey));
        }

        headers.AddRange(OpenAICompatibleRequestOptions.GetCustomHeaders(profile));

        return new HttpTransportRequest
        {
            Method = HttpTransportMethod.Get,
            Uri = uri,
            Headers = headers,
        };
    }

    private bool HasSavedApiKey()
    {
        return !string.IsNullOrWhiteSpace(_apiKeyProvider());
    }

    private static string BuildFailureMessage(
        string prefix,
        HttpTransportResponse response,
        string? path,
        bool apiKeyConfigured)
    {
        var pathText = string.IsNullOrWhiteSpace(path) ? string.Empty : $"，路径 {path}";
        if (response.Error != null)
        {
            return $"{prefix}（{response.Error.Message}{pathText}）。";
        }

        var status = response.StatusCode;
        var keyText = apiKeyConfigured ? "API Key 已保存" : "API Key 未保存";
        return status == 401
            ? $"{prefix}（HTTP 401{pathText}，{keyText}）。请确认 API Key 属于当前服务商。"
            : $"{prefix}（HTTP {status}{pathText}）。";
    }
}

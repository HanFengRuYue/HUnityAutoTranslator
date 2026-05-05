using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Globalization;
using HUnityAutoTranslator.Core.Configuration;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace HUnityAutoTranslator.Core.Providers;

public sealed class ProviderUtilityClient
{
    private readonly HttpClient _httpClient;
    private readonly Func<string?> _apiKeyProvider;

    public ProviderUtilityClient(HttpClient httpClient, Func<string?> apiKeyProvider)
    {
        _httpClient = httpClient;
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
        using var request = CreateGet(profile, path);
        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return new ProviderModelsResult(
                false,
                BuildFailureMessage("获取模型列表失败", response.StatusCode, request.RequestUri?.AbsolutePath, HasSavedApiKey()),
                Array.Empty<ProviderModelInfo>());
        }

        var data = JObject.Parse(json)["data"] as JArray ?? new JArray();
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

        using var request = CreateTestRequest(profile);
        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        var path = request.RequestUri?.AbsolutePath;
        if (!response.IsSuccessStatusCode)
        {
            return new ProviderTestResult(
                false,
                BuildFailureMessage("连接测试失败", response.StatusCode, path, HasSavedApiKey()));
        }

        var reply = ParseTestReply(profile, json);
        if (string.IsNullOrWhiteSpace(reply))
        {
            return new ProviderTestResult(false, $"连接测试失败：接口已响应，但没有返回模型文本（路径 {path ?? profile.Endpoint}）。");
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
        using var request = CreateGet(profile, path);
        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return new ProviderBalanceResult(
                false,
                BuildFailureMessage("查询账户余额失败", response.StatusCode, request.RequestUri?.AbsolutePath, HasSavedApiKey()),
                Array.Empty<ProviderBalanceInfo>());
        }

        if (profile.Kind == ProviderKind.DeepSeek)
        {
            var data = JObject.Parse(json)["balance_infos"] as JArray ?? new JArray();
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

        var costRows = JObject.Parse(json)["data"] as JArray ?? new JArray();
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

    private HttpRequestMessage CreateTestRequest(ProviderProfile profile)
    {
        var body = profile.Kind == ProviderKind.OpenAI
            ? CreateOpenAiResponsesTestBody(profile)
            : CreateChatCompletionsTestBody(profile);
        var request = new HttpRequestMessage(
            HttpMethod.Post,
            new Uri(new Uri(profile.BaseUrl.TrimEnd(new[] { '/' }) + "/"), profile.Endpoint.TrimStart(new[] { '/' })))
        {
            Content = new StringContent(JsonConvert.SerializeObject(body, Formatting.None), Encoding.UTF8, "application/json")
        };

        var apiKey = _apiKeyProvider();
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        }

        OpenAICompatibleRequestOptions.ApplyCustomHeaders(request, profile);
        return request;
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

    private HttpRequestMessage CreateGet(ProviderProfile profile, string path)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, new Uri(new Uri(profile.BaseUrl.TrimEnd(new[] { '/' }) + "/"), path.TrimStart(new[] { '/' })));
        var apiKey = _apiKeyProvider();
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        }

        OpenAICompatibleRequestOptions.ApplyCustomHeaders(request, profile);

        return request;
    }

    private bool HasSavedApiKey()
    {
        return !string.IsNullOrWhiteSpace(_apiKeyProvider());
    }

    private static string BuildFailureMessage(
        string prefix,
        System.Net.HttpStatusCode statusCode,
        string? path,
        bool apiKeyConfigured)
    {
        var status = (int)statusCode;
        var pathText = string.IsNullOrWhiteSpace(path) ? string.Empty : $"，路径 {path}";
        var keyText = apiKeyConfigured ? "API Key 已保存" : "API Key 未保存";
        return status == 401
            ? $"{prefix}（HTTP 401{pathText}，{keyText}）。请确认 API Key 属于当前服务商。"
            : $"{prefix}（HTTP {status}{pathText}）。";
    }
}

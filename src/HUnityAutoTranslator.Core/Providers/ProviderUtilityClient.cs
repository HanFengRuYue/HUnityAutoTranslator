using System.Net.Http;
using System.Net.Http.Headers;
using System.Globalization;
using HUnityAutoTranslator.Core.Configuration;
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
        var path = profile.Kind == ProviderKind.DeepSeek ? "/models" : "/v1/models";
        using var request = CreateGet(profile, path);
        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return new ProviderModelsResult(false, BuildFailureMessage("获取模型列表失败", response.StatusCode), Array.Empty<ProviderModelInfo>());
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

    public async Task<ProviderBalanceResult> FetchBalanceAsync(ProviderProfile profile, CancellationToken cancellationToken)
    {
        if (profile.Kind == ProviderKind.LlamaCpp)
        {
            return new ProviderBalanceResult(true, "本地模型不适用余额或成本查询。", Array.Empty<ProviderBalanceInfo>());
        }

        var path = profile.Kind == ProviderKind.DeepSeek
            ? "/user/balance"
            : $"/v1/organization/costs?start_time={DateTimeOffset.UtcNow.AddDays(-7).ToUnixTimeSeconds()}&limit=7";
        using var request = CreateGet(profile, path);
        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return new ProviderBalanceResult(false, BuildFailureMessage("查询余额/成本失败", response.StatusCode), Array.Empty<ProviderBalanceInfo>());
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

    private HttpRequestMessage CreateGet(ProviderProfile profile, string path)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, new Uri(new Uri(profile.BaseUrl.TrimEnd('/') + "/"), path.TrimStart('/')));
        var apiKey = _apiKeyProvider();
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        }

        return request;
    }

    private static string BuildFailureMessage(string prefix, System.Net.HttpStatusCode statusCode)
    {
        var status = (int)statusCode;
        return status == 401
            ? $"{prefix}（HTTP 401）。请确认 API Key 已保存且属于当前服务商。"
            : $"{prefix}（HTTP {status}）。";
    }
}

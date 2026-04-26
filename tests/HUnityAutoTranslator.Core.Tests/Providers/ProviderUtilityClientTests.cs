using System.Net;
using System.Text;
using FluentAssertions;
using HUnityAutoTranslator.Core.Configuration;
using HUnityAutoTranslator.Core.Providers;

namespace HUnityAutoTranslator.Core.Tests.Providers;

public sealed class ProviderUtilityClientTests
{
    [Fact]
    public async Task FetchModelsAsync_parses_openai_compatible_model_list()
    {
        var handler = new CaptureHandler("""{"object":"list","data":[{"id":"gpt-5.5","object":"model","owned_by":"openai"}]}""");
        var client = new ProviderUtilityClient(new HttpClient(handler), () => "key");

        var result = await client.FetchModelsAsync(ProviderProfile.DefaultOpenAi(), CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        result.Models.Should().ContainSingle(model => model.Id == "gpt-5.5" && model.OwnedBy == "openai");
        handler.RequestPath.Should().Be("/v1/models");
    }

    [Fact]
    public async Task FetchBalanceAsync_parses_deepseek_balance()
    {
        var handler = new CaptureHandler("""{"is_available":true,"balance_infos":[{"currency":"CNY","total_balance":"110.00","granted_balance":"10.00","topped_up_balance":"100.00"}]}""");
        var client = new ProviderUtilityClient(new HttpClient(handler), () => "key");

        var result = await client.FetchBalanceAsync(ProviderProfile.DefaultDeepSeek(), CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        result.Balances.Should().ContainSingle(balance => balance.Currency == "CNY" && balance.TotalBalance == "110.00");
        handler.RequestPath.Should().Be("/user/balance");
    }

    [Fact]
    public async Task FetchBalanceAsync_queries_openai_organization_costs_with_start_time()
    {
        var handler = new CaptureHandler("""
{"object":"page","data":[{"object":"bucket","results":[{"object":"organization.costs.result","amount":{"currency":"usd","value":0.06}}]}],"has_more":false}
""");
        var client = new ProviderUtilityClient(new HttpClient(handler), () => "admin-key");

        var result = await client.FetchBalanceAsync(ProviderProfile.DefaultOpenAi(), CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        result.Message.Should().Contain("管理员密钥");
        result.Balances.Should().ContainSingle(balance => balance.Currency == "USD" && balance.TotalBalance == "0.06");
        handler.RequestPath.Should().Be("/v1/organization/costs");
        handler.RequestQuery.Should().Contain("start_time=");
        handler.RequestQuery.Should().Contain("limit=7");
    }

    private sealed class CaptureHandler : HttpMessageHandler
    {
        private readonly string _json;

        public CaptureHandler(string json)
        {
            _json = json;
        }

        public string RequestPath { get; private set; } = string.Empty;
        public string RequestQuery { get; private set; } = string.Empty;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestPath = request.RequestUri?.AbsolutePath ?? string.Empty;
            RequestQuery = request.RequestUri?.Query ?? string.Empty;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_json, Encoding.UTF8, "application/json")
            });
        }
    }
}

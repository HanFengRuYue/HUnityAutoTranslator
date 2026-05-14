using FluentAssertions;
using HUnityAutoTranslator.Core.Configuration;
using HUnityAutoTranslator.Core.Http;
using HUnityAutoTranslator.Core.Providers;
using HUnityAutoTranslator.Core.Tests.Http;

namespace HUnityAutoTranslator.Core.Tests.Providers;

public sealed class ProviderUtilityClientTests
{
    [Fact]
    public async Task FetchModelsAsync_parses_openai_compatible_model_list()
    {
        var transport = new FakeHttpTransport(_ => FakeHttpTransport.Json(
            """{"object":"list","data":[{"id":"gpt-5.5","object":"model","owned_by":"openai"}]}"""));
        var client = new ProviderUtilityClient(transport, () => "key");

        var result = await client.FetchModelsAsync(ProviderProfile.DefaultOpenAi(), CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        result.Models.Should().ContainSingle(model => model.Id == "gpt-5.5" && model.OwnedBy == "openai");
        transport.LastPath.Should().Be("/v1/models");
    }

    [Fact]
    public async Task FetchModelsAsync_reports_api_key_hint_for_unauthorized_response()
    {
        var transport = new FakeHttpTransport(_ => FakeHttpTransport.Json(
            """{"error":{"message":"Unauthorized"}}""", 401));
        var client = new ProviderUtilityClient(transport, () => null);

        var result = await client.FetchModelsAsync(ProviderProfile.DefaultDeepSeek(), CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        result.Message.Should().Contain("401");
        result.Message.Should().Contain("API Key");
        result.Models.Should().BeEmpty();
    }

    [Fact]
    public async Task FetchModelsAsync_reports_network_error_message()
    {
        var transport = new FakeHttpTransport(_ => FakeHttpTransport.NetworkError("无法连接到服务器。"));
        var client = new ProviderUtilityClient(transport, () => "key");

        var result = await client.FetchModelsAsync(ProviderProfile.DefaultOpenAi(), CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        result.Message.Should().Contain("无法连接到服务器");
        result.Models.Should().BeEmpty();
    }

    [Fact]
    public async Task FetchBalanceAsync_parses_deepseek_balance()
    {
        var transport = new FakeHttpTransport(_ => FakeHttpTransport.Json(
            """{"is_available":true,"balance_infos":[{"currency":"CNY","total_balance":"110.00","granted_balance":"10.00","topped_up_balance":"100.00"}]}"""));
        var client = new ProviderUtilityClient(transport, () => "key");

        var result = await client.FetchBalanceAsync(ProviderProfile.DefaultDeepSeek(), CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        result.Balances.Should().ContainSingle(balance => balance.Currency == "CNY" && balance.TotalBalance == "110.00");
        transport.LastPath.Should().Be("/user/balance");
    }

    [Fact]
    public async Task FetchBalanceAsync_queries_openai_organization_costs_with_start_time()
    {
        var transport = new FakeHttpTransport(_ => FakeHttpTransport.Json("""
{"object":"page","data":[{"object":"bucket","results":[{"object":"organization.costs.result","amount":{"currency":"usd","value":0.06}}]}],"has_more":false}
"""));
        var client = new ProviderUtilityClient(transport, () => "admin-key");

        var result = await client.FetchBalanceAsync(ProviderProfile.DefaultOpenAi(), CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        result.Message.Should().Contain("管理员密钥");
        result.Balances.Should().ContainSingle(balance => balance.Currency == "USD" && balance.TotalBalance == "0.06");
        transport.LastPath.Should().Be("/v1/organization/costs");
        transport.LastQuery.Should().Contain("start_time=");
        transport.LastQuery.Should().Contain("limit=7");
    }

    [Fact]
    public async Task FetchModelsAsync_applies_openai_compatible_custom_headers_without_overriding_authorization()
    {
        var transport = new FakeHttpTransport(_ => FakeHttpTransport.Json(
            """{"object":"list","data":[{"id":"proxy-model","owned_by":"proxy"}]}"""));
        var client = new ProviderUtilityClient(transport, () => "key");
        var profile = new ProviderProfile(
            ProviderKind.OpenAICompatible,
            "http://127.0.0.1:9000",
            "/v1/chat/completions",
            "proxy-model",
            true,
            """
            X-App-Title: HUnity
            Authorization: Bearer ignored
            Content-Type: text/plain
            """);

        var result = await client.FetchModelsAsync(profile, CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        result.Models.Should().ContainSingle(model => model.Id == "proxy-model");
        transport.LastPath.Should().Be("/v1/models");
        transport.AuthorizationHeader.Should().Be("Bearer key");
        transport.Header("X-App-Title").Should().Be("HUnity");
        transport.Header("Content-Type").Should().BeNull();
    }

    [Fact]
    public async Task TestConnectionAsync_posts_openai_compatible_chat_endpoint_with_auth_headers_and_extra_body()
    {
        var transport = new FakeHttpTransport(_ => FakeHttpTransport.Json(
            """{"choices":[{"message":{"content":"ok"}}]}"""));
        var client = new ProviderUtilityClient(transport, () => "key");
        var profile = new ProviderProfile(
            ProviderKind.OpenAICompatible,
            "http://127.0.0.1:9000",
            "/v1/chat/completions",
            "proxy-model",
            true,
            """
            X-App-Title: HUnity
            Authorization: Bearer ignored
            Content-Type: text/plain
            """,
            """{"stream":true,"metadata":{"source":"test"}}""");

        var result = await client.TestConnectionAsync(profile, CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        result.Message.Should().Contain("ok");
        transport.LastMethod.Should().Be(HttpTransportMethod.Post);
        transport.LastPath.Should().Be("/v1/chat/completions");
        transport.AuthorizationHeader.Should().Be("Bearer key");
        transport.Header("X-App-Title").Should().Be("HUnity");
        transport.Header("Content-Type").Should().BeNull();
        transport.LastStringBodyContentType.Should().Be("application/json");
        var body = Newtonsoft.Json.Linq.JObject.Parse(transport.LastStringBody);
        body.Value<string>("model").Should().Be("proxy-model");
        body["messages"]!.Should().HaveCount(2);
        body.Value<bool>("stream").Should().BeFalse();
        body["metadata"]!.Value<string>("source").Should().Be("test");
    }

    [Fact]
    public async Task TestConnectionAsync_posts_deepseek_chat_endpoint_instead_of_fetching_models()
    {
        var transport = new FakeHttpTransport(_ => FakeHttpTransport.Json(
            """{"choices":[{"message":{"content":"ok"}}]}"""));
        var client = new ProviderUtilityClient(transport, () => "key");

        var result = await client.TestConnectionAsync(ProviderProfile.DefaultDeepSeek(), CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        result.Message.Should().Contain("ok");
        transport.LastMethod.Should().Be(HttpTransportMethod.Post);
        transport.LastPath.Should().Be("/chat/completions");
        var body = Newtonsoft.Json.Linq.JObject.Parse(transport.LastStringBody);
        body.Value<string>("model").Should().Be(ProviderProfile.DefaultDeepSeek().Model);
        body["messages"]!.Should().HaveCount(2);
    }

    [Fact]
    public async Task TestConnectionAsync_fails_when_generation_response_has_no_text()
    {
        var transport = new FakeHttpTransport(_ => FakeHttpTransport.Json(
            """{"choices":[{"message":{"content":""}}]}"""));
        var client = new ProviderUtilityClient(transport, () => "key");

        var result = await client.TestConnectionAsync(ProviderProfile.DefaultDeepSeek(), CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        result.Message.Should().Contain("没有返回模型文本");
    }

    [Fact]
    public async Task TestConnectionAsync_reports_path_and_saved_key_hint_for_openai_compatible_unauthorized_response()
    {
        var transport = new FakeHttpTransport(_ => FakeHttpTransport.Json(
            """{"error":{"message":"Unauthorized"}}""", 401));
        var client = new ProviderUtilityClient(transport, () => "key");
        var profile = new ProviderProfile(
            ProviderKind.OpenAICompatible,
            "http://127.0.0.1:9000",
            "/v1/chat/completions",
            "proxy-model",
            true);

        var result = await client.TestConnectionAsync(profile, CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        result.Message.Should().Contain("连接测试失败");
        result.Message.Should().Contain("401");
        result.Message.Should().Contain("/v1/chat/completions");
        result.Message.Should().Contain("API Key 已保存");
    }

    [Fact]
    public async Task FetchModelsAsync_uses_preset_models_path_when_preset_id_is_supplied()
    {
        var transport = new FakeHttpTransport(_ => FakeHttpTransport.Json(
            """{"object":"list","data":[{"id":"Qwen/Qwen3-8B","owned_by":"qwen"}]}"""));
        var client = new ProviderUtilityClient(transport, () => "key");

        var result = await client.FetchModelsAsync(PresetProfile("siliconflow"), CancellationToken.None, "siliconflow");

        result.Succeeded.Should().BeTrue();
        result.Models.Should().ContainSingle(model => model.Id == "Qwen/Qwen3-8B");
        transport.LastPath.Should().Be("/v1/models");
    }

    [Fact]
    public async Task FetchModelsAsync_reports_unsupported_when_preset_has_no_models_endpoint()
    {
        var client = new ProviderUtilityClient(FakeHttpTransport.Throwing(), () => "key");

        var result = await client.FetchModelsAsync(PresetProfile("zhipu"), CancellationToken.None, "zhipu");

        result.Succeeded.Should().BeFalse();
        result.Message.Should().Contain("未提供模型列表接口");
        result.Models.Should().BeEmpty();
    }

    [Fact]
    public async Task FetchModelsAsync_falls_back_to_provider_kind_when_preset_id_is_unknown()
    {
        var transport = new FakeHttpTransport(_ => FakeHttpTransport.Json(
            """{"object":"list","data":[{"id":"gpt-5.5","owned_by":"openai"}]}"""));
        var client = new ProviderUtilityClient(transport, () => "key");

        var result = await client.FetchModelsAsync(ProviderProfile.DefaultOpenAi(), CancellationToken.None, "totally-unknown");

        result.Succeeded.Should().BeTrue();
        transport.LastPath.Should().Be("/v1/models");
    }

    [Fact]
    public async Task FetchBalanceAsync_uses_preset_balance_adapter_for_siliconflow()
    {
        var transport = new FakeHttpTransport(_ => FakeHttpTransport.Json(
            """{"code":20000,"data":{"balance":"0.88","chargeBalance":"88.00","totalBalance":"88.88"}}"""));
        var client = new ProviderUtilityClient(transport, () => "key");

        var result = await client.FetchBalanceAsync(PresetProfile("siliconflow"), CancellationToken.None, "siliconflow");

        result.Succeeded.Should().BeTrue();
        transport.LastPath.Should().Be("/v1/user/info");
        result.Balances.Should().ContainSingle(balance =>
            balance.Currency == "CNY" && balance.TotalBalance == "88.88" && balance.GrantedBalance == "0.88");
    }

    [Fact]
    public async Task FetchBalanceAsync_uses_preset_balance_adapter_for_openrouter()
    {
        var transport = new FakeHttpTransport(_ => FakeHttpTransport.Json(
            """{"data":{"total_credits":10,"total_usage":3.5}}"""));
        var client = new ProviderUtilityClient(transport, () => "key");

        var result = await client.FetchBalanceAsync(PresetProfile("openrouter"), CancellationToken.None, "openrouter");

        result.Succeeded.Should().BeTrue();
        transport.LastPath.Should().Be("/api/v1/credits");
        result.Balances.Should().ContainSingle(balance => balance.Currency == "USD" && balance.TotalBalance == "6.5");
    }

    [Fact]
    public async Task FetchBalanceAsync_reports_console_fallback_when_preset_has_no_balance_endpoint()
    {
        var client = new ProviderUtilityClient(FakeHttpTransport.Throwing(), () => "key");

        var result = await client.FetchBalanceAsync(PresetProfile("groq"), CancellationToken.None, "groq");

        result.Succeeded.Should().BeFalse();
        result.Message.Should().Contain("未提供余额查询接口");
        result.Message.Should().Contain("控制台");
        result.Balances.Should().BeEmpty();
    }

    private static ProviderProfile PresetProfile(string presetId)
    {
        var preset = ProviderPresetCatalog.Resolve(presetId)!;
        return new ProviderProfile(preset.Kind, preset.BaseUrl, preset.Endpoint, preset.DefaultModel, true);
    }
}

using System.Globalization;
using HUnityAutoTranslator.Core.Configuration;
using Newtonsoft.Json.Linq;

namespace HUnityAutoTranslator.Core.Providers;

/// <summary>
/// 内置 AI 翻译服务商预设目录。多数第三方服务按官方 OpenAI 兼容接口接入，复用
/// <see cref="ChatCompletionsProvider"/>；OpenAI 与 DeepSeek 使用各自原生的
/// <see cref="ProviderKind"/>（分别走 Responses API 与 DeepSeek Chat Completions）。
///
/// 注意：Base URL / Endpoint / 默认模型 / 模型列表路径 / 余额接口均为按官方文档核实过的值，
/// 但服务商接口和模型名会随时间变动——发现失效时直接更新本文件即可，无需改动其它代码。
///
/// 列表顺序即控制面板「快速预设」下拉的显示顺序。
/// </summary>
public static class ProviderPresetCatalog
{
    public static IReadOnlyList<ProviderPreset> All { get; } = new[]
    {
        new ProviderPreset(
            Id: "deepseek",
            DisplayName: "DeepSeek 深度求索",
            Kind: ProviderKind.DeepSeek,
            BaseUrl: "https://api.deepseek.com",
            Endpoint: "/chat/completions",
            DefaultModel: "deepseek-v4-flash",
            SuggestedModels: new[] { "deepseek-v4-flash", "deepseek-chat", "deepseek-reasoner" },
            RequestsPerMinute: 15000,
            ModelsPath: "/models",
            BalanceQuery: null,
            ConsoleUrl: "https://platform.deepseek.com",
            DocsUrl: "https://api-docs.deepseek.com",
            Notes: "DeepSeek 官方接口，走原生 Chat Completions。余额请在控制台查看。"),
        new ProviderPreset(
            Id: "siliconflow",
            DisplayName: "硅基流动 SiliconFlow",
            Kind: ProviderKind.OpenAICompatible,
            BaseUrl: "https://api.siliconflow.cn/v1",
            Endpoint: "/chat/completions",
            DefaultModel: "Qwen/Qwen3-8B",
            SuggestedModels: new[] { "Qwen/Qwen3-8B", "deepseek-ai/DeepSeek-V3", "zai-org/GLM-4.6" },
            RequestsPerMinute: 1000,
            ModelsPath: "/models",
            BalanceQuery: new ProviderBalanceQuery("/user/info", ParseSiliconFlowBalance),
            ConsoleUrl: "https://cloud.siliconflow.cn",
            DocsUrl: "https://docs.siliconflow.cn",
            Notes: "聚合多家开源模型；模型名带组织前缀，如 Qwen/Qwen3-8B。"),
        new ProviderPreset(
            Id: "zhipu",
            DisplayName: "智谱 GLM",
            Kind: ProviderKind.OpenAICompatible,
            BaseUrl: "https://open.bigmodel.cn/api/paas/v4",
            Endpoint: "/chat/completions",
            DefaultModel: "glm-4-flash",
            SuggestedModels: new[] { "glm-4-flash", "glm-4-air", "glm-4-plus" },
            RequestsPerMinute: 1000,
            ModelsPath: null,
            BalanceQuery: null,
            ConsoleUrl: "https://open.bigmodel.cn/usercenter/proj-mgmt/apikeys",
            DocsUrl: "https://docs.bigmodel.cn",
            Notes: "OpenAI 兼容路径为 /api/paas/v4，不要再追加 /v1。余额请在控制台查看。"),
        new ProviderPreset(
            Id: "moonshot",
            DisplayName: "月之暗面 Kimi",
            Kind: ProviderKind.OpenAICompatible,
            BaseUrl: "https://api.moonshot.cn/v1",
            Endpoint: "/chat/completions",
            DefaultModel: "moonshot-v1-8k",
            SuggestedModels: new[] { "moonshot-v1-8k", "moonshot-v1-32k", "moonshot-v1-128k", "kimi-latest" },
            RequestsPerMinute: 500,
            ModelsPath: "/models",
            BalanceQuery: new ProviderBalanceQuery("/users/me/balance", ParseMoonshotBalance),
            ConsoleUrl: "https://platform.moonshot.cn/console",
            DocsUrl: "https://platform.moonshot.cn/docs",
            Notes: "国内站与国际站账号独立；本预设对应国内站 api.moonshot.cn。"),
        new ProviderPreset(
            Id: "dashscope",
            DisplayName: "阿里通义千问 / 百炼",
            Kind: ProviderKind.OpenAICompatible,
            BaseUrl: "https://dashscope.aliyuncs.com/compatible-mode/v1",
            Endpoint: "/chat/completions",
            DefaultModel: "qwen-plus",
            SuggestedModels: new[] { "qwen-plus", "qwen-turbo", "qwen-flash", "qwen-max" },
            RequestsPerMinute: 1000,
            ModelsPath: "/models",
            BalanceQuery: null,
            ConsoleUrl: "https://bailian.console.aliyun.com",
            DocsUrl: "https://help.aliyun.com/zh/model-studio/",
            Notes: "使用百炼 OpenAI 兼容模式；模型名如 qwen-plus、qwen-max。余额请在控制台查看。"),
        new ProviderPreset(
            Id: "volcengine",
            DisplayName: "火山方舟 / 豆包",
            Kind: ProviderKind.OpenAICompatible,
            BaseUrl: "https://ark.cn-beijing.volces.com/api/v3",
            Endpoint: "/chat/completions",
            DefaultModel: "doubao-seed-1.6",
            SuggestedModels: new[] { "doubao-seed-1.6", "doubao-1.5-pro-32k" },
            RequestsPerMinute: 1000,
            ModelsPath: null,
            BalanceQuery: null,
            ConsoleUrl: "https://console.volcengine.com/ark",
            DocsUrl: "https://www.volcengine.com/docs/82379",
            Notes: "模型字段可填模型名或接入点 ID；如遇 404 请在方舟控制台开通对应模型。余额请在控制台查看。"),
        new ProviderPreset(
            Id: "openai",
            DisplayName: "OpenAI GPT",
            Kind: ProviderKind.OpenAI,
            BaseUrl: "https://api.openai.com",
            Endpoint: "/v1/responses",
            DefaultModel: "gpt-5.5",
            SuggestedModels: new[] { "gpt-5.5", "gpt-5.4-mini" },
            RequestsPerMinute: 500,
            ModelsPath: "/v1/models",
            BalanceQuery: null,
            ConsoleUrl: "https://platform.openai.com",
            DocsUrl: "https://platform.openai.com/docs",
            Notes: "OpenAI 官方接口，走原生 Responses API。余额请在控制台查看（成本接口通常需要管理员密钥）。"),
        new ProviderPreset(
            Id: "openrouter",
            DisplayName: "OpenRouter",
            Kind: ProviderKind.OpenAICompatible,
            BaseUrl: "https://openrouter.ai/api/v1",
            Endpoint: "/chat/completions",
            DefaultModel: "openai/gpt-4o-mini",
            SuggestedModels: new[] { "openai/gpt-4o-mini", "google/gemini-2.5-flash", "deepseek/deepseek-chat", "anthropic/claude-3.5-haiku" },
            RequestsPerMinute: 500,
            ModelsPath: "/models",
            BalanceQuery: new ProviderBalanceQuery("/credits", ParseOpenRouterBalance),
            ConsoleUrl: "https://openrouter.ai/settings/credits",
            DocsUrl: "https://openrouter.ai/docs",
            Notes: "模型名带提供方前缀，如 openai/gpt-4o-mini。余额查询需密钥具备读取额度权限。"),
        new ProviderPreset(
            Id: "groq",
            DisplayName: "Groq",
            Kind: ProviderKind.OpenAICompatible,
            BaseUrl: "https://api.groq.com/openai/v1",
            Endpoint: "/chat/completions",
            DefaultModel: "llama-3.3-70b-versatile",
            SuggestedModels: new[] { "llama-3.3-70b-versatile", "llama-3.1-8b-instant" },
            RequestsPerMinute: 100,
            ModelsPath: "/models",
            BalanceQuery: null,
            ConsoleUrl: "https://console.groq.com",
            DocsUrl: "https://console.groq.com/docs",
            Notes: "推理速度快，但免费档每分钟请求数较低；如遇 429 请下调 RPM。余额请在控制台查看。"),
        new ProviderPreset(
            Id: "xai",
            DisplayName: "xAI Grok",
            Kind: ProviderKind.OpenAICompatible,
            BaseUrl: "https://api.x.ai/v1",
            Endpoint: "/chat/completions",
            DefaultModel: "grok-3-mini",
            SuggestedModels: new[] { "grok-3-mini", "grok-3", "grok-4" },
            RequestsPerMinute: 500,
            ModelsPath: "/models",
            BalanceQuery: null,
            ConsoleUrl: "https://console.x.ai",
            DocsUrl: "https://docs.x.ai",
            Notes: "完全兼容 OpenAI Chat Completions。余额请在控制台查看。"),
        new ProviderPreset(
            Id: "gemini",
            DisplayName: "Google Gemini",
            Kind: ProviderKind.OpenAICompatible,
            BaseUrl: "https://generativelanguage.googleapis.com/v1beta/openai",
            Endpoint: "/chat/completions",
            DefaultModel: "gemini-2.5-flash",
            SuggestedModels: new[] { "gemini-2.5-flash", "gemini-2.5-pro", "gemini-2.0-flash" },
            RequestsPerMinute: 500,
            ModelsPath: "/models",
            BalanceQuery: null,
            ConsoleUrl: "https://aistudio.google.com/apikey",
            DocsUrl: "https://ai.google.dev/gemini-api/docs/openai",
            Notes: "使用 Gemini 的 OpenAI 兼容端点（v1beta/openai）。余额请在控制台查看。"),
    };

    /// <summary>预设 Id 是否存在于目录中（大小写不敏感）。</summary>
    public static bool IsKnown(string? id)
    {
        return Resolve(id) != null;
    }

    /// <summary>按 Id 解析预设；Id 为空或未知时返回 null。</summary>
    public static ProviderPreset? Resolve(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return null;
        }

        var trimmed = id.Trim();
        return All.FirstOrDefault(preset => string.Equals(preset.Id, trimmed, StringComparison.OrdinalIgnoreCase));
    }

    // —— 余额响应解析器 ——
    // 各家余额接口 JSON 形状差异极大，每家一个小静态方法，紧挨目录维护。

    private static IReadOnlyList<ProviderBalanceInfo> ParseSiliconFlowBalance(string body)
    {
        // { "code": 20000, "data": { "balance": "0.88", "chargeBalance": "88.00", "totalBalance": "88.88" } }
        if (JObject.Parse(body)["data"] is not JObject data)
        {
            return Array.Empty<ProviderBalanceInfo>();
        }

        var total = data.Value<string>("totalBalance") ?? data.Value<string>("balance") ?? string.Empty;
        return new[]
        {
            new ProviderBalanceInfo("CNY", total, data.Value<string>("balance"), data.Value<string>("chargeBalance"))
        };
    }

    private static IReadOnlyList<ProviderBalanceInfo> ParseMoonshotBalance(string body)
    {
        // { "code": 0, "data": { "available_balance": 49.58, "voucher_balance": 46.58, "cash_balance": 3.0 } }
        if (JObject.Parse(body)["data"] is not JObject data)
        {
            return Array.Empty<ProviderBalanceInfo>();
        }

        return new[]
        {
            new ProviderBalanceInfo(
                "CNY",
                FormatAmount(data["available_balance"]),
                FormatAmount(data["voucher_balance"]),
                FormatAmount(data["cash_balance"]))
        };
    }

    private static IReadOnlyList<ProviderBalanceInfo> ParseOpenRouterBalance(string body)
    {
        // { "data": { "total_credits": 10.0, "total_usage": 3.5 } }
        if (JObject.Parse(body)["data"] is not JObject data)
        {
            return Array.Empty<ProviderBalanceInfo>();
        }

        var totalCredits = data.Value<decimal?>("total_credits") ?? 0m;
        var totalUsage = data.Value<decimal?>("total_usage") ?? 0m;
        var remaining = totalCredits - totalUsage;
        return new[]
        {
            new ProviderBalanceInfo(
                "USD",
                remaining.ToString("0.####", CultureInfo.InvariantCulture),
                totalCredits.ToString("0.####", CultureInfo.InvariantCulture),
                totalUsage.ToString("0.####", CultureInfo.InvariantCulture))
        };
    }

    /// <summary>把可能是数字或字符串的金额 JSON 值统一格式化成字符串。</summary>
    private static string FormatAmount(JToken? token)
    {
        if (token == null || token.Type == JTokenType.Null)
        {
            return string.Empty;
        }

        return token.Type is JTokenType.Float or JTokenType.Integer
            ? token.Value<decimal>().ToString("0.####", CultureInfo.InvariantCulture)
            : token.Value<string>() ?? string.Empty;
    }
}

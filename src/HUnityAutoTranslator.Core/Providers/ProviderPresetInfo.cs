using HUnityAutoTranslator.Core.Configuration;

namespace HUnityAutoTranslator.Core.Providers;

/// <summary>
/// <see cref="ProviderPreset"/> 的可序列化投影：去掉解析委托，余额能力拍平成 <see cref="SupportsBalanceQuery"/>。
/// 通过 <c>GET /api/provider-presets</c> 下发给控制面板，纯静态描述、不含任何密钥。
/// </summary>
public sealed record ProviderPresetInfo(
    string Id,
    string DisplayName,
    ProviderKind Kind,
    string BaseUrl,
    string Endpoint,
    string DefaultModel,
    IReadOnlyList<string> SuggestedModels,
    int RequestsPerMinute,
    bool SupportsModelList,
    bool SupportsBalanceQuery,
    string ConsoleUrl,
    string DocsUrl,
    string Notes);

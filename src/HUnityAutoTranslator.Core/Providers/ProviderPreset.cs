using HUnityAutoTranslator.Core.Configuration;

namespace HUnityAutoTranslator.Core.Providers;

/// <summary>
/// 余额查询适配。<see cref="Path"/> 是相对服务商 Base URL 的 GET 路径，
/// <see cref="Parse"/> 把响应体 JSON 解析成余额条目列表。
/// 没有公开余额接口的服务商应把整个 <see cref="ProviderBalanceQuery"/> 设为 null，
/// 改用 <see cref="ProviderPreset.ConsoleUrl"/> 让界面引导用户前往控制台。
/// </summary>
public sealed record ProviderBalanceQuery(
    string Path,
    Func<string, IReadOnlyList<ProviderBalanceInfo>> Parse);

/// <summary>
/// 一个内置 AI 翻译服务商预设：按官方文档核实过的接入信息，外加模型列表 / 余额查询的专属适配。
/// 因为含解析委托，必须留在 Core 内部；对外（控制面板）通过 <see cref="ProviderPresetInfo"/> 暴露。
/// </summary>
public sealed record ProviderPreset(
    string Id,
    string DisplayName,
    ProviderKind Kind,
    string BaseUrl,
    string Endpoint,
    string DefaultModel,
    IReadOnlyList<string> SuggestedModels,
    int RequestsPerMinute,
    string? ModelsPath,
    ProviderBalanceQuery? BalanceQuery,
    string ConsoleUrl,
    string DocsUrl,
    string Notes)
{
    /// <summary>是否提供可用的模型列表接口（决定「获取模型」是否可用）。</summary>
    public bool SupportsModelList => !string.IsNullOrWhiteSpace(ModelsPath);

    /// <summary>是否提供可用的余额查询接口（决定界面显示「查询余额」还是「前往控制台」）。</summary>
    public bool SupportsBalanceQuery => BalanceQuery != null;

    /// <summary>投影成可序列化、可下发给控制面板的 DTO（去掉解析委托）。</summary>
    public ProviderPresetInfo ToInfo()
    {
        return new ProviderPresetInfo(
            Id,
            DisplayName,
            Kind,
            BaseUrl,
            Endpoint,
            DefaultModel,
            SuggestedModels,
            RequestsPerMinute,
            SupportsModelList,
            SupportsBalanceQuery,
            ConsoleUrl,
            DocsUrl,
            Notes);
    }
}

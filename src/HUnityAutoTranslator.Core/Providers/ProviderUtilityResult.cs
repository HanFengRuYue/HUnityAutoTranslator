namespace HUnityAutoTranslator.Core.Providers;

public sealed record ProviderModelInfo(string Id, string? OwnedBy);

public sealed record ProviderBalanceInfo(
    string Currency,
    string TotalBalance,
    string? GrantedBalance,
    string? ToppedUpBalance);

public sealed record ProviderModelsResult(
    bool Succeeded,
    string Message,
    IReadOnlyList<ProviderModelInfo> Models);

public sealed record ProviderTestResult(
    bool Succeeded,
    string Message);

public sealed record ProviderBalanceResult(
    bool Succeeded,
    string Message,
    IReadOnlyList<ProviderBalanceInfo> Balances);

using Newtonsoft.Json;

namespace HUnityAutoTranslator.Core.Queueing;

public sealed record TranslationRequestDebugSnapshot(
    string Phase,
    string PromptPolicyVersion,
    string TargetLanguage,
    string? GameTitle,
    string ProviderKind,
    string ProviderModel,
    int BatchSize,
    int ContextExampleCount,
    int GlossaryTermCount,
    bool ItemContextsIncluded,
    bool QualityRulesEnabled,
    string? RepairReason,
    IReadOnlyList<TranslationRequestDebugItem> Items)
{
    public string ToLogLine()
    {
        return JsonConvert.SerializeObject(this, Formatting.None);
    }
}

public sealed record TranslationRequestDebugItem(
    int TextIndex,
    string SourceText,
    string? SceneName,
    string? ComponentHierarchy,
    string? OptionContainerHierarchy,
    string? SettingGroupHierarchy,
    string? ComponentType,
    IReadOnlyList<string> Hints,
    string? CandidateTranslation = null,
    string? QualityFailureReason = null,
    int QualityRetryCount = 0);

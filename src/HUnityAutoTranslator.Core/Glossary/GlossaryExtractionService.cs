using HUnityAutoTranslator.Core.Caching;
using HUnityAutoTranslator.Core.Configuration;
using HUnityAutoTranslator.Core.Prompts;
using HUnityAutoTranslator.Core.Providers;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace HUnityAutoTranslator.Core.Glossary;

public static class GlossaryExtractionService
{
    // 每轮从水位线之后拉取的最大行数。
    private const int BatchRowLimit = 400;
    // 一个组件分组成立的最小文本数；不足则向父层级上提。
    private const int MinGroupSize = 3;
    // 小组向父层级上提的最大层数。
    private const int MaxEscalationLevels = 2;
    // 每轮最多向 AI 发起的分组提取请求数（用户选「彻底」）。
    private const int MaxGroupsPerRun = 8;
    // 单个分组送给 AI 的行数与字符上限。
    private const int MaxGroupRows = 60;
    private const int MaxGroupCharacters = 4000;
    // 上提时拉取同父层级兄弟文本的上限。
    private const int HierarchyContextLimit = 200;
    // 一致性检查：候选源词在缓存里被查询的样本上限。
    private const int ConsistencyOccurrenceLimit = 200;
    // 一致性检查：判定「译法稳定」所需的最少样本数。
    private const int ConsistencyMinOccurrences = 3;
    // 一致性检查：候选译法在样本里的命中率门槛。
    private const double ConsistencyThreshold = 0.8;
    private const char GroupSeparator = '';

    public static async Task<GlossaryExtractionResult> ExtractOnceAsync(
        ITranslationCache cache,
        IGlossaryStore glossary,
        ITranslationProvider provider,
        RuntimeConfig config,
        CancellationToken cancellationToken)
    {
        if (!config.EnableAutoTermExtraction)
        {
            return new GlossaryExtractionResult(0, 0, 0);
        }

        var targetLanguage = config.TargetLanguage;
        var watermark = glossary.GetExtractionWatermark(targetLanguage);
        var batch = cache.GetCompletedSince(targetLanguage, watermark, BatchRowLimit);
        if (batch.Count == 0)
        {
            return new GlossaryExtractionResult(0, 0, 0);
        }

        var groups = BuildGroups(batch, cache, targetLanguage);
        var imported = 0;
        var skipped = 0;
        var processedRows = 0;
        var processedBatchRowKeys = new HashSet<string>(StringComparer.Ordinal);

        foreach (var group in groups.Take(MaxGroupsPerRun))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = await ProcessGroupAsync(
                group, cache, glossary, provider, config, targetLanguage, cancellationToken).ConfigureAwait(false);
            if (!result.Ok)
            {
                // provider 失败：该组视为未处理，停止本轮（水位线不越过它，下轮重试）。
                break;
            }

            imported += result.Imported;
            skipped += result.Skipped;
            processedRows += group.EffectiveBatchRows.Count;
            foreach (var row in group.EffectiveBatchRows)
            {
                processedBatchRowKeys.Add(RowKey(row));
            }
        }

        var newWatermark = ComputeWatermark(batch, processedBatchRowKeys);
        if (newWatermark != null)
        {
            glossary.SetExtractionWatermark(targetLanguage, newWatermark);
        }

        return new GlossaryExtractionResult(imported, skipped, processedRows);
    }

    /// <summary>
    /// 把本轮 batch 按场景/组件层级分组：同组件的多条文本（对话）整组送；
    /// 单条 UI 文本所在的小组向父层级上提，并入同父层级兄弟文本一起提取。
    /// </summary>
    private static IReadOnlyList<ExtractionGroup> BuildGroups(
        IReadOnlyList<TranslationCacheEntry> batch,
        ITranslationCache cache,
        string targetLanguage)
    {
        var groups = new Dictionary<string, ExtractionGroup>(StringComparer.Ordinal);

        foreach (var componentGroup in batch.GroupBy(row => GroupKey(row.SceneName, row.ComponentHierarchy)))
        {
            var rows = componentGroup.ToList();
            var sample = rows[0];
            var scene = sample.SceneName ?? string.Empty;
            var hierarchy = sample.ComponentHierarchy ?? string.Empty;

            if (rows.Count >= MinGroupSize)
            {
                // 文本足够多的组件（典型是对话框）：自成一组，无需上提。
                GetOrCreate(groups, componentGroup.Key).BatchRows.AddRange(rows);
                continue;
            }

            // 文本太少（典型是单条 UI 标签）：向父层级上提，并入兄弟文本。
            var (escalatedKey, contextRows) = Escalate(scene, hierarchy, cache, targetLanguage);
            var escalated = GetOrCreate(groups, escalatedKey);
            escalated.BatchRows.AddRange(rows);
            foreach (var contextRow in contextRows)
            {
                escalated.ContextRows[RowKey(contextRow)] = contextRow;
            }
        }

        foreach (var group in groups.Values)
        {
            group.Seal();
        }

        return groups.Values
            .Where(group => group.EffectiveBatchRows.Count > 0)
            .OrderBy(group => group.OrderKey)
            .ToList();
    }

    private static (string Key, IReadOnlyList<TranslationCacheEntry> Context) Escalate(
        string scene,
        string hierarchy,
        ITranslationCache cache,
        string targetLanguage)
    {
        var current = hierarchy;
        for (var level = 0; level < MaxEscalationLevels; level++)
        {
            var parent = PromptItemClassifier.GetParentHierarchy(current);
            if (string.IsNullOrEmpty(parent))
            {
                break;
            }

            var siblings = cache.GetCompletedInHierarchy(targetLanguage, scene, parent, HierarchyContextLimit);
            if (siblings.Count >= MinGroupSize)
            {
                return (GroupKey(scene, parent), siblings);
            }

            current = parent!;
        }

        // 无法上提到足够大的父层级：保持原组件分组，仅送自身（无兄弟上下文）。
        return (GroupKey(scene, hierarchy), Array.Empty<TranslationCacheEntry>());
    }

    private static async Task<(bool Ok, int Imported, int Skipped)> ProcessGroupAsync(
        ExtractionGroup group,
        ITranslationCache cache,
        IGlossaryStore glossary,
        ITranslationProvider provider,
        RuntimeConfig config,
        string targetLanguage,
        CancellationToken cancellationToken)
    {
        var request = new TranslationRequest(
            Array.Empty<string>(),
            targetLanguage,
            PromptBuilder.BuildGlossaryExtractionSystemPrompt(config.PromptTemplates),
            PromptBuilder.BuildGlossaryExtractionUserPrompt(group.RowsToSend, config.PromptTemplates));
        var response = await provider.TranslateAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.Succeeded || response.TranslatedTexts.Count == 0)
        {
            return (false, 0, 0);
        }

        var candidates = ParseCandidates(response.TranslatedTexts[0]);
        var imported = 0;
        var skipped = 0;
        foreach (var candidate in candidates)
        {
            if (!IsValidCandidate(candidate, cache, targetLanguage))
            {
                skipped++;
                continue;
            }

            var result = glossary.UpsertAutomatic(GlossaryTerm.CreateAutomatic(
                candidate.Source,
                candidate.Target,
                targetLanguage,
                GlossaryTermCategory.Normalize(candidate.Note)));
            if (result is GlossaryUpsertResult.Created or GlossaryUpsertResult.Updated)
            {
                imported++;
            }
            else
            {
                skipped++;
            }
        }

        return (true, imported, skipped);
    }

    /// <summary>
    /// 推进水位线：batch 已按 updated_utc 升序，取「全部已处理」的最长前缀，
    /// 水位线 = 该前缀最后一行的 updated_utc。最旧的组先处理，故未处理的剩余组在最新端，前缀最长、重复最少。
    /// </summary>
    private static string? ComputeWatermark(
        IReadOnlyList<TranslationCacheEntry> batch,
        HashSet<string> processedBatchRowKeys)
    {
        string? watermark = null;
        foreach (var row in batch)
        {
            if (!processedBatchRowKeys.Contains(RowKey(row)))
            {
                break;
            }

            watermark = row.UpdatedUtc.UtcDateTime.ToString("O");
        }

        return watermark;
    }

    private static IReadOnlyList<Candidate> ParseCandidates(string text)
    {
        try
        {
            var array = JArray.Parse(text.Trim());
            return array
                .OfType<JObject>()
                .Select(item => new Candidate(
                    item.Value<string>("source") ?? string.Empty,
                    item.Value<string>("target") ?? string.Empty,
                    item.Value<string>("note")))
                .ToArray();
        }
        catch
        {
            return Array.Empty<Candidate>();
        }
    }

    private static bool IsValidCandidate(Candidate candidate, ITranslationCache cache, string targetLanguage)
    {
        var source = candidate.Source.Trim();
        var target = candidate.Target.Trim();

        // ① 基础检查（廉价，纯内存）
        if (source.Length < 2 || target.Length == 0 || !HasSemanticCharacter(source) || !HasSemanticCharacter(target))
        {
            return false;
        }

        if (LooksUnsafe(source) || LooksUnsafe(target))
        {
            return false;
        }

        // ② 停用词 / 脚本启发式：拦掉「はい」这类简短高频功能词
        if (SuspiciousGlossaryTermDetector.IsSuspicious(source))
        {
            return false;
        }

        // ③ 跨上下文一致性检查（查 DB，最贵，放最后）
        return PassesConsistencyCheck(source, target, cache, targetLanguage);
    }

    /// <summary>
    /// 一个词只有在「不同上下文里译法稳定」时才配做术语：
    /// 查缓存里所有包含该源词的已完成行，候选译法的命中率必须达到阈值。
    /// </summary>
    private static bool PassesConsistencyCheck(
        string source,
        string target,
        ITranslationCache cache,
        string targetLanguage)
    {
        var occurrences = cache.GetCompletedContainingSource(source, targetLanguage, ConsistencyOccurrenceLimit);
        if (occurrences.Count < ConsistencyMinOccurrences)
        {
            // 样本不足无法判定译法是否稳定，保守拒绝（真高频词必然样本充足）。
            return false;
        }

        var hits = occurrences.Count(row =>
            (row.TranslatedText ?? string.Empty).IndexOf(target, StringComparison.Ordinal) >= 0);
        return (double)hits / occurrences.Count >= ConsistencyThreshold;
    }

    private static bool HasSemanticCharacter(string value)
    {
        return value.Any(char.IsLetterOrDigit);
    }

    private static bool LooksUnsafe(string value)
    {
        return value.IndexOf("__HUT_TOKEN_", StringComparison.Ordinal) >= 0
            || value.IndexOf('<') >= 0
            || value.IndexOf('>') >= 0
            || value.IndexOf('{') >= 0
            || value.IndexOf('}') >= 0
            || value.Length > 80;
    }

    private static ExtractionGroup GetOrCreate(Dictionary<string, ExtractionGroup> groups, string key)
    {
        if (!groups.TryGetValue(key, out var group))
        {
            group = new ExtractionGroup();
            groups[key] = group;
        }

        return group;
    }

    private static string GroupKey(string? sceneName, string? componentHierarchy)
    {
        return (sceneName ?? string.Empty) + GroupSeparator + (componentHierarchy ?? string.Empty);
    }

    private static string RowKey(TranslationCacheEntry row)
    {
        return (row.SceneName ?? string.Empty)
            + GroupSeparator + (row.ComponentHierarchy ?? string.Empty)
            + GroupSeparator + row.SourceText;
    }

    private sealed record Candidate(string Source, string Target, string? Note);

    /// <summary>一个待提取的文本分组：同组件、或上提到同父层级合并而成。</summary>
    private sealed class ExtractionGroup
    {
        // batch 中归属本组的行，驱动水位线推进。
        public List<TranslationCacheEntry> BatchRows { get; } = new();

        // 上提时拉来的同父层级兄弟文本（可能含已提取过的行），只作提取上下文。
        public Dictionary<string, TranslationCacheEntry> ContextRows { get; } = new(StringComparer.Ordinal);

        // 实际送给 AI 的行：BatchRows 优先，再用 ContextRows 补足，受行数/字符上限约束。
        public List<TranslationCacheEntry> RowsToSend { get; private set; } = new();

        // BatchRows 中实际进入了 RowsToSend 的行；只有这些才计入水位线（保证「标记已处理 ⇔ AI 已看到」）。
        public List<TranslationCacheEntry> EffectiveBatchRows { get; private set; } = new();

        // 组排序键：本组最旧 batch 行的 updated_utc，最旧的组先处理。
        public DateTimeOffset OrderKey { get; private set; } = DateTimeOffset.MaxValue;

        public void Seal()
        {
            var send = new List<TranslationCacheEntry>();
            var effective = new List<TranslationCacheEntry>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var characters = 0;

            foreach (var row in BatchRows.OrderBy(item => item.UpdatedUtc))
            {
                if (!TryAdd(send, seen, ref characters, row))
                {
                    break;
                }

                effective.Add(row);
            }

            foreach (var row in ContextRows.Values.OrderBy(item => item.UpdatedUtc))
            {
                if (!TryAdd(send, seen, ref characters, row))
                {
                    break;
                }
            }

            RowsToSend = send;
            EffectiveBatchRows = effective;
            OrderKey = effective.Count > 0
                ? effective.Min(item => item.UpdatedUtc)
                : DateTimeOffset.MaxValue;
        }

        private static bool TryAdd(
            List<TranslationCacheEntry> send,
            HashSet<string> seen,
            ref int characters,
            TranslationCacheEntry row)
        {
            if (send.Count >= MaxGroupRows)
            {
                return false;
            }

            if (!seen.Add(RowKey(row)))
            {
                return true;
            }

            var cost = (row.SourceText?.Length ?? 0) + (row.TranslatedText?.Length ?? 0);
            if (send.Count > 0 && characters + cost > MaxGroupCharacters)
            {
                return false;
            }

            send.Add(row);
            characters += cost;
            return true;
        }
    }
}

public sealed record GlossaryExtractionResult(
    int ImportedCount,
    int SkippedCount,
    int SourcePairCount);

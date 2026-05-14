namespace HUnityAutoTranslator.Core.Glossary;

public interface IGlossaryStore
{
    int Count { get; }

    GlossaryTermPage Query(GlossaryQuery query);

    GlossaryFilterOptionPage GetFilterOptions(GlossaryFilterOptionsQuery query);

    IReadOnlyList<GlossaryTerm> GetEnabledTerms(string targetLanguage);

    GlossaryTerm UpsertManual(GlossaryTerm term);

    GlossaryUpsertResult UpsertAutomatic(GlossaryTerm term);

    void Delete(GlossaryTerm term);

    /// <summary>读取某目标语言的术语提取水位线（最后一次提取处理到的 updated_utc）；从未提取过则为 null。</summary>
    string? GetExtractionWatermark(string targetLanguage);

    /// <summary>持久化某目标语言的术语提取水位线。</summary>
    void SetExtractionWatermark(string targetLanguage, string watermark);

    /// <summary>列出所有「启用中 + 自动提取 + 命中可疑术语规则」的术语，用于存量清理预览。</summary>
    IReadOnlyList<GlossaryTerm> FindSuspiciousAutomaticTerms();

    /// <summary>批量禁用给定术语（置 Enabled=false），返回实际影响的条数。</summary>
    int DisableTerms(IReadOnlyList<GlossaryTerm> terms);

    /// <summary>把所有自动术语的备注重新归一化到固定分类，返回备注发生变化的条数。</summary>
    int RenormalizeAutomaticTermNotes();
}

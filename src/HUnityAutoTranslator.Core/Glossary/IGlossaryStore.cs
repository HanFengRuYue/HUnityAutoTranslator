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
}

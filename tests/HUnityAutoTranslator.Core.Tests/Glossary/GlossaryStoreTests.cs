using FluentAssertions;
using HUnityAutoTranslator.Core.Glossary;

namespace HUnityAutoTranslator.Core.Tests.Glossary;

public sealed class GlossaryStoreTests
{
    [Fact]
    public void Memory_store_creates_updates_queries_and_deletes_manual_terms()
    {
        var store = new MemoryGlossaryStore();
        var term = GlossaryTerm.CreateManual("Freddy", "弗雷迪", "zh-Hans", "角色名");

        store.UpsertManual(term);
        store.Count.Should().Be(1);

        var page = store.Query(new GlossaryQuery("fre", "updated_utc", true, 0, 20));
        page.TotalCount.Should().Be(1);
        page.Items[0].SourceTerm.Should().Be("Freddy");
        page.Items[0].TargetTerm.Should().Be("弗雷迪");
        page.Items[0].Source.Should().Be(GlossaryTermSource.Manual);
        page.Items[0].Enabled.Should().BeTrue();

        store.UpsertManual(page.Items[0] with { TargetTerm = "佛莱迪", Note = "人工修正" });
        store.Query(new GlossaryQuery(null, "source_term", false, 0, 20)).Items[0].TargetTerm.Should().Be("佛莱迪");

        store.Delete(page.Items[0]);
        store.Count.Should().Be(0);
    }

    [Fact]
    public void Automatic_terms_do_not_overwrite_manual_terms()
    {
        var store = new MemoryGlossaryStore();
        store.UpsertManual(GlossaryTerm.CreateManual("Bonnie", "邦尼", "zh-Hans", null));

        var result = store.UpsertAutomatic(GlossaryTerm.CreateAutomatic("Bonnie", "波尼", "zh-Hans", "AI"));

        result.Should().Be(GlossaryUpsertResult.SkippedManualConflict);
        store.GetEnabledTerms("zh-Hans").Should().ContainSingle(term =>
            term.SourceTerm == "Bonnie" &&
            term.TargetTerm == "邦尼" &&
            term.Source == GlossaryTermSource.Manual);
    }

    [Fact]
    public void Automatic_terms_can_refresh_matching_automatic_terms()
    {
        var store = new MemoryGlossaryStore();
        store.UpsertAutomatic(GlossaryTerm.CreateAutomatic("Chica", "奇卡", "zh-Hans", null));

        var result = store.UpsertAutomatic(GlossaryTerm.CreateAutomatic("Chica", "奇卡", "zh-Hans", "again"));

        result.Should().Be(GlossaryUpsertResult.Updated);
        var term = store.GetEnabledTerms("zh-Hans").Should().ContainSingle().Subject;
        term.UsageCount.Should().Be(2);
        term.Note.Should().Be("again");
    }

    [Fact]
    public void Sqlite_store_uses_independent_readable_schema_and_manual_priority()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "translation-glossary.sqlite");
        using var store = new SqliteGlossaryStore(path);

        store.UpsertManual(GlossaryTerm.CreateManual("Security Breach", "安全漏洞", "zh-Hans", "标题"));
        store.UpsertAutomatic(GlossaryTerm.CreateAutomatic("Security Breach", "保安漏洞", "zh-Hans", null))
            .Should().Be(GlossaryUpsertResult.SkippedManualConflict);

        var page = store.Query(new GlossaryQuery("security", "source_term", false, 0, 10));
        page.TotalCount.Should().Be(1);
        page.Items[0].TargetTerm.Should().Be("安全漏洞");
        page.Items[0].Source.Should().Be(GlossaryTermSource.Manual);

        File.Exists(path).Should().BeTrue();
    }
}

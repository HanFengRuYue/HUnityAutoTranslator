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

    [Fact]
    public void Memory_store_filters_and_counts_glossary_columns()
    {
        var store = new MemoryGlossaryStore();
        store.UpsertManual(GlossaryTerm.CreateManual("Freddy", "弗雷迪", "zh-Hans", "角色名"));
        store.UpsertManual(GlossaryTerm.CreateManual("Foxy", "狐狸", "zh-Hans", null) with { Enabled = false });
        store.UpsertManual(GlossaryTerm.CreateManual("Bonnie", "邦尼", "ja", "角色名"));
        store.UpsertAutomatic(GlossaryTerm.CreateAutomatic("Pirate Cove", "海盗湾", "zh-Hans", "场景"));

        var page = store.Query(new GlossaryQuery(
            Search: null,
            SortColumn: "source_term",
            SortDescending: false,
            Offset: 0,
            Limit: 20,
            ColumnFilters: new[]
            {
                new GlossaryColumnFilter("target_language", new[] { "zh-Hans" }),
                new GlossaryColumnFilter("note", new string?[] { null, "场景" })
            }));

        page.TotalCount.Should().Be(2);
        page.Items.Select(item => item.SourceTerm).Should().Equal("Foxy", "Pirate Cove");

        var options = store.GetFilterOptions(new GlossaryFilterOptionsQuery(
            Column: "source",
            Search: null,
            ColumnFilters: new[] { new GlossaryColumnFilter("target_language", new[] { "zh-Hans" }) },
            OptionSearch: null,
            Limit: 20));

        options.Column.Should().Be("source");
        options.Items.Should().ContainEquivalentOf(new GlossaryFilterOption("Manual", 2));
        options.Items.Should().ContainEquivalentOf(new GlossaryFilterOption("Automatic", 1));
    }

    [Fact]
    public void Sqlite_store_filters_and_counts_glossary_columns()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "translation-glossary.sqlite");
        using var store = new SqliteGlossaryStore(path);
        store.UpsertManual(GlossaryTerm.CreateManual("Freddy", "弗雷迪", "zh-Hans", "角色名"));
        store.UpsertManual(GlossaryTerm.CreateManual("Foxy", "狐狸", "zh-Hans", null) with { Enabled = false });
        store.UpsertManual(GlossaryTerm.CreateManual("Bonnie", "邦尼", "ja", "角色名"));
        store.UpsertAutomatic(GlossaryTerm.CreateAutomatic("Pirate Cove", "海盗湾", "zh-Hans", "场景"));

        var page = store.Query(new GlossaryQuery(
            Search: null,
            SortColumn: "source_term",
            SortDescending: false,
            Offset: 0,
            Limit: 20,
            ColumnFilters: new[]
            {
                new GlossaryColumnFilter("enabled", new[] { "true" }),
                new GlossaryColumnFilter("target_language", new[] { "zh-Hans" })
            }));

        page.TotalCount.Should().Be(2);
        page.Items.Select(item => item.SourceTerm).Should().Equal("Freddy", "Pirate Cove");

        var options = store.GetFilterOptions(new GlossaryFilterOptionsQuery(
            Column: "note",
            Search: null,
            ColumnFilters: new[] { new GlossaryColumnFilter("target_language", new[] { "zh-Hans" }) },
            OptionSearch: null,
            Limit: 20));

        options.Column.Should().Be("note");
        options.Items.Should().ContainEquivalentOf(new GlossaryFilterOption(null, 1));
        options.Items.Should().ContainEquivalentOf(new GlossaryFilterOption("角色名", 1));
        options.Items.Should().ContainEquivalentOf(new GlossaryFilterOption("场景", 1));
    }
}

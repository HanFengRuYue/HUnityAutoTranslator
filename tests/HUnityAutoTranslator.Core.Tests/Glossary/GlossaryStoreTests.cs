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
    public void Sqlite_store_invalidates_enabled_term_cache_after_mutations()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "translation-glossary.sqlite");
        using var store = new SqliteGlossaryStore(path);

        store.GetEnabledTerms("zh-Hans").Should().BeEmpty();

        var manual = GlossaryTerm.CreateManual("Freddy", "Freddy CN", "zh-Hans", null);
        store.UpsertManual(manual);
        store.GetEnabledTerms("zh-Hans").Should().ContainSingle(term => term.SourceTerm == "Freddy");

        store.UpsertAutomatic(GlossaryTerm.CreateAutomatic("Bonnie", "Bonnie CN", "zh-Hans", null))
            .Should().Be(GlossaryUpsertResult.Created);
        store.GetEnabledTerms("zh-Hans").Select(term => term.SourceTerm).Should().BeEquivalentTo("Freddy", "Bonnie");

        store.Delete(manual);
        store.GetEnabledTerms("zh-Hans").Select(term => term.SourceTerm).Should().Equal("Bonnie");
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

    [Fact]
    public void Memory_store_disables_existing_automatic_term_on_translation_conflict()
    {
        var store = new MemoryGlossaryStore();
        store.UpsertAutomatic(GlossaryTerm.CreateAutomatic("Spark", "火花", "zh-Hans", "物品名"));

        var result = store.UpsertAutomatic(GlossaryTerm.CreateAutomatic("Spark", "斯帕克", "zh-Hans", "角色名"));

        result.Should().Be(GlossaryUpsertResult.SkippedAutomaticConflict);
        store.GetEnabledTerms("zh-Hans").Should().BeEmpty();
        var page = store.Query(new GlossaryQuery(null, "updated_utc", true, 0, 20));
        page.TotalCount.Should().Be(1);
        page.Items[0].TargetTerm.Should().Be("火花");
        page.Items[0].Enabled.Should().BeFalse();
    }

    [Fact]
    public void Sqlite_store_disables_existing_automatic_term_on_translation_conflict()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "translation-glossary.sqlite");
        using var store = new SqliteGlossaryStore(path);
        store.UpsertAutomatic(GlossaryTerm.CreateAutomatic("Spark", "火花", "zh-Hans", "物品名"));

        var result = store.UpsertAutomatic(GlossaryTerm.CreateAutomatic("Spark", "斯帕克", "zh-Hans", "角色名"));

        result.Should().Be(GlossaryUpsertResult.SkippedAutomaticConflict);
        store.GetEnabledTerms("zh-Hans").Should().BeEmpty();
        var page = store.Query(new GlossaryQuery(null, "updated_utc", true, 0, 20));
        page.TotalCount.Should().Be(1);
        page.Items[0].TargetTerm.Should().Be("火花");
        page.Items[0].Enabled.Should().BeFalse();
    }

    [Fact]
    public void Memory_store_round_trips_extraction_watermark_per_language()
    {
        var store = new MemoryGlossaryStore();

        store.GetExtractionWatermark("zh-Hans").Should().BeNull();

        store.SetExtractionWatermark("zh-Hans", "2026-05-01T00:05:00.0000000Z");
        store.GetExtractionWatermark("zh-Hans").Should().Be("2026-05-01T00:05:00.0000000Z");

        store.SetExtractionWatermark("zh-Hans", "2026-05-01T00:09:00.0000000Z");
        store.GetExtractionWatermark("zh-Hans").Should().Be("2026-05-01T00:09:00.0000000Z");
        store.GetExtractionWatermark("ja").Should().BeNull();
    }

    [Fact]
    public void Sqlite_store_round_trips_extraction_watermark_per_language()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "translation-glossary.sqlite");
        using var store = new SqliteGlossaryStore(path);

        store.GetExtractionWatermark("zh-Hans").Should().BeNull();

        store.SetExtractionWatermark("zh-Hans", "2026-05-01T00:05:00.0000000Z");
        store.GetExtractionWatermark("zh-Hans").Should().Be("2026-05-01T00:05:00.0000000Z");

        store.SetExtractionWatermark("zh-Hans", "2026-05-01T00:09:00.0000000Z");
        store.GetExtractionWatermark("zh-Hans").Should().Be("2026-05-01T00:09:00.0000000Z");
        store.GetExtractionWatermark("ja").Should().BeNull();
    }

    [Fact]
    public void FindSuspiciousAutomaticTerms_returns_only_enabled_automatic_suspicious_terms()
    {
        var store = new MemoryGlossaryStore();
        store.UpsertManual(GlossaryTerm.CreateManual("はい", "是的", "zh-Hans", null));
        store.UpsertAutomatic(GlossaryTerm.CreateAutomatic("うん", "嗯", "zh-Hans", "其他"));
        store.UpsertAutomatic(GlossaryTerm.CreateAutomatic("エクスカリバー", "圣剑", "zh-Hans", "物品名"));
        store.UpsertAutomatic(GlossaryTerm.CreateAutomatic("そう", "对", "zh-Hans", "其他"));
        store.DisableTerms(new[] { GlossaryTerm.CreateAutomatic("そう", "对", "zh-Hans", null) });

        var suspicious = store.FindSuspiciousAutomaticTerms();

        suspicious.Should().ContainSingle(term => term.SourceTerm == "うん");
    }

    [Fact]
    public void DisableTerms_disables_enabled_targets_and_returns_affected_count()
    {
        var store = new MemoryGlossaryStore();
        store.UpsertManual(GlossaryTerm.CreateManual("A", "甲", "zh-Hans", null));
        store.UpsertManual(GlossaryTerm.CreateManual("B", "乙", "zh-Hans", null));
        store.UpsertManual(GlossaryTerm.CreateManual("C", "丙", "zh-Hans", null) with { Enabled = false });

        var affected = store.DisableTerms(new[]
        {
            GlossaryTerm.CreateManual("A", "甲", "zh-Hans", null),
            GlossaryTerm.CreateManual("C", "丙", "zh-Hans", null),
            GlossaryTerm.CreateManual("Z", "未知", "zh-Hans", null)
        });

        affected.Should().Be(1);
        store.GetEnabledTerms("zh-Hans").Select(term => term.SourceTerm).Should().Equal("B");
    }

    [Fact]
    public void Sqlite_store_finds_disables_and_renormalizes_automatic_terms()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "translation-glossary.sqlite");
        using var store = new SqliteGlossaryStore(path);
        store.UpsertAutomatic(GlossaryTerm.CreateAutomatic("うん", "嗯", "zh-Hans", "Boss name"));
        store.UpsertAutomatic(GlossaryTerm.CreateAutomatic("エクスカリバー", "圣剑", "zh-Hans", "weapon"));
        store.UpsertManual(GlossaryTerm.CreateManual("Manual", "手动", "zh-Hans", "free note"));

        store.RenormalizeAutomaticTermNotes().Should().Be(2);

        var suspicious = store.FindSuspiciousAutomaticTerms();
        suspicious.Should().ContainSingle(term => term.SourceTerm == "うん");

        store.DisableTerms(suspicious).Should().Be(1);
        store.GetEnabledTerms("zh-Hans").Select(term => term.SourceTerm)
            .Should().BeEquivalentTo("エクスカリバー", "Manual");
    }

    [Fact]
    public void RenormalizeAutomaticTermNotes_maps_messy_notes_to_categories_and_skips_manual()
    {
        var store = new MemoryGlossaryStore();
        store.UpsertAutomatic(GlossaryTerm.CreateAutomatic("Eleanor", "艾蕾诺尔", "zh-Hans", "Boss name"));
        store.UpsertAutomatic(GlossaryTerm.CreateAutomatic("Bonnie", "邦尼", "zh-Hans", "BOSS名"));
        store.UpsertAutomatic(GlossaryTerm.CreateAutomatic("Cove", "海湾", "zh-Hans", GlossaryTermCategory.PlaceName));
        store.UpsertManual(GlossaryTerm.CreateManual("Manual", "手动", "zh-Hans", "Boss name"));

        var changed = store.RenormalizeAutomaticTermNotes();

        changed.Should().Be(2);
        var rows = store.Query(new GlossaryQuery(null, "source_term", false, 0, 20)).Items;
        rows.Single(term => term.SourceTerm == "Eleanor").Note.Should().Be(GlossaryTermCategory.BossOrEnemyName);
        rows.Single(term => term.SourceTerm == "Bonnie").Note.Should().Be(GlossaryTermCategory.BossOrEnemyName);
        rows.Single(term => term.SourceTerm == "Cove").Note.Should().Be(GlossaryTermCategory.PlaceName);
        rows.Single(term => term.SourceTerm == "Manual").Note.Should().Be("Boss name");
    }
}

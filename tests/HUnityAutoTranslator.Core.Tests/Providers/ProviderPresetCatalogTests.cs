using FluentAssertions;
using HUnityAutoTranslator.Core.Configuration;
using HUnityAutoTranslator.Core.Providers;

namespace HUnityAutoTranslator.Core.Tests.Providers;

public sealed class ProviderPresetCatalogTests
{
    [Fact]
    public void Catalog_contains_the_expected_eleven_presets()
    {
        ProviderPresetCatalog.All.Should().HaveCount(11);
        ProviderPresetCatalog.All.Select(preset => preset.Id).Should().BeEquivalentTo(new[]
        {
            "deepseek", "siliconflow", "zhipu", "moonshot", "dashscope", "volcengine",
            "openai", "openrouter", "groq", "xai", "gemini"
        });
    }

    [Fact]
    public void Catalog_preset_ids_are_unique()
    {
        ProviderPresetCatalog.All.Select(preset => preset.Id).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void Catalog_entries_have_well_formed_metadata()
    {
        foreach (var preset in ProviderPresetCatalog.All)
        {
            preset.Kind.Should().NotBe(ProviderKind.LlamaCpp, preset.Id);
            preset.RequestsPerMinute.Should().BeGreaterThan(0, preset.Id);
            preset.DisplayName.Should().NotBeNullOrWhiteSpace();
            preset.DefaultModel.Should().NotBeNullOrWhiteSpace();
            preset.Endpoint.Should().StartWith("/", preset.Id);
            preset.SuggestedModels.Should().Contain(preset.DefaultModel, preset.Id);
            preset.Notes.Should().NotBeNullOrWhiteSpace();

            IsAbsoluteHttps(preset.BaseUrl).Should().BeTrue(preset.BaseUrl);
            IsAbsoluteHttps(preset.ConsoleUrl).Should().BeTrue(preset.ConsoleUrl);
            IsAbsoluteHttps(preset.DocsUrl).Should().BeTrue(preset.DocsUrl);

            if (preset.BalanceQuery != null)
            {
                preset.BalanceQuery.Path.Should().StartWith("/", preset.Id);
            }
        }
    }

    [Fact]
    public void Resolve_matches_ids_case_insensitively_and_rejects_unknown_ids()
    {
        ProviderPresetCatalog.Resolve("siliconflow").Should().NotBeNull();
        ProviderPresetCatalog.Resolve("SiliconFlow")!.Id.Should().Be("siliconflow");
        ProviderPresetCatalog.Resolve("  moonshot  ")!.Id.Should().Be("moonshot");
        ProviderPresetCatalog.Resolve("not-a-real-preset").Should().BeNull();
        ProviderPresetCatalog.Resolve(null).Should().BeNull();
        ProviderPresetCatalog.Resolve("").Should().BeNull();

        ProviderPresetCatalog.IsKnown("groq").Should().BeTrue();
        ProviderPresetCatalog.IsKnown("groq-x").Should().BeFalse();
        ProviderPresetCatalog.IsKnown(null).Should().BeFalse();
    }

    [Fact]
    public void ToInfo_projects_balance_capability_to_a_boolean_and_keeps_console_url()
    {
        var siliconflow = ProviderPresetCatalog.Resolve("siliconflow")!;
        siliconflow.SupportsBalanceQuery.Should().BeTrue();
        siliconflow.ToInfo().SupportsBalanceQuery.Should().BeTrue();

        var groq = ProviderPresetCatalog.Resolve("groq")!;
        groq.SupportsBalanceQuery.Should().BeFalse();
        var groqInfo = groq.ToInfo();
        groqInfo.SupportsBalanceQuery.Should().BeFalse();
        groqInfo.ConsoleUrl.Should().Be(groq.ConsoleUrl);
        groqInfo.SupportsModelList.Should().Be(groq.SupportsModelList);
    }

    private static bool IsAbsoluteHttps(string url)
    {
        return Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps;
    }
}

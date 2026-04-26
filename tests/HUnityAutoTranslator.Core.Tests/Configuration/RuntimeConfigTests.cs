using FluentAssertions;
using HUnityAutoTranslator.Core.Configuration;
using HUnityAutoTranslator.Core.Prompts;

namespace HUnityAutoTranslator.Core.Tests.Configuration;

public sealed class RuntimeConfigTests
{
    [Fact]
    public void DefaultConfig_is_localhost_and_zhHans()
    {
        var config = RuntimeConfig.CreateDefault();

        config.TargetLanguage.Should().Be("zh-Hans");
        config.HttpHost.Should().Be("127.0.0.1");
        config.Provider.Kind.Should().Be(ProviderKind.OpenAI);
        config.Style.Should().Be(TranslationStyle.Localized);
        config.MaxConcurrentRequests.Should().BeGreaterThan(1);
    }

    [Fact]
    public void DefaultConfig_enables_font_replacement_with_automatic_cjk_fallbacks()
    {
        var config = RuntimeConfig.CreateDefault();

        config.EnableFontReplacement.Should().BeTrue();
        config.ReplaceUguiFonts.Should().BeTrue();
        config.ReplaceTmpFonts.Should().BeTrue();
        config.ReplaceImguiFonts.Should().BeTrue();
        config.AutoUseCjkFallbackFonts.Should().BeTrue();
        config.ReplacementFontName.Should().BeNull();
        config.ReplacementFontFile.Should().BeNull();
        config.FontSamplingPointSize.Should().Be(90);
    }
}

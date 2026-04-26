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
        config.FontSizeAdjustmentMode.Should().Be(FontSizeAdjustmentMode.Disabled);
        config.FontSizeAdjustmentValue.Should().Be(0);
    }

    [Theory]
    [InlineData(FontSizeAdjustmentMode.Points, -5, 32, 27)]
    [InlineData(FontSizeAdjustmentMode.Percent, -10, 32, 28.8)]
    [InlineData(FontSizeAdjustmentMode.Points, -100, 12, 1)]
    [InlineData(FontSizeAdjustmentMode.Percent, -99, 12, 1)]
    public void Font_size_adjustment_is_calculated_from_original_size(
        FontSizeAdjustmentMode mode,
        double adjustmentValue,
        float originalSize,
        float expectedSize)
    {
        FontSizeAdjustment.Calculate(originalSize, mode, adjustmentValue)
            .Should().BeApproximately(expectedSize, 0.001f);
    }

    [Fact]
    public void DefaultConfig_uses_requested_runtime_hotkeys()
    {
        var config = RuntimeConfig.CreateDefault();

        config.OpenControlPanelHotkey.Should().Be("Alt+H");
        config.ToggleTranslationHotkey.Should().Be("Alt+F");
        config.ForceScanHotkey.Should().Be("Alt+G");
        config.ToggleFontHotkey.Should().Be("Alt+D");
    }

    [Fact]
    public void DefaultDeepSeek_profile_uses_current_official_flash_model()
    {
        var profile = ProviderProfile.DefaultDeepSeek();

        profile.Kind.Should().Be(ProviderKind.DeepSeek);
        profile.BaseUrl.Should().Be("https://api.deepseek.com");
        profile.Endpoint.Should().Be("/chat/completions");
        profile.Model.Should().Be("deepseek-v4-flash");
    }
}

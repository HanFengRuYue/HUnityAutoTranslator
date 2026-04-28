using FluentAssertions;
using HUnityAutoTranslator.Core.Dispatching;

namespace HUnityAutoTranslator.Core.Tests.Dispatching;

public sealed class TranslationWritebackTrackerTests
{
    [Fact]
    public void TryGetReplacement_reapplies_translation_when_game_restores_original_text()
    {
        var tracker = new TranslationWritebackTracker();
        tracker.Remember("title", "IMPORTANT", "IMPORTANT_ZH");

        tracker.TryGetReplacement("title", "IMPORTANT", out var replacement).Should().BeTrue();
        replacement.Should().Be("IMPORTANT_ZH");

        tracker.TryGetReplacement("title", "IMPORTANT_ZH", out _).Should().BeFalse();

        tracker.TryGetReplacement("title", "IMPORTANT", out replacement).Should().BeTrue();
        replacement.Should().Be("IMPORTANT_ZH");
    }

    [Fact]
    public void TryGetReplacement_keeps_memory_when_target_temporarily_changes_to_different_text()
    {
        var tracker = new TranslationWritebackTracker();
        tracker.Remember("title", "IMPORTANT", "IMPORTANT_ZH");

        tracker.TryGetReplacement("title", "OPTIONS", out _).Should().BeFalse();
        tracker.TryGetReplacement("title", "IMPORTANT", out var replacement).Should().BeTrue();
        replacement.Should().Be("IMPORTANT_ZH");
    }

    [Fact]
    public void IsRememberedTranslation_detects_text_that_was_already_written_back()
    {
        var tracker = new TranslationWritebackTracker();
        tracker.Remember("title", "IMPORTANT", "IMPORTANT_ZH");

        tracker.IsRememberedTranslation("title", "IMPORTANT_ZH").Should().BeTrue();
        tracker.IsRememberedTranslation("title", "IMPORTANT").Should().BeFalse();
        tracker.IsRememberedTranslation("missing", "IMPORTANT_ZH").Should().BeFalse();
    }

    [Fact]
    public void TryGetReplacement_replaces_previous_manual_translation_with_latest_text()
    {
        var tracker = new TranslationWritebackTracker();
        tracker.Remember("title", "Start Game", "开始游戏");

        tracker.Remember("title", "Start Game", "开始", "开始游戏");

        tracker.IsRememberedTranslation("title", "开始游戏").Should().BeFalse();
        tracker.IsRememberedTranslation("title", "开始").Should().BeTrue();
        tracker.TryGetReplacement("title", "开始游戏", out var replacement).Should().BeTrue();
        replacement.Should().Be("开始");
        tracker.TryGetReplacement("title", "Start Game", out replacement).Should().BeTrue();
        replacement.Should().Be("开始");
    }

    [Fact]
    public void TryGetReplacement_does_not_apply_manual_translation_to_unrelated_text()
    {
        var tracker = new TranslationWritebackTracker();
        tracker.Remember("title", "Start Game", "开始", "开始游戏");

        tracker.TryGetReplacement("title", "Options", out _).Should().BeFalse();
        tracker.TryGetReplacement("title", "开始", out _).Should().BeFalse();
        tracker.TryGetReplacement("missing", "Start Game", out _).Should().BeFalse();
    }
    [Fact]
    public void TryRememberForCurrentText_does_not_replace_existing_memory_when_result_is_stale_for_component()
    {
        var tracker = new TranslationWritebackTracker();
        tracker.Remember("title", "Current", "Current ZH");

        tracker.TryRememberForCurrentText("title", "Current ZH", "Previous", "Previous ZH").Should().BeFalse();

        tracker.TryGetReplacement("title", "Current", out var replacement).Should().BeTrue();
        replacement.Should().Be("Current ZH");
    }

    [Fact]
    public void TryRememberForCurrentText_accepts_source_previous_or_translated_text_for_the_same_component()
    {
        var tracker = new TranslationWritebackTracker();

        tracker.TryRememberForCurrentText("title", "Start Game", "Start Game", "Start Game ZH").Should().BeTrue();
        tracker.IsRememberedTranslation("title", "Start Game ZH").Should().BeTrue();

        tracker.TryRememberForCurrentText("title", "Start Game ZH", "Start Game", "Start Game ZH").Should().BeTrue();
        tracker.IsRememberedTranslation("title", "Start Game ZH").Should().BeTrue();

        tracker.TryRememberForCurrentText("title", "Start Game ZH", "Start Game", "Start ZH", "Start Game ZH").Should().BeTrue();
        tracker.TryGetReplacement("title", "Start Game", out var replacement).Should().BeTrue();
        replacement.Should().Be("Start ZH");
    }

    [Fact]
    public void TryGetDisplayText_switches_between_source_and_translated_text()
    {
        var tracker = new TranslationWritebackTracker();
        tracker.Remember("title", "Start Game", "Start Game ZH");

        tracker.TryGetDisplayText("title", "Start Game ZH", useTranslatedText: false, out var replacement)
            .Should().BeTrue();
        replacement.Should().Be("Start Game");

        tracker.TryGetDisplayText("title", "Start Game", useTranslatedText: true, out replacement)
            .Should().BeTrue();
        replacement.Should().Be("Start Game ZH");
    }

    [Fact]
    public void TryRestoreSourceText_forgets_translation_so_source_can_be_captured_again()
    {
        var tracker = new TranslationWritebackTracker();
        tracker.Remember("title", "Start Game", "Start Game ZH");

        tracker.TryRestoreSourceText("title", "Start Game ZH", "Start Game", "Start Game ZH", out var replacement)
            .Should().BeTrue();

        replacement.Should().Be("Start Game");
        tracker.IsRememberedTranslation("title", "Start Game").Should().BeFalse();
        tracker.IsRememberedTranslation("title", "Start Game ZH").Should().BeFalse();
        tracker.TryGetReplacement("title", "Start Game", out _).Should().BeFalse();
    }
}

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
    public void TryGetReplacement_forgets_translation_when_target_changes_to_different_source()
    {
        var tracker = new TranslationWritebackTracker();
        tracker.Remember("title", "IMPORTANT", "IMPORTANT_ZH");

        tracker.TryGetReplacement("title", "OPTIONS", out _).Should().BeFalse();
        tracker.TryGetReplacement("title", "IMPORTANT", out _).Should().BeFalse();
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
}

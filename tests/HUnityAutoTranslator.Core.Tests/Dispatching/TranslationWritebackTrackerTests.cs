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
}

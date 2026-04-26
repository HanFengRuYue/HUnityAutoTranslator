using FluentAssertions;
using HUnityAutoTranslator.Core.Runtime;

namespace HUnityAutoTranslator.Core.Tests.Runtime;

public sealed class RoundRobinCursorTests
{
    [Fact]
    public void TakeWindow_rotates_through_items_when_the_window_is_smaller_than_the_source()
    {
        var cursor = new RoundRobinCursor();
        var items = new[] { "a", "b", "c", "d", "e" };

        cursor.TakeWindow(items, 2).Should().Equal("a", "b");
        cursor.TakeWindow(items, 2).Should().Equal("c", "d");
        cursor.TakeWindow(items, 2).Should().Equal("e", "a");
        cursor.TakeWindow(items, 2).Should().Equal("b", "c");
    }

    [Fact]
    public void TakeWindow_returns_every_item_when_the_limit_covers_the_source()
    {
        var cursor = new RoundRobinCursor();
        var items = new[] { 1, 2, 3 };

        cursor.TakeWindow(items, 10).Should().Equal(1, 2, 3);
        cursor.TakeWindow(items, 10).Should().Equal(1, 2, 3);
    }

    [Fact]
    public void TakeWindow_keeps_the_cursor_valid_when_the_source_size_changes()
    {
        var cursor = new RoundRobinCursor();

        cursor.TakeWindow(new[] { 1, 2, 3, 4 }, 3).Should().Equal(1, 2, 3);
        cursor.TakeWindow(new[] { 10, 20 }, 1).Should().Equal(20);
        cursor.TakeWindow(new[] { 10, 20 }, 1).Should().Equal(10);
    }

    [Fact]
    public void TakeWindow_returns_empty_for_empty_sources_or_non_positive_limits()
    {
        var cursor = new RoundRobinCursor();

        cursor.TakeWindow(Array.Empty<string>(), 2).Should().BeEmpty();
        cursor.TakeWindow(new[] { "a" }, 0).Should().BeEmpty();
        cursor.TakeWindow(new[] { "a" }, -1).Should().BeEmpty();
    }

    [Fact]
    public void TakeFullRound_returns_all_items_from_a_rotating_start_position()
    {
        var cursor = new RoundRobinCursor();
        var items = new[] { "a", "b", "c", "d" };

        cursor.TakeFullRound(items).Should().Equal("a", "b", "c", "d");
        cursor.TakeFullRound(items).Should().Equal("b", "c", "d", "a");
        cursor.TakeFullRound(items).Should().Equal("c", "d", "a", "b");
    }
}

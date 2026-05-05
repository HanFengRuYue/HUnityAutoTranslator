using FluentAssertions;
using HUnityAutoTranslator.Core.Providers;

namespace HUnityAutoTranslator.Core.Tests.Providers;

public sealed class ProviderParserTests
{
    [Fact]
    public void OpenAiResponsesParser_reads_output_text_items()
    {
        const string json = """
        {
          "output": [
            {
              "type": "message",
              "content": [
                { "type": "output_text", "text": "开始游戏" }
              ]
            }
          ]
        }
        """;

        ProviderJsonParsers.ParseOpenAiResponsesText(json).Should().Be("开始游戏");
    }

    [Fact]
    public void ChatCompletionsParser_reads_choice_message_content()
    {
        const string json = """
        {
          "choices": [
            { "message": { "role": "assistant", "content": "开始游戏" } }
          ]
        }
        """;

        ProviderJsonParsers.ParseChatCompletionsText(json).Should().Be("开始游戏");
    }

    [Fact]
    public void AssistantTextParser_strips_numbered_line_prefixes()
    {
        var texts = ProviderJsonParsers.ParseAssistantTextAsList("""
        0: 继续
        1: 关
        """);

        texts.Should().Equal("继续", "关");
    }

    [Fact]
    public void AssistantTextParser_strips_single_numbered_line_prefix()
    {
        var texts = ProviderJsonParsers.ParseAssistantTextAsList("0: 继续");

        texts.Should().ContainSingle().Which.Should().Be("继续");
    }

    [Fact]
    public void AssistantTextParser_unwraps_single_json_string_when_one_item_is_expected()
    {
        var texts = ProviderJsonParsers.ParseAssistantTextAsList("\u0022\u8bbe\u7f6e\u0022", expectedCount: 1);

        texts.Should().ContainSingle().Which.Should().Be("\u8bbe\u7f6e");
    }

    [Fact]
    public void AssistantTextParser_extracts_single_indexed_translation_object()
    {
        var texts = ProviderJsonParsers.ParseAssistantTextAsList(
            """{"text_index":0,"text":"\u4f60\u7559\u7740\u5427\uff0c\u8fd9\u611f\u89c9\u4e0d\u592a\u5bf9\u3002"}""",
            expectedCount: 1);

        texts.Should().ContainSingle().Which.Should().Be("\u4f60\u7559\u7740\u5427\uff0c\u8fd9\u611f\u89c9\u4e0d\u592a\u5bf9\u3002");
    }

    [Fact]
    public void AssistantTextParser_extracts_indexed_translation_object_array_in_text_index_order()
    {
        var texts = ProviderJsonParsers.ParseAssistantTextAsList(
            """
            [
              {"text_index":1,"translation":"\u5173\u95ed"},
              {"text_index":0,"text":"\u5f00\u542f"}
            ]
            """,
            expectedCount: 2);

        texts.Should().Equal("\u5f00\u542f", "\u5173\u95ed");
    }

    [Fact]
    public void AssistantTextParser_preserves_plain_json_string_arrays()
    {
        var texts = ProviderJsonParsers.ParseAssistantTextAsList(
            """["\u7ee7\u7eed","\u5173\u95ed"]""",
            expectedCount: 2);

        texts.Should().Equal("\u7ee7\u7eed", "\u5173\u95ed");
    }

    [Theory]
    [InlineData("""[{"text_index":1,"text":"\u5173\u95ed"}]""")]
    [InlineData("""[{"text_index":0,"text":"\u5f00\u542f"},{"text_index":0,"text":"\u5173\u95ed"}]""")]
    [InlineData("""[{"text":"\u5f00\u542f"},{"text_index":1,"text":"\u5173\u95ed"}]""")]
    public void AssistantTextParser_leaves_invalid_indexed_object_arrays_for_format_validation(string assistantText)
    {
        var texts = ProviderJsonParsers.ParseAssistantTextAsList(assistantText, expectedCount: 2);

        texts.Should().ContainSingle().Which.Should().Be(assistantText);
    }

    [Fact]
    public void AssistantTextParser_leaves_unindexed_object_for_format_validation()
    {
        const string assistantText = """{"text":"\u8bbe\u7f6e"}""";

        var texts = ProviderJsonParsers.ParseAssistantTextAsList(assistantText, expectedCount: 1);

        texts.Should().ContainSingle().Which.Should().Be(assistantText);
    }
}

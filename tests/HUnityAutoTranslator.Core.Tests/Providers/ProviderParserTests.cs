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
}

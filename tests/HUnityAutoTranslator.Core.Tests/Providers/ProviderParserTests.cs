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
}

using FluentAssertions;
using HUnityAutoTranslator.Core.Prompts;

namespace HUnityAutoTranslator.Core.Tests.Prompts;

public sealed class TranslationOutputValidatorStructuredJsonTests
{
    [Theory]
    [InlineData("""{"text_index":0,"text":"\u8bbe\u7f6e"}""")]
    [InlineData("""{"text_index":0,"translation":"\u8bbe\u7f6e"}""")]
    [InlineData("""{"text":"\u8bbe\u7f6e"}""")]
    [InlineData("""[{"text_index":0,"text":"\u8bbe\u7f6e"}]""")]
    public void Validator_rejects_structured_json_response_artifacts(string translatedText)
    {
        var result = TranslationOutputValidator.ValidateSingle(
            "Settings",
            translatedText,
            requireSameRichTextTags: true);

        result.IsValid.Should().BeFalse();
        result.Reason.Should().Contain("JSON");
    }
}

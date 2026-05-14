using FluentAssertions;
using HUnityAutoTranslator.Core.Configuration;
using HUnityAutoTranslator.Core.Http;
using HUnityAutoTranslator.Core.Providers;
using HUnityAutoTranslator.Core.Tests.Http;
using HUnityAutoTranslator.Core.Textures;

namespace HUnityAutoTranslator.Core.Tests.Textures;

public sealed class TextureImageEditClientTests
{
    [Fact]
    public async Task EditAsync_posts_openai_compatible_multipart_request_and_reads_base64_png()
    {
        var transport = new FakeHttpTransport(_ => FakeHttpTransport.Json(
            $$"""{"data":[{"b64_json":"{{Convert.ToBase64String(PngBytes())}}","revised_prompt":"ok"}]}"""));
        var config = TextureImageTranslationConfig.Default() with { Enabled = true };
        var client = new TextureImageEditClient(transport, () => "sk-texture");

        var result = await client.EditAsync(
            config,
            "Translate visible texture text to Simplified Chinese.",
            PngBytes(),
            "1024x1024",
            CancellationToken.None);

        result.PngBytes.Should().Equal(PngBytes());
        transport.LastRequest!.Uri.ToString().Should().Be("https://api.openai.com/v1/images/edits");
        transport.LastMethod.Should().Be(HttpTransportMethod.Post);
        transport.AuthorizationHeader.Should().Be("Bearer sk-texture");
        var parts = transport.LastMultipartParts;
        parts.Should().NotBeNull();
        PartValue(parts!, "model").Should().Be("gpt-image-2");
        PartValue(parts!, "prompt").Should().Contain("Translate visible texture text");
        PartValue(parts!, "size").Should().Be("1024x1024");
        PartValue(parts!, "quality").Should().Be("medium");
        var imagePart = parts!.Single(part => part.Name == "image");
        imagePart.IsFile.Should().BeTrue();
        imagePart.FileName.Should().Be("texture.png");
        imagePart.ContentType.Should().Be("image/png");
        imagePart.Bytes.Should().Equal(PngBytes());
    }

    [Fact]
    public async Task TestConnectionAsync_posts_real_image_edit_request_instead_of_fetching_models()
    {
        var transport = new FakeHttpTransport(_ => FakeHttpTransport.Json(
            $$"""{"data":[{"b64_json":"{{Convert.ToBase64String(PngBytes())}}"}]}"""));
        var config = TextureImageTranslationConfig.Default() with { Enabled = true };
        var client = new TextureImageEditClient(transport, () => "sk-texture");

        ProviderTestResult result = await client.TestConnectionAsync(config, CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        result.Message.Should().Contain("图片编辑接口");
        transport.LastMethod.Should().Be(HttpTransportMethod.Post);
        transport.LastRequest!.Uri.ToString().Should().Be("https://api.openai.com/v1/images/edits");
        transport.LastRequest.Uri.AbsolutePath.Should().NotContain("/models");
        var parts = transport.LastMultipartParts;
        parts.Should().NotBeNull();
        PartValue(parts!, "prompt").Should().Contain("HUnityAutoTranslator 贴图翻译连接测试");
        PartValue(parts!, "model").Should().Be("gpt-image-2");
    }

    [Fact]
    public async Task EditAsync_reports_gateway_errors_with_status_code()
    {
        var transport = new FakeHttpTransport(_ => FakeHttpTransport.Json(
            """{"error":"Invalid API key"}""", 401));
        var client = new TextureImageEditClient(transport, () => "bad-key");

        var act = () => client.EditAsync(
            TextureImageTranslationConfig.Default(),
            "prompt",
            PngBytes(),
            "1024x1024",
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*401*Invalid API key*");
    }

    [Fact]
    public async Task EditAsync_reports_network_errors()
    {
        var transport = new FakeHttpTransport(_ => FakeHttpTransport.NetworkError("连接被拒绝。"));
        var client = new TextureImageEditClient(transport, () => "key");

        var act = () => client.EditAsync(
            TextureImageTranslationConfig.Default(),
            "prompt",
            PngBytes(),
            "1024x1024",
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*连接被拒绝*");
    }

    private static string PartValue(IReadOnlyList<HttpMultipartPart> parts, string name)
    {
        return parts.Single(part => part.Name == name).Value ?? string.Empty;
    }

    private static byte[] PngBytes()
    {
        return Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+/p9sAAAAASUVORK5CYII=");
    }
}

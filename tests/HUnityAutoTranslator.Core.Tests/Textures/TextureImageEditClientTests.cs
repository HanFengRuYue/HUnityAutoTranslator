using System.Net;
using System.Text;
using FluentAssertions;
using HUnityAutoTranslator.Core.Configuration;
using HUnityAutoTranslator.Core.Textures;

namespace HUnityAutoTranslator.Core.Tests.Textures;

public sealed class TextureImageEditClientTests
{
    [Fact]
    public async Task EditAsync_posts_openai_compatible_multipart_request_and_reads_base64_png()
    {
        using var handler = new CaptureHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                $$"""{"data":[{"b64_json":"{{Convert.ToBase64String(PngBytes())}}","revised_prompt":"ok"}]}""",
                Encoding.UTF8,
                "application/json")
        });
        using var http = new HttpClient(handler);
        var config = TextureImageTranslationConfig.Default() with { Enabled = true };
        var client = new TextureImageEditClient(http, () => "sk-texture");

        var result = await client.EditAsync(
            config,
            "Translate visible texture text to Simplified Chinese.",
            PngBytes(),
            "1024x1024",
            CancellationToken.None);

        result.PngBytes.Should().Equal(PngBytes());
        handler.Request!.RequestUri!.ToString().Should().Be("http://192.168.2.10:8317/v1/images/edits");
        handler.Request.Headers.Authorization!.Scheme.Should().Be("Bearer");
        handler.Request.Headers.Authorization.Parameter.Should().Be("sk-texture");
        handler.Request.Content!.Headers.ContentType!.MediaType.Should().Be("multipart/form-data");
        var body = await handler.Request.Content.ReadAsStringAsync();
        body.Should().Contain("gpt-image-2");
        body.Should().Contain("Translate visible texture text");
        body.Should().Contain("1024x1024");
        body.Should().Contain("medium");
    }

    [Fact]
    public async Task EditAsync_reports_gateway_errors_with_status_code()
    {
        using var handler = new CaptureHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized)
        {
            Content = new StringContent("""{"error":"Invalid API key"}""", Encoding.UTF8, "application/json")
        });
        using var http = new HttpClient(handler);
        var client = new TextureImageEditClient(http, () => "bad-key");

        var act = () => client.EditAsync(
            TextureImageTranslationConfig.Default(),
            "prompt",
            PngBytes(),
            "1024x1024",
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*401*Invalid API key*");
    }

    private static byte[] PngBytes()
    {
        return Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+/p9sAAAAASUVORK5CYII=");
    }

    private sealed class CaptureHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;

        public CaptureHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
        {
            _handler = handler;
        }

        public HttpRequestMessage? Request { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            return Task.FromResult(_handler(request));
        }
    }
}

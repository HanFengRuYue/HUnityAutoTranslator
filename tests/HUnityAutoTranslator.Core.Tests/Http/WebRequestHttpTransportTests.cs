using System.Net;
using System.Text;
using FluentAssertions;
using HUnityAutoTranslator.Core.Http;

namespace HUnityAutoTranslator.Core.Tests.Http;

/// <summary>
/// 针对 <see cref="WebRequestHttpTransport"/> 的集成测试：起一个本地 HttpListener 跑真实 round-trip，
/// 覆盖 GET/POST、Authorization 头、4xx 经 WebException 解包这几条关键路径。
/// </summary>
public sealed class WebRequestHttpTransportTests
{
    [Fact]
    public async Task SendAsync_get_returns_body_and_status()
    {
        using var server = new LoopbackServer(context =>
        {
            context.Response.StatusCode = 200;
            WriteBody(context.Response, """{"ok":true}""");
        });
        using var transport = new WebRequestHttpTransport();

        var response = await transport.SendAsync(
            new HttpTransportRequest
            {
                Method = HttpTransportMethod.Get,
                Uri = server.Uri,
            },
            CancellationToken.None);

        response.IsSuccessStatusCode.Should().BeTrue();
        response.StatusCode.Should().Be(200);
        response.Body.Should().Be("""{"ok":true}""");
        response.Error.Should().BeNull();
    }

    [Fact]
    public async Task SendAsync_post_sends_json_body_and_authorization_header()
    {
        string? receivedBody = null;
        string? receivedAuth = null;
        string? receivedContentType = null;
        using var server = new LoopbackServer(context =>
        {
            receivedAuth = context.Request.Headers["Authorization"];
            receivedContentType = context.Request.ContentType;
            using (var reader = new StreamReader(context.Request.InputStream, Encoding.UTF8))
            {
                receivedBody = reader.ReadToEnd();
            }

            context.Response.StatusCode = 200;
            WriteBody(context.Response, """{"echo":"done"}""");
        });
        using var transport = new WebRequestHttpTransport();

        var response = await transport.SendAsync(
            new HttpTransportRequest
            {
                Method = HttpTransportMethod.Post,
                Uri = server.Uri,
                Headers = new[] { new HttpHeaderEntry("Authorization", "Bearer test-key") },
                StringBody = new HttpTransportStringBody
                {
                    Content = """{"input":"你好"}""",
                    ContentType = "application/json",
                },
            },
            CancellationToken.None);

        response.IsSuccessStatusCode.Should().BeTrue();
        response.Body.Should().Be("""{"echo":"done"}""");
        receivedBody.Should().Be("""{"input":"你好"}""");
        receivedAuth.Should().Be("Bearer test-key");
        receivedContentType.Should().Contain("application/json");
        receivedContentType.Should().Contain("charset=utf-8");
    }

    [Fact]
    public async Task SendAsync_unwraps_http_error_response_instead_of_throwing()
    {
        using var server = new LoopbackServer(context =>
        {
            context.Response.StatusCode = 401;
            WriteBody(context.Response, """{"error":"unauthorized"}""");
        });
        using var transport = new WebRequestHttpTransport();

        var response = await transport.SendAsync(
            new HttpTransportRequest
            {
                Method = HttpTransportMethod.Get,
                Uri = server.Uri,
            },
            CancellationToken.None);

        response.IsSuccessStatusCode.Should().BeFalse();
        response.StatusCode.Should().Be(401);
        response.Body.Should().Be("""{"error":"unauthorized"}""");
        response.Error.Should().BeNull();
    }

    [Fact]
    public async Task SendAsync_reports_network_error_when_connection_refused()
    {
        // 关掉 listener 后端口立刻拒连，验证网络错误映射到 Error 而不是抛异常。
        Uri deadUri;
        using (var server = new LoopbackServer(_ => { }))
        {
            deadUri = server.Uri;
        }

        using var transport = new WebRequestHttpTransport();
        var response = await transport.SendAsync(
            new HttpTransportRequest
            {
                Method = HttpTransportMethod.Get,
                Uri = deadUri,
                Timeout = TimeSpan.FromSeconds(5),
            },
            CancellationToken.None);

        response.IsSuccessStatusCode.Should().BeFalse();
        response.StatusCode.Should().Be(0);
        response.Error.Should().NotBeNull();
        response.Error!.Kind.Should().Be(HttpTransportErrorKind.Network);
    }

    [Fact]
    public async Task SendStreamingAsync_returns_open_body_stream()
    {
        var payload = Encoding.UTF8.GetBytes("streamed-content");
        using var server = new LoopbackServer(context =>
        {
            context.Response.StatusCode = 200;
            context.Response.OutputStream.Write(payload, 0, payload.Length);
            context.Response.OutputStream.Close();
        });
        using var transport = new WebRequestHttpTransport();

        using var response = await transport.SendStreamingAsync(
            new HttpTransportRequest
            {
                Method = HttpTransportMethod.Get,
                Uri = server.Uri,
                ResponseHeadersOnly = true,
            },
            CancellationToken.None);

        response.IsSuccessStatusCode.Should().BeTrue();
        response.StatusCode.Should().Be(200);
        using var reader = new StreamReader(response.Body, Encoding.UTF8);
        var content = await reader.ReadToEndAsync();
        content.Should().Be("streamed-content");
    }

    private static void WriteBody(HttpListenerResponse response, string body)
    {
        var bytes = Encoding.UTF8.GetBytes(body);
        response.ContentType = "application/json; charset=utf-8";
        response.ContentLength64 = bytes.Length;
        response.OutputStream.Write(bytes, 0, bytes.Length);
        response.OutputStream.Close();
    }

    private sealed class LoopbackServer : IDisposable
    {
        private readonly HttpListener _listener;
        private readonly CancellationTokenSource _cts = new();
        private readonly Task _serveTask;

        public LoopbackServer(Action<HttpListenerContext> handle)
        {
            var port = GetFreeLoopbackPort();
            Uri = new Uri($"http://127.0.0.1:{port}/");
            _listener = new HttpListener();
            _listener.Prefixes.Add(Uri.ToString());
            _listener.Start();
            _serveTask = Task.Run(() =>
            {
                while (!_cts.IsCancellationRequested)
                {
                    HttpListenerContext context;
                    try
                    {
                        context = _listener.GetContext();
                    }
                    catch (Exception)
                    {
                        return;
                    }

                    try
                    {
                        handle(context);
                    }
                    catch (Exception)
                    {
                        try
                        {
                            context.Response.Abort();
                        }
                        catch (Exception)
                        {
                            // 测试服务，吞掉。
                        }
                    }
                }
            });
        }

        public Uri Uri { get; }

        public void Dispose()
        {
            _cts.Cancel();
            try
            {
                _listener.Stop();
                _listener.Close();
            }
            catch (Exception)
            {
                // 已停止。
            }

            try
            {
                _serveTask.Wait(TimeSpan.FromSeconds(2));
            }
            catch (Exception)
            {
                // 后台线程退出无关紧要。
            }

            _cts.Dispose();
        }

        private static int GetFreeLoopbackPort()
        {
            var probe = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            var port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();
            return port;
        }
    }
}

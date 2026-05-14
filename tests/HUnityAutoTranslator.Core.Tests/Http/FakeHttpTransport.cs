using HUnityAutoTranslator.Core.Http;

namespace HUnityAutoTranslator.Core.Tests.Http;

/// <summary>
/// 测试用 <see cref="IHttpTransport"/> 桩。记录最近一次请求并返回脚本化的响应，
/// 取代旧的 HttpMessageHandler 捕获器。支持缓冲响应、流式响应和"任何调用都抛异常"三种模式。
/// </summary>
public sealed class FakeHttpTransport : IHttpTransport
{
    private readonly Func<HttpTransportRequest, HttpTransportResponse>? _buffered;
    private readonly Func<HttpTransportRequest, HttpTransportStreamResponse>? _streaming;
    private readonly bool _throwOnUse;

    public FakeHttpTransport(Func<HttpTransportRequest, HttpTransportResponse> buffered)
    {
        _buffered = buffered;
    }

    public FakeHttpTransport(Func<HttpTransportRequest, HttpTransportStreamResponse> streaming)
    {
        _streaming = streaming;
    }

    private FakeHttpTransport(bool throwOnUse)
    {
        _throwOnUse = throwOnUse;
    }

    /// <summary>任何 HTTP 调用都抛异常的桩，用于断言"不应发起网络请求"。</summary>
    public static FakeHttpTransport Throwing()
    {
        return new FakeHttpTransport(throwOnUse: true);
    }

    public HttpTransportRequest? LastRequest { get; private set; }

    public int CallCount { get; private set; }

    public string LastPath => LastRequest?.Uri.AbsolutePath ?? string.Empty;

    public string LastQuery => LastRequest?.Uri.Query ?? string.Empty;

    public HttpTransportMethod? LastMethod => LastRequest?.Method;

    public string LastStringBody => LastRequest?.StringBody?.Content ?? string.Empty;

    public string? LastStringBodyContentType => LastRequest?.StringBody?.ContentType;

    public IReadOnlyList<HttpMultipartPart>? LastMultipartParts => LastRequest?.MultipartParts;

    public string? AuthorizationHeader => Header("Authorization");

    public string? Header(string name)
    {
        if (LastRequest == null)
        {
            return null;
        }

        foreach (var entry in LastRequest.Headers)
        {
            if (string.Equals(entry.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return entry.Value;
            }
        }

        return null;
    }

    public Task<HttpTransportResponse> SendAsync(HttpTransportRequest request, CancellationToken cancellationToken)
    {
        LastRequest = request;
        CallCount++;
        cancellationToken.ThrowIfCancellationRequested();
        if (_throwOnUse)
        {
            throw new InvalidOperationException("FakeHttpTransport.Throwing：不应发起 HTTP 请求。");
        }

        if (_buffered == null)
        {
            throw new InvalidOperationException("FakeHttpTransport 是为流式响应构造的，不支持 SendAsync。");
        }

        return Task.FromResult(_buffered(request));
    }

    public Task<HttpTransportStreamResponse> SendStreamingAsync(HttpTransportRequest request, CancellationToken cancellationToken)
    {
        LastRequest = request;
        CallCount++;
        cancellationToken.ThrowIfCancellationRequested();
        if (_throwOnUse)
        {
            throw new InvalidOperationException("FakeHttpTransport.Throwing：不应发起 HTTP 请求。");
        }

        if (_streaming == null)
        {
            throw new InvalidOperationException("FakeHttpTransport 是为缓冲响应构造的，不支持 SendStreamingAsync。");
        }

        return Task.FromResult(_streaming(request));
    }

    public void Dispose()
    {
    }

    public static HttpTransportResponse Json(string body, int statusCode = 200)
    {
        return HttpTransportResponse.FromStatus(
            statusCode,
            statusCode is >= 200 and < 300 ? "OK" : "Error",
            body);
    }

    public static HttpTransportResponse NetworkError(string message)
    {
        return HttpTransportResponse.FromError(HttpTransportErrorKind.Network, message);
    }
}

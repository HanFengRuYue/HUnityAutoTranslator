namespace HUnityAutoTranslator.Core.Http;

/// <summary>
/// 缓冲响应：响应体已完整读进 <see cref="Body"/>。非流式调用方都用这个。
/// </summary>
public sealed class HttpTransportResponse
{
    public bool IsSuccessStatusCode { get; init; }

    public int StatusCode { get; init; }

    public string? ReasonPhrase { get; init; }

    public string Body { get; init; } = string.Empty;

    /// <summary>
    /// 请求在拿到 HTTP 状态码之前就失败时填充（DNS/拒连/TLS/超时）。
    /// 此时 <see cref="IsSuccessStatusCode"/> 为 false、<see cref="StatusCode"/> 为 0。
    /// </summary>
    public HttpTransportError? Error { get; init; }

    public static HttpTransportResponse FromStatus(int statusCode, string? reasonPhrase, string body)
    {
        return new HttpTransportResponse
        {
            IsSuccessStatusCode = statusCode is >= 200 and < 300,
            StatusCode = statusCode,
            ReasonPhrase = reasonPhrase,
            Body = body,
        };
    }

    public static HttpTransportResponse FromError(HttpTransportErrorKind kind, string message)
    {
        return new HttpTransportResponse
        {
            IsSuccessStatusCode = false,
            StatusCode = 0,
            Error = new HttpTransportError(kind, message),
        };
    }
}

public enum HttpTransportErrorKind
{
    None,
    Timeout,
    Canceled,
    Network,
}

public sealed class HttpTransportError
{
    public HttpTransportError(HttpTransportErrorKind kind, string message)
    {
        Kind = kind;
        Message = message;
    }

    public HttpTransportErrorKind Kind { get; }

    public string Message { get; }
}

/// <summary>
/// 流式响应：响应头已读，响应体流保持打开。调用方必须 Dispose（会一并释放底层连接/取消注册）。
/// </summary>
public sealed class HttpTransportStreamResponse : IDisposable
{
    private readonly IDisposable?[] _ownedResources;
    private bool _disposed;

    public HttpTransportStreamResponse(
        bool isSuccessStatusCode,
        int statusCode,
        string? reasonPhrase,
        long? contentLength,
        Stream body,
        params IDisposable?[] ownedResources)
    {
        IsSuccessStatusCode = isSuccessStatusCode;
        StatusCode = statusCode;
        ReasonPhrase = reasonPhrase;
        ContentLength = contentLength;
        Body = body;
        _ownedResources = ownedResources;
    }

    public bool IsSuccessStatusCode { get; }

    public int StatusCode { get; }

    public string? ReasonPhrase { get; }

    public long? ContentLength { get; }

    public Stream Body { get; }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        try
        {
            Body.Dispose();
        }
        catch (Exception)
        {
            // 流可能已被底层连接关闭，忽略。
        }

        foreach (var resource in _ownedResources)
        {
            try
            {
                resource?.Dispose();
            }
            catch (Exception)
            {
                // 释放顺序无关紧要，吞掉。
            }
        }
    }
}

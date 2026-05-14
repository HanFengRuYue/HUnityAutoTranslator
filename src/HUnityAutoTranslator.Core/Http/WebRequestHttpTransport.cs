using System.Net;
using System.Text;

namespace HUnityAutoTranslator.Core.Http;

/// <summary>
/// 基于 System.Net.HttpWebRequest 的传输实现。HttpWebRequest/WebRequest 位于 System.dll，
/// 每个 Unity Mono 运行时（含精简版 Unity 2019.x）和 net6.0(IL2CPP) 都有，因此不依赖 System.Net.Http.dll。
/// 工厂在游戏未自带 System.Net.Http.dll 时选用本实现。
/// </summary>
public sealed class WebRequestHttpTransport : IHttpTransport
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(100);

    static WebRequestHttpTransport()
    {
        // Unity 2019.4 的 Mono 默认 SecurityProtocol 可能只有 TLS1.0/1.1，在线服务商基本都强制 TLS1.2+。
        try
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            const SecurityProtocolType tls13 = (SecurityProtocolType)12288;
            if (Enum.IsDefined(typeof(SecurityProtocolType), tls13))
            {
                ServicePointManager.SecurityProtocol |= tls13;
            }
        }
        catch (NotSupportedException)
        {
            // 古早运行时不认这些值，保持默认即可。
        }
    }

    public async Task<HttpTransportResponse> SendAsync(HttpTransportRequest request, CancellationToken cancellationToken)
    {
        var timeout = request.Timeout ?? DefaultTimeout;
        CancellationTokenSource? timeoutCts = null;
        CancellationTokenSource? linkedCts = null;
        CancellationToken effectiveToken;
        if (timeout == System.Threading.Timeout.InfiniteTimeSpan)
        {
            effectiveToken = cancellationToken;
        }
        else
        {
            timeoutCts = new CancellationTokenSource(timeout);
            linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
            effectiveToken = linkedCts.Token;
        }

        var web = BuildRequest(request);
        var registration = effectiveToken.Register(() => SafeAbort(web));
        try
        {
            await WriteBodyAsync(web, request, effectiveToken).ConfigureAwait(false);
            using var response = (HttpWebResponse)await web.GetResponseAsync().ConfigureAwait(false);
            var body = await ReadBodyAsync(response).ConfigureAwait(false);
            return HttpTransportResponse.FromStatus((int)response.StatusCode, response.StatusDescription, body);
        }
        catch (WebException ex)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                throw new OperationCanceledException(cancellationToken);
            }

            if (timeoutCts != null && timeoutCts.IsCancellationRequested)
            {
                return HttpTransportResponse.FromError(HttpTransportErrorKind.Timeout, "请求超时。");
            }

            // HttpWebRequest 把 4xx/5xx 当抛出的 WebException 投递，ex.Response 才是真正的响应。
            if (ex.Response is HttpWebResponse errorResponse)
            {
                using (errorResponse)
                {
                    var body = await ReadBodyAsync(errorResponse).ConfigureAwait(false);
                    return HttpTransportResponse.FromStatus(
                        (int)errorResponse.StatusCode,
                        errorResponse.StatusDescription,
                        body);
                }
            }

            return HttpTransportResponse.FromError(HttpTransportErrorKind.Network, ex.Message);
        }
        finally
        {
            registration.Dispose();
            linkedCts?.Dispose();
            timeoutCts?.Dispose();
        }
    }

    public async Task<HttpTransportStreamResponse> SendStreamingAsync(HttpTransportRequest request, CancellationToken cancellationToken)
    {
        var timeout = request.Timeout ?? DefaultTimeout;
        CancellationTokenSource? timeoutCts = null;
        CancellationTokenSource? linkedCts = null;
        CancellationToken effectiveToken;
        if (timeout == System.Threading.Timeout.InfiniteTimeSpan)
        {
            effectiveToken = cancellationToken;
        }
        else
        {
            timeoutCts = new CancellationTokenSource(timeout);
            linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
            effectiveToken = linkedCts.Token;
        }

        var web = BuildRequest(request);
        var registration = effectiveToken.Register(() => SafeAbort(web));
        var ownershipTransferred = false;
        try
        {
            await WriteBodyAsync(web, request, effectiveToken).ConfigureAwait(false);
            var response = (HttpWebResponse)await web.GetResponseAsync().ConfigureAwait(false);
            var stream = response.GetResponseStream() ?? Stream.Null;
            var contentLength = response.ContentLength >= 0 ? response.ContentLength : (long?)null;
            var statusCode = (int)response.StatusCode;
            // response + registration + CTS 的所有权转交给流响应：流式读到一半被取消还能 Abort()。
            ownershipTransferred = true;
            return new HttpTransportStreamResponse(
                statusCode is >= 200 and < 300,
                statusCode,
                response.StatusDescription,
                contentLength,
                stream,
                response,
                registration,
                linkedCts,
                timeoutCts);
        }
        catch (WebException ex)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                throw new OperationCanceledException(cancellationToken);
            }

            if (timeoutCts != null && timeoutCts.IsCancellationRequested)
            {
                throw new IOException("请求超时。", ex);
            }

            if (ex.Response is HttpWebResponse errorResponse)
            {
                // 非 2xx：把错误响应交回去，流式调用方（模型下载）会按状态码抛自己的错误信息。
                var errorStream = errorResponse.GetResponseStream() ?? Stream.Null;
                var statusCode = (int)errorResponse.StatusCode;
                ownershipTransferred = true;
                return new HttpTransportStreamResponse(
                    false,
                    statusCode,
                    errorResponse.StatusDescription,
                    errorResponse.ContentLength >= 0 ? errorResponse.ContentLength : (long?)null,
                    errorStream,
                    errorResponse,
                    registration,
                    linkedCts,
                    timeoutCts);
            }

            throw new IOException(ex.Message, ex);
        }
        finally
        {
            if (!ownershipTransferred)
            {
                registration.Dispose();
                linkedCts?.Dispose();
                timeoutCts?.Dispose();
            }
        }
    }

    private static HttpWebRequest BuildRequest(HttpTransportRequest request)
    {
        var web = (HttpWebRequest)WebRequest.Create(request.Uri);
        web.Method = request.Method == HttpTransportMethod.Post ? "POST" : "GET";
        web.AllowAutoRedirect = true;
        web.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
        // 内建 Timeout 只对同步调用生效；这里统一用 CTS + Abort() 控制，禁用内建超时避免叠加误杀大文件下载。
        web.Timeout = int.MaxValue;
        web.ReadWriteTimeout = int.MaxValue;

        foreach (var header in request.Headers)
        {
            ApplyHeader(web, header.Name, header.Value);
        }

        return web;
    }

    private static void ApplyHeader(HttpWebRequest web, string name, string value)
    {
        // 受限头必须走属性，否则 HttpWebRequest 的 Headers 索引器抛 ArgumentException。
        if (string.Equals(name, "User-Agent", StringComparison.OrdinalIgnoreCase))
        {
            web.UserAgent = value;
            return;
        }

        if (string.Equals(name, "Content-Type", StringComparison.OrdinalIgnoreCase))
        {
            web.ContentType = value;
            return;
        }

        if (string.Equals(name, "Accept", StringComparison.OrdinalIgnoreCase))
        {
            web.Accept = value;
            return;
        }

        // Authorization 和自定义头不受限，走索引器即可。
        web.Headers[name] = value;
    }

    private static async Task WriteBodyAsync(HttpWebRequest web, HttpTransportRequest request, CancellationToken cancellationToken)
    {
        byte[] bodyBytes;
        string contentType;
        if (request.StringBody != null)
        {
            contentType = request.StringBody.ContentType + "; charset=utf-8";
            bodyBytes = Encoding.UTF8.GetBytes(request.StringBody.Content);
        }
        else if (request.MultipartParts != null)
        {
            bodyBytes = BuildMultipartBody(request.MultipartParts, out contentType);
        }
        else
        {
            return;
        }

        web.ContentType = contentType;
        web.ContentLength = bodyBytes.Length;
        using var requestStream = await web.GetRequestStreamAsync().ConfigureAwait(false);
        await requestStream.WriteAsync(bodyBytes, 0, bodyBytes.Length, cancellationToken).ConfigureAwait(false);
        await requestStream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static byte[] BuildMultipartBody(IReadOnlyList<HttpMultipartPart> parts, out string contentType)
    {
        var boundary = "----HUnityFormBoundary" + Guid.NewGuid().ToString("N");
        contentType = "multipart/form-data; boundary=" + boundary;
        using var buffer = new MemoryStream();

        foreach (var part in parts)
        {
            WriteAscii(buffer, "--" + boundary + "\r\n");
            if (part.IsFile)
            {
                WriteAscii(
                    buffer,
                    $"Content-Disposition: form-data; name=\"{part.Name}\"; filename=\"{part.FileName}\"\r\n");
                WriteAscii(buffer, $"Content-Type: {part.ContentType}\r\n\r\n");
                buffer.Write(part.Bytes!, 0, part.Bytes!.Length);
                WriteAscii(buffer, "\r\n");
            }
            else
            {
                WriteAscii(buffer, $"Content-Disposition: form-data; name=\"{part.Name}\"\r\n\r\n");
                var valueBytes = Encoding.UTF8.GetBytes(part.Value ?? string.Empty);
                buffer.Write(valueBytes, 0, valueBytes.Length);
                WriteAscii(buffer, "\r\n");
            }
        }

        WriteAscii(buffer, "--" + boundary + "--\r\n");
        return buffer.ToArray();
    }

    private static void WriteAscii(Stream stream, string text)
    {
        var bytes = Encoding.ASCII.GetBytes(text);
        stream.Write(bytes, 0, bytes.Length);
    }

    private static async Task<string> ReadBodyAsync(HttpWebResponse response)
    {
        var stream = response.GetResponseStream();
        if (stream == null)
        {
            return string.Empty;
        }

        using (stream)
        using (var reader = new StreamReader(stream, Encoding.UTF8))
        {
            return await reader.ReadToEndAsync().ConfigureAwait(false);
        }
    }

    private static void SafeAbort(HttpWebRequest web)
    {
        try
        {
            web.Abort();
        }
        catch (Exception)
        {
            // 请求可能已经完成，Abort 抛错无所谓。
        }
    }

    public void Dispose()
    {
        // 无状态，无需释放。
    }
}

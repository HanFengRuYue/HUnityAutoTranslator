using System.Net.Http;
using System.Text;

namespace HUnityAutoTranslator.Core.Http;

/// <summary>
/// 基于 System.Net.Http.HttpClient 的传输实现。这是已经在 Unity 2021.3+/Unity 6/IL2CPP 上验证过的路径，
/// 各 provider 原来的逻辑原样集中到这里。仅在游戏运行时能可靠提供 System.Net.Http 时由工厂选用。
/// </summary>
public sealed class HttpClientHttpTransport : IHttpTransport
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(100);

    private readonly HttpClient _httpClient;

    public HttpClientHttpTransport()
    {
        // 每个请求自己用 CTS 控制超时，HttpClient 的内建超时禁用以免叠加。
        _httpClient = new HttpClient
        {
            Timeout = System.Threading.Timeout.InfiniteTimeSpan,
        };
    }

    public async Task<HttpTransportResponse> SendAsync(HttpTransportRequest request, CancellationToken cancellationToken)
    {
        using var message = BuildMessage(request);
        var (effectiveToken, timeoutCts, linkedCts) = CreateToken(request, cancellationToken);
        try
        {
            using var response = await _httpClient
                .SendAsync(message, HttpCompletionOption.ResponseContentRead, effectiveToken)
                .ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            return HttpTransportResponse.FromStatus((int)response.StatusCode, response.ReasonPhrase, body);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            return HttpTransportResponse.FromError(HttpTransportErrorKind.Timeout, "请求超时。");
        }
        catch (HttpRequestException ex)
        {
            return HttpTransportResponse.FromError(HttpTransportErrorKind.Network, ex.Message);
        }
        finally
        {
            linkedCts?.Dispose();
            timeoutCts?.Dispose();
        }
    }

    public async Task<HttpTransportStreamResponse> SendStreamingAsync(HttpTransportRequest request, CancellationToken cancellationToken)
    {
        var message = BuildMessage(request);
        var (effectiveToken, timeoutCts, linkedCts) = CreateToken(request, cancellationToken);
        HttpResponseMessage? response = null;
        try
        {
            response = await _httpClient
                .SendAsync(message, HttpCompletionOption.ResponseHeadersRead, effectiveToken)
                .ConfigureAwait(false);
            var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
            return new HttpTransportStreamResponse(
                response.IsSuccessStatusCode,
                (int)response.StatusCode,
                response.ReasonPhrase,
                response.Content.Headers.ContentLength,
                stream,
                response,
                message,
                linkedCts,
                timeoutCts);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            response?.Dispose();
            message.Dispose();
            linkedCts?.Dispose();
            timeoutCts?.Dispose();
            throw;
        }
        catch (Exception ex) when (ex is OperationCanceledException or HttpRequestException)
        {
            response?.Dispose();
            message.Dispose();
            linkedCts?.Dispose();
            timeoutCts?.Dispose();
            throw new IOException(ex.Message, ex);
        }
    }

    private static (CancellationToken Token, CancellationTokenSource? TimeoutCts, CancellationTokenSource? LinkedCts) CreateToken(
        HttpTransportRequest request,
        CancellationToken cancellationToken)
    {
        var timeout = request.Timeout ?? DefaultTimeout;
        if (timeout == System.Threading.Timeout.InfiniteTimeSpan)
        {
            return (cancellationToken, null, null);
        }

        var timeoutCts = new CancellationTokenSource(timeout);
        var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
        return (linkedCts.Token, timeoutCts, linkedCts);
    }

    private static HttpRequestMessage BuildMessage(HttpTransportRequest request)
    {
        var method = request.Method == HttpTransportMethod.Post ? HttpMethod.Post : HttpMethod.Get;
        var message = new HttpRequestMessage(method, request.Uri);

        foreach (var header in request.Headers)
        {
            message.Headers.TryAddWithoutValidation(header.Name, header.Value);
        }

        if (request.StringBody != null)
        {
            message.Content = new StringContent(
                request.StringBody.Content,
                Encoding.UTF8,
                request.StringBody.ContentType);
        }
        else if (request.MultipartParts != null)
        {
            var multipart = new MultipartFormDataContent();
            foreach (var part in request.MultipartParts)
            {
                if (part.IsFile)
                {
                    var fileContent = new ByteArrayContent(part.Bytes!);
                    if (!string.IsNullOrWhiteSpace(part.ContentType))
                    {
                        fileContent.Headers.ContentType =
                            new System.Net.Http.Headers.MediaTypeHeaderValue(part.ContentType!);
                    }

                    multipart.Add(fileContent, part.Name, part.FileName ?? part.Name);
                }
                else
                {
                    multipart.Add(new StringContent(part.Value ?? string.Empty), part.Name);
                }
            }

            message.Content = multipart;
        }

        return message;
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }
}

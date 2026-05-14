namespace HUnityAutoTranslator.Core.Http;

/// <summary>
/// 出站 HTTP 抽象。两套实现：HttpClientHttpTransport（System.Net.Http，已验证路径）和
/// WebRequestHttpTransport（System.Net.HttpWebRequest，给精简 Mono 的 Unity 2019.x 回退用）。
/// 公共表面不出现任何 System.Net.Http 类型，调用方对两种运行环境一视同仁。
/// </summary>
public interface IHttpTransport : IDisposable
{
    /// <summary>
    /// 发送请求并把整个响应体读进字符串。网络层失败（DNS/拒连/TLS/超时）回填到
    /// <see cref="HttpTransportResponse.Error"/> 而不抛异常；只有调用方自己的 token 取消时才抛
    /// <see cref="OperationCanceledException"/>。
    /// </summary>
    Task<HttpTransportResponse> SendAsync(HttpTransportRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// 发送请求，响应头到达即返回，响应体流保持打开由调用方读取并 Dispose。仅用于模型下载。
    /// </summary>
    Task<HttpTransportStreamResponse> SendStreamingAsync(HttpTransportRequest request, CancellationToken cancellationToken);
}

public enum HttpTransportMethod
{
    Get,
    Post,
}

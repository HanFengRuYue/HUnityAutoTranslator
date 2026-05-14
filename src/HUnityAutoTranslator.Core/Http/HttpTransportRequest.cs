namespace HUnityAutoTranslator.Core.Http;

/// <summary>
/// 出站 HTTP 请求 DTO。纯数据，不含任何 System.Net.Http 类型。
/// </summary>
public sealed class HttpTransportRequest
{
    public HttpTransportMethod Method { get; init; } = HttpTransportMethod.Get;

    public Uri Uri { get; init; } = null!;

    /// <summary>有序头列表。Authorization 与自定义头都放这里（名称不区分大小写由实现处理）。</summary>
    public IReadOnlyList<HttpHeaderEntry> Headers { get; init; } = Array.Empty<HttpHeaderEntry>();

    /// <summary>字符串请求体（JSON 等）。与 <see cref="MultipartParts"/> 互斥，GET 时为 null。</summary>
    public HttpTransportStringBody? StringBody { get; init; }

    /// <summary>multipart/form-data 请求体。与 <see cref="StringBody"/> 互斥。</summary>
    public IReadOnlyList<HttpMultipartPart>? MultipartParts { get; init; }

    /// <summary>true 表示响应头到达即返回，响应体留给调用方流式读取（仅 SendStreamingAsync 用）。</summary>
    public bool ResponseHeadersOnly { get; init; }

    /// <summary>
    /// 请求超时。null 表示用实现默认值；<see cref="System.Threading.Timeout.InfiniteTimeSpan"/>
    /// 表示不设超时（多 GB 模型下载用）。
    /// </summary>
    public TimeSpan? Timeout { get; init; }
}

public sealed class HttpHeaderEntry
{
    public HttpHeaderEntry(string name, string value)
    {
        Name = name;
        Value = value;
    }

    public string Name { get; }

    public string Value { get; }
}

public sealed class HttpTransportStringBody
{
    public string Content { get; init; } = string.Empty;

    /// <summary>不含 charset；实现会自行追加 "; charset=utf-8"。</summary>
    public string ContentType { get; init; } = "application/json";
}

/// <summary>
/// multipart/form-data 的一个部分：文本部分（设 <see cref="Value"/>）或二进制部分
/// （设 <see cref="Bytes"/> + <see cref="FileName"/> + <see cref="ContentType"/>）。
/// </summary>
public sealed class HttpMultipartPart
{
    public string Name { get; private init; } = string.Empty;

    public string? Value { get; private init; }

    public byte[]? Bytes { get; private init; }

    public string? FileName { get; private init; }

    public string? ContentType { get; private init; }

    public bool IsFile => Bytes != null;

    public static HttpMultipartPart Text(string name, string value)
    {
        return new HttpMultipartPart { Name = name, Value = value };
    }

    public static HttpMultipartPart File(string name, byte[] bytes, string fileName, string contentType)
    {
        return new HttpMultipartPart
        {
            Name = name,
            Bytes = bytes,
            FileName = fileName,
            ContentType = contentType,
        };
    }
}

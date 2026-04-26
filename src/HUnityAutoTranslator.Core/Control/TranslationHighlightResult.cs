namespace HUnityAutoTranslator.Core.Control;

public sealed record TranslationHighlightResult(
    string Status,
    string Message,
    string? TargetId = null)
{
    public static TranslationHighlightResult Queued(string targetId)
    {
        return new TranslationHighlightResult("queued", "已发送高亮请求。", targetId);
    }

    public static TranslationHighlightResult TargetNotFound()
    {
        return new TranslationHighlightResult("not_found", "当前场景中没有找到匹配的文本组件。");
    }

    public static TranslationHighlightResult UnsupportedTarget()
    {
        return new TranslationHighlightResult("unsupported", "当前文本类型暂不支持定位高亮。");
    }
}

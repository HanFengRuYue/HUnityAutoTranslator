namespace HUnityAutoTranslator.Core.Control;

public sealed record FontPickResult(string Status, string? FilePath, string? FontName, string Message)
{
    public static FontPickResult Selected(string filePath, string fontName)
    {
        return new FontPickResult("selected", filePath, fontName, "已选择字体文件。");
    }

    public static FontPickResult Cancelled()
    {
        return new FontPickResult("cancelled", null, null, "已取消选择字体文件。");
    }

    public static FontPickResult Unsupported()
    {
        return new FontPickResult("unsupported", null, null, "当前系统不支持从控制面板打开字体文件选择器。");
    }

    public static FontPickResult Error(string message)
    {
        return new FontPickResult("error", null, null, string.IsNullOrWhiteSpace(message) ? "打开字体文件选择器失败。" : message);
    }
}

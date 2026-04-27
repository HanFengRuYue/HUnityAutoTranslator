namespace HUnityAutoTranslator.Core.Control;

public sealed record LlamaCppModelPickResult(
    string Status,
    string? FilePath,
    string Message)
{
    public static LlamaCppModelPickResult Selected(string filePath)
    {
        return new LlamaCppModelPickResult("selected", filePath, "已选择 GGUF 模型文件。");
    }

    public static LlamaCppModelPickResult Cancelled()
    {
        return new LlamaCppModelPickResult("cancelled", null, "已取消选择。");
    }

    public static LlamaCppModelPickResult Unsupported()
    {
        return new LlamaCppModelPickResult("unsupported", null, "当前系统不支持本地文件选择。");
    }

    public static LlamaCppModelPickResult Error(string message)
    {
        return new LlamaCppModelPickResult("error", null, message);
    }
}

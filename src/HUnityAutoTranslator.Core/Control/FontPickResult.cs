namespace HUnityAutoTranslator.Core.Control;

public sealed record FontPickResult(string Status, string? FilePath, string? FontName, string Message)
{
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".ttf",
        ".otf"
    };

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

    public FontPickResult CopyToDirectory(string directory)
    {
        if (!string.Equals(Status, "selected", StringComparison.OrdinalIgnoreCase))
        {
            return this;
        }

        if (string.IsNullOrWhiteSpace(FilePath) || !File.Exists(FilePath))
        {
            return Error("所选字体文件不存在。");
        }

        var extension = Path.GetExtension(FilePath);
        if (!SupportedExtensions.Contains(extension))
        {
            return Error("仅支持 .ttf 或 .otf 字体文件。");
        }

        Directory.CreateDirectory(directory);
        var targetPath = CreateUniqueTargetPath(directory, FilePath, extension);
        File.Copy(FilePath, targetPath, overwrite: false);
        return this with
        {
            FilePath = targetPath,
            Message = "已选择并复制字体文件。"
        };
    }

    private static string CreateUniqueTargetPath(string directory, string sourcePath, string extension)
    {
        var baseName = SanitizeFileName(Path.GetFileNameWithoutExtension(sourcePath));
        var targetPath = Path.Combine(directory, baseName + extension);
        for (var index = 1; File.Exists(targetPath); index++)
        {
            targetPath = Path.Combine(directory, $"{baseName}-{index}{extension}");
        }

        return targetPath;
    }

    private static string SanitizeFileName(string value)
    {
        var invalidCharacters = Path.GetInvalidFileNameChars();
        var sanitized = new string((string.IsNullOrWhiteSpace(value) ? "font" : value.Trim())
            .Select(character => invalidCharacters.Contains(character) ? '_' : character)
            .ToArray());
        return string.IsNullOrWhiteSpace(sanitized) ? "font" : sanitized;
    }
}

namespace HUnityAutoTranslator.Core.Providers;

public static class LlamaCppProcessPath
{
    public static string? NormalizeModelPathForWorkingDirectory(string? modelPath, string workingDirectory)
    {
        if (string.IsNullOrWhiteSpace(modelPath) || string.IsNullOrWhiteSpace(workingDirectory))
        {
            return modelPath;
        }

        if (!Path.IsPathRooted(modelPath))
        {
            return modelPath;
        }

        try
        {
            var modelRoot = Path.GetPathRoot(modelPath);
            var workingRoot = Path.GetPathRoot(workingDirectory);
            if (string.IsNullOrWhiteSpace(modelRoot) ||
                string.IsNullOrWhiteSpace(workingRoot) ||
                !string.Equals(modelRoot, workingRoot, StringComparison.OrdinalIgnoreCase))
            {
                return modelPath;
            }

            var fullModelPath = Path.GetFullPath(modelPath);
            var fullWorkingDirectory = EnsureTrailingDirectorySeparator(Path.GetFullPath(workingDirectory));
            var workingUri = new Uri(fullWorkingDirectory);
            var modelUri = new Uri(fullModelPath);
            return Uri.UnescapeDataString(workingUri.MakeRelativeUri(modelUri).ToString())
                .Replace('/', Path.DirectorySeparatorChar);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException or UriFormatException)
        {
            return modelPath;
        }
    }

    private static string EnsureTrailingDirectorySeparator(string path)
    {
        return path.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal) ||
            path.EndsWith(Path.AltDirectorySeparatorChar.ToString(), StringComparison.Ordinal)
            ? path
            : path + Path.DirectorySeparatorChar;
    }
}

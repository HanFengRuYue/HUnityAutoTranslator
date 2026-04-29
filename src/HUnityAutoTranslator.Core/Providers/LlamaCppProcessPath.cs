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
            var fullWorkingDirectory = Path.GetFullPath(workingDirectory);
            return Path.GetRelativePath(fullWorkingDirectory, fullModelPath);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return modelPath;
        }
    }
}

using System.IO.Compression;

namespace HUnityAutoTranslator.Toolbox.Core.Installation;

public static class SafeZipExtractor
{
    public static string GetSafeDestinationPath(string destinationRoot, string entryName)
    {
        if (string.IsNullOrWhiteSpace(entryName))
        {
            throw new InvalidOperationException("Zip entry name is empty.");
        }

        var root = Path.GetFullPath(destinationRoot);
        var destinationPath = Path.GetFullPath(Path.Combine(root, entryName.Replace('/', Path.DirectorySeparatorChar)));
        var rootWithSeparator = root.EndsWith(Path.DirectorySeparatorChar)
            ? root
            : root + Path.DirectorySeparatorChar;

        if (!destinationPath.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(destinationPath, root, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Zip entry escapes the destination directory: {entryName}");
        }

        return destinationPath;
    }

    public static IReadOnlyList<string> ExtractToDirectory(string archivePath, string destinationRoot, bool overwrite)
    {
        var written = new List<string>();
        using var archive = ZipFile.OpenRead(archivePath);
        foreach (var entry in archive.Entries)
        {
            var destinationPath = GetSafeDestinationPath(destinationRoot, entry.FullName);
            if (entry.FullName.EndsWith("/", StringComparison.Ordinal) ||
                entry.FullName.EndsWith("\\", StringComparison.Ordinal))
            {
                Directory.CreateDirectory(destinationPath);
                continue;
            }

            var directory = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            entry.ExtractToFile(destinationPath, overwrite);
            written.Add(destinationPath);
        }

        return written;
    }
}

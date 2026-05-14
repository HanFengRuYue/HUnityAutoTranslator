using System.IO.Compression;

namespace HUnityAutoTranslator.Toolbox.Core.Installation;

public static class SafeZipExtractor
{
    private const int CopyBufferSize = 64 * 1024;

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
        using var stream = File.OpenRead(archivePath);
        return ExtractToDirectory(stream, destinationRoot, overwrite, protectedAbsolutePaths: null, onEntryWritten: null, CancellationToken.None);
    }

    public static IReadOnlyList<string> ExtractToDirectory(
        Stream archiveStream,
        string destinationRoot,
        bool overwrite,
        IReadOnlySet<string>? protectedAbsolutePaths,
        Action<string>? onEntryWritten,
        CancellationToken cancellationToken)
    {
        var written = new List<string>();
        using var archive = new ZipArchive(archiveStream, ZipArchiveMode.Read, leaveOpen: false);
        foreach (var entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var destinationPath = GetSafeDestinationPath(destinationRoot, entry.FullName);
            if (entry.FullName.EndsWith("/", StringComparison.Ordinal) ||
                entry.FullName.EndsWith("\\", StringComparison.Ordinal))
            {
                Directory.CreateDirectory(destinationPath);
                continue;
            }

            if (protectedAbsolutePaths is not null && protectedAbsolutePaths.Contains(destinationPath))
            {
                onEntryWritten?.Invoke($"skipped:{destinationPath}");
                continue;
            }

            var directory = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            ExtractEntryWithCancellation(entry, destinationPath, overwrite, cancellationToken);
            written.Add(destinationPath);
            onEntryWritten?.Invoke($"written:{destinationPath}");
        }

        return written;
    }

    private static void ExtractEntryWithCancellation(ZipArchiveEntry entry, string destinationPath, bool overwrite, CancellationToken cancellationToken)
    {
        if (File.Exists(destinationPath))
        {
            if (!overwrite)
            {
                throw new IOException($"Destination file already exists: {destinationPath}");
            }
            File.Delete(destinationPath);
        }

        using var source = entry.Open();
        using var destination = new FileStream(destinationPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);

        var buffer = new byte[CopyBufferSize];
        int read;
        while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            destination.Write(buffer, 0, read);
        }
    }
}

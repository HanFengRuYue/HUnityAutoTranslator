using Newtonsoft.Json;

namespace HUnityAutoTranslator.Core.Textures;

public sealed class TextureCatalogStore
{
    private const string IndexFileName = "catalog.json";
    private const string SourcesDirectoryName = "sources";
    private const int DefaultLimit = 20;
    private const int MaxLimit = 500;

    private readonly object _gate = new();
    private readonly string _directory;
    private readonly string _sourcesDirectory;
    private readonly string _indexPath;
    private readonly Dictionary<string, PersistedTextureCatalogItem> _items = new(StringComparer.OrdinalIgnoreCase);
    private bool _dirty;

    public TextureCatalogStore(string directory)
    {
        _directory = directory;
        _sourcesDirectory = Path.Combine(_directory, SourcesDirectoryName);
        _indexPath = Path.Combine(_directory, IndexFileName);
        Directory.CreateDirectory(_sourcesDirectory);
        Load();
    }

    public void Upsert(TextureCatalogItem item, byte[] sourcePngBytes)
    {
        if (string.IsNullOrWhiteSpace(item.SourceHash))
        {
            return;
        }

        lock (_gate)
        {
            if (!_items.TryGetValue(item.SourceHash, out var persisted))
            {
                persisted = new PersistedTextureCatalogItem
                {
                    SourceHash = item.SourceHash,
                    TextureName = item.TextureName,
                    Width = item.Width,
                    Height = item.Height,
                    Format = item.Format,
                    FileName = item.FileName,
                    References = Array.Empty<TextureReferenceInfo>(),
                    UpdatedUtc = DateTimeOffset.UtcNow
                };
                _items[item.SourceHash] = persisted;
            }

            persisted.TextureName = PreferText(persisted.TextureName, item.TextureName);
            persisted.Width = item.Width > 0 ? item.Width : persisted.Width;
            persisted.Height = item.Height > 0 ? item.Height : persisted.Height;
            persisted.Format = PreferText(persisted.Format, item.Format);
            persisted.FileName = TextureArchiveNaming.IsSafeArchivePath(item.FileName)
                ? item.FileName
                : TextureArchiveNaming.BuildTextureEntryName(item.SourceHash, item.TextureName);
            persisted.References = MergeReferences(persisted.References, item.References);
            persisted.UpdatedUtc = DateTimeOffset.UtcNow;

            if (sourcePngBytes.Length > 0)
            {
                var sourcePath = SourcePath(item.SourceHash);
                if (!File.Exists(sourcePath))
                {
                    File.WriteAllBytes(sourcePath, sourcePngBytes);
                }
            }

            _dirty = true;
        }
    }

    public TextureCatalogQueryResult Query(TextureCatalogQuery query, TextureOverrideIndex overrides)
    {
        lock (_gate)
        {
            var offset = Math.Max(0, query.Offset);
            var limit = NormalizeLimit(query.Limit);
            var overrideByHash = overrides.Records.ToDictionary(record => record.SourceHash, StringComparer.OrdinalIgnoreCase);
            var allItems = _items.Values
                .OrderBy(item => item.TextureName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.SourceHash, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var scenes = allItems
                .SelectMany(item => item.References ?? Array.Empty<TextureReferenceInfo>())
                .Select(reference => reference.SceneName)
                .Where(scene => !string.IsNullOrWhiteSpace(scene))
                .Select(scene => scene!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(scene => scene, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var totalReferenceCount = allItems.Sum(item => (item.References ?? Array.Empty<TextureReferenceInfo>()).Length);
            var filteredItems = allItems
                .Select(item => ToCatalogItem(item, overrideByHash, query.SceneName))
                .Where(item => item.ReferenceCount > 0)
                .ToArray();
            var pageItems = filteredItems
                .Skip(offset)
                .Take(limit)
                .ToArray();

            return new TextureCatalogQueryResult(
                allItems.Length,
                filteredItems.Length,
                totalReferenceCount,
                offset,
                limit,
                scenes,
                pageItems);
        }
    }

    public IReadOnlyList<TextureCatalogItem> GetItemsForExport(string? sceneName, TextureOverrideIndex overrides)
    {
        return Query(new TextureCatalogQuery(sceneName, 0, int.MaxValue), overrides).Items;
    }

    public bool TryReadSourceBytes(string sourceHash, out byte[] bytes)
    {
        bytes = Array.Empty<byte>();
        if (!IsSafeSourceHash(sourceHash))
        {
            return false;
        }

        var path = SourcePath(sourceHash);
        if (!File.Exists(path))
        {
            return false;
        }

        bytes = File.ReadAllBytes(path);
        return bytes.Length > 0;
    }

    public void Save()
    {
        lock (_gate)
        {
            if (!_dirty)
            {
                return;
            }

            Directory.CreateDirectory(_directory);
            Directory.CreateDirectory(_sourcesDirectory);
            var index = new PersistedTextureCatalogIndex(
                _items.Values
                    .OrderBy(item => item.TextureName, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(item => item.SourceHash, StringComparer.OrdinalIgnoreCase)
                    .ToArray());
            File.WriteAllText(_indexPath, JsonConvert.SerializeObject(index, Formatting.Indented));
            _dirty = false;
        }
    }

    private void Load()
    {
        if (!File.Exists(_indexPath))
        {
            return;
        }

        try
        {
            var index = JsonConvert.DeserializeObject<PersistedTextureCatalogIndex>(File.ReadAllText(_indexPath));
            if (index?.Items == null)
            {
                return;
            }

            foreach (var item in index.Items)
            {
                if (string.IsNullOrWhiteSpace(item.SourceHash))
                {
                    continue;
                }

                item.References = DeduplicateReferences(item.References ?? Array.Empty<TextureReferenceInfo>());
                _items[item.SourceHash] = item;
            }
        }
        catch
        {
            _items.Clear();
        }
    }

    private TextureCatalogItem ToCatalogItem(
        PersistedTextureCatalogItem item,
        IReadOnlyDictionary<string, TextureOverrideRecord> overrides,
        string? sceneName)
    {
        var references = FilterReferences(item.References ?? Array.Empty<TextureReferenceInfo>(), sceneName);
        overrides.TryGetValue(item.SourceHash, out var overrideRecord);
        return new TextureCatalogItem(
            item.SourceHash,
            item.TextureName,
            item.Width,
            item.Height,
            item.Format,
            TextureArchiveNaming.IsSafeArchivePath(item.FileName)
                ? item.FileName
                : TextureArchiveNaming.BuildTextureEntryName(item.SourceHash, item.TextureName),
            references.Length,
            references,
            overrideRecord != null,
            overrideRecord?.UpdatedUtc);
    }

    private string SourcePath(string sourceHash)
    {
        return Path.Combine(_sourcesDirectory, TextureArchiveNaming.BuildOverrideFileName(sourceHash));
    }

    private static int NormalizeLimit(int limit)
    {
        if (limit <= 0)
        {
            return DefaultLimit;
        }

        if (limit == int.MaxValue)
        {
            return int.MaxValue;
        }

        return Math.Min(MaxLimit, limit);
    }

    private static string PreferText(string? current, string? incoming)
    {
        return string.IsNullOrWhiteSpace(incoming)
            ? current ?? string.Empty
            : incoming;
    }

    private static TextureReferenceInfo[] MergeReferences(
        IReadOnlyList<TextureReferenceInfo>? existing,
        IReadOnlyList<TextureReferenceInfo>? incoming)
    {
        return DeduplicateReferences((existing ?? Array.Empty<TextureReferenceInfo>())
            .Concat(incoming ?? Array.Empty<TextureReferenceInfo>())
            .ToArray());
    }

    private static TextureReferenceInfo[] DeduplicateReferences(IReadOnlyList<TextureReferenceInfo> references)
    {
        return references
            .Where(reference => reference != null)
            .GroupBy(ReferenceKey, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(reference => reference.SceneName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(reference => reference.ComponentHierarchy, StringComparer.OrdinalIgnoreCase)
            .ThenBy(reference => reference.ComponentType, StringComparer.OrdinalIgnoreCase)
            .ThenBy(reference => reference.TargetId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static TextureReferenceInfo[] FilterReferences(IReadOnlyList<TextureReferenceInfo> references, string? sceneName)
    {
        if (string.IsNullOrWhiteSpace(sceneName))
        {
            return references.ToArray();
        }

        return references
            .Where(reference => string.Equals(reference.SceneName, sceneName, StringComparison.OrdinalIgnoreCase))
            .ToArray();
    }

    private static string ReferenceKey(TextureReferenceInfo reference)
    {
        var scene = reference.SceneName ?? string.Empty;
        var hierarchy = reference.ComponentHierarchy ?? string.Empty;
        var componentType = reference.ComponentType ?? string.Empty;
        if (scene.Length > 0 || hierarchy.Length > 0 || componentType.Length > 0)
        {
            return string.Join("|", scene, hierarchy, componentType);
        }

        return reference.TargetId ?? string.Empty;
    }

    private static bool IsSafeSourceHash(string sourceHash)
    {
        return !string.IsNullOrWhiteSpace(sourceHash) &&
            sourceHash.Length <= 64 &&
            sourceHash.All(char.IsLetterOrDigit);
    }

    private sealed record PersistedTextureCatalogIndex(PersistedTextureCatalogItem[] Items);

    private sealed class PersistedTextureCatalogItem
    {
        public string SourceHash { get; set; } = string.Empty;

        public string TextureName { get; set; } = string.Empty;

        public int Width { get; set; }

        public int Height { get; set; }

        public string Format { get; set; } = string.Empty;

        public string FileName { get; set; } = string.Empty;

        public TextureReferenceInfo[] References { get; set; } = Array.Empty<TextureReferenceInfo>();

        public DateTimeOffset UpdatedUtc { get; set; }
    }
}

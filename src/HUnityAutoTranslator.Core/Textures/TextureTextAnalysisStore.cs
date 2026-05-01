using Newtonsoft.Json;

namespace HUnityAutoTranslator.Core.Textures;

public sealed class TextureTextAnalysisStore
{
    private readonly object _gate = new();
    private readonly string _path;
    private readonly Dictionary<string, TextureTextAnalysis> _items = new(StringComparer.OrdinalIgnoreCase);

    public TextureTextAnalysisStore(string path)
    {
        _path = path;
        Load();
    }

    public bool TryGet(string sourceHash, out TextureTextAnalysis? analysis)
    {
        lock (_gate)
        {
            return _items.TryGetValue(sourceHash, out analysis);
        }
    }

    public TextureTextAnalysis GetOrUnknown(string sourceHash)
    {
        lock (_gate)
        {
            return _items.TryGetValue(sourceHash, out var analysis)
                ? analysis
                : TextureTextAnalysis.Unknown(sourceHash);
        }
    }

    public IReadOnlyDictionary<string, TextureTextAnalysis> Snapshot()
    {
        lock (_gate)
        {
            return new Dictionary<string, TextureTextAnalysis>(_items, StringComparer.OrdinalIgnoreCase);
        }
    }

    public TextureTextAnalysis Upsert(TextureTextAnalysis analysis)
    {
        if (string.IsNullOrWhiteSpace(analysis.SourceHash))
        {
            throw new ArgumentException("Texture source hash must not be empty.", nameof(analysis));
        }

        lock (_gate)
        {
            _items[analysis.SourceHash] = analysis;
            Save();
            return analysis;
        }
    }

    public TextureTextAnalysis Mark(string sourceHash, TextureTextStatus status, DateTimeOffset updatedUtc)
    {
        lock (_gate)
        {
            var current = _items.TryGetValue(sourceHash, out var existing)
                ? existing
                : TextureTextAnalysis.Unknown(sourceHash);
            var marked = current with
            {
                SourceHash = sourceHash,
                Status = status,
                NeedsManualReview = status == TextureTextStatus.NeedsManualReview,
                UserReviewed = true,
                UpdatedUtc = updatedUtc,
                LastError = null
            };
            _items[sourceHash] = marked;
            Save();
            return marked;
        }
    }

    private void Load()
    {
        if (!File.Exists(_path))
        {
            return;
        }

        try
        {
            var json = File.ReadAllText(_path);
            var items = json.TrimStart().StartsWith("[", StringComparison.Ordinal)
                ? JsonConvert.DeserializeObject<TextureTextAnalysis[]>(json) ?? Array.Empty<TextureTextAnalysis>()
                : JsonConvert.DeserializeObject<TextureTextAnalysisIndex>(json)?.Items ?? Array.Empty<TextureTextAnalysis>();
            foreach (var item in items)
            {
                if (!string.IsNullOrWhiteSpace(item.SourceHash))
                {
                    _items[item.SourceHash] = item;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            _items.Clear();
        }
    }

    private void Save()
    {
        var directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var index = new TextureTextAnalysisIndex(_items.Values
            .OrderBy(item => item.SourceHash, StringComparer.OrdinalIgnoreCase)
            .ToArray());
        File.WriteAllText(_path, JsonConvert.SerializeObject(index, Formatting.Indented));
    }

    private sealed record TextureTextAnalysisIndex(TextureTextAnalysis[] Items);
}

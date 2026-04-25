using System.Collections.Concurrent;
using System.Text;

namespace HUnityAutoTranslator.Core.Caching;

public sealed class DiskTranslationCache : ITranslationCache, IDisposable
{
    private readonly string _filePath;
    private readonly ConcurrentDictionary<TranslationCacheKey, string> _items = new();
    private bool _disposed;

    public DiskTranslationCache(string filePath)
    {
        _filePath = filePath;
        Load();
    }

    public int Count => _items.Count;

    public bool TryGet(TranslationCacheKey key, out string translatedText)
    {
        return _items.TryGetValue(key, out translatedText!);
    }

    public void Set(TranslationCacheKey key, string translatedText)
    {
        _items[key] = translatedText;
        Persist();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        Persist();
        _disposed = true;
    }

    private void Load()
    {
        if (!File.Exists(_filePath))
        {
            return;
        }

        foreach (var line in File.ReadAllLines(_filePath, Encoding.UTF8))
        {
            var separator = line.IndexOf('\t');
            if (separator <= 0)
            {
                continue;
            }

            var key = new TranslationCacheKey(line[..separator]);
            var encoded = line[(separator + 1)..];
            try
            {
                var translated = Encoding.UTF8.GetString(Convert.FromBase64String(encoded));
                _items[key] = translated;
            }
            catch (FormatException)
            {
                continue;
            }
        }
    }

    private void Persist()
    {
        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var lines = _items
            .OrderBy(item => item.Key.Value, StringComparer.Ordinal)
            .Select(item => item.Key.Value + "\t" + Convert.ToBase64String(Encoding.UTF8.GetBytes(item.Value)));

        File.WriteAllLines(_filePath, lines, Encoding.UTF8);
    }
}

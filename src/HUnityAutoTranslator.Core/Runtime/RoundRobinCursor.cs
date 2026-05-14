namespace HUnityAutoTranslator.Core.Runtime;

public sealed class RoundRobinCursor
{
    private int _nextIndex;

    public IReadOnlyList<T> TakeWindow<T>(IReadOnlyList<T> source, int maxCount)
    {
        if (source.Count == 0 || maxCount <= 0)
        {
            _nextIndex = 0;
            return Array.Empty<T>();
        }

        var count = Math.Min(source.Count, maxCount);
        if (count == source.Count)
        {
            _nextIndex = 0;
            return source;
        }

        var start = _nextIndex % source.Count;
        var window = new T[count];
        for (var i = 0; i < count; i++)
        {
            window[i] = source[(start + i) % source.Count];
        }

        _nextIndex = (start + count) % source.Count;
        return window;
    }

    public IReadOnlyList<T> TakeFullRound<T>(IReadOnlyList<T> source)
    {
        if (source.Count == 0)
        {
            _nextIndex = 0;
            return Array.Empty<T>();
        }

        var start = _nextIndex % source.Count;
        var round = new T[source.Count];
        for (var i = 0; i < source.Count; i++)
        {
            round[i] = source[(start + i) % source.Count];
        }

        _nextIndex = (start + 1) % source.Count;
        return round;
    }
}

namespace HUnityAutoTranslator.Core.Queueing;

public sealed class ProviderRateLimiter
{
    private readonly int _requestsPerMinute;
    private readonly Queue<DateTimeOffset> _requestTimes = new();
    private readonly object _gate = new();

    public ProviderRateLimiter(int requestsPerMinute)
    {
        _requestsPerMinute = Math.Max(1, requestsPerMinute);
    }

    public async Task WaitAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            TimeSpan delay;
            lock (_gate)
            {
                var now = DateTimeOffset.UtcNow;
                while (_requestTimes.Count > 0 && now - _requestTimes.Peek() >= TimeSpan.FromMinutes(1))
                {
                    _requestTimes.Dequeue();
                }

                if (_requestTimes.Count < _requestsPerMinute)
                {
                    _requestTimes.Enqueue(now);
                    return;
                }

                delay = TimeSpan.FromMinutes(1) - (now - _requestTimes.Peek());
            }

            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }
        }
    }
}

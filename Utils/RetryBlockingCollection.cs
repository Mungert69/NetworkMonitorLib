
using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;


namespace NetworkMonitor.Utils
{
 

public class RetryBlockingCollection<T> : BlockingCollection<T>
{
    private const int _defaultMaxRetries = 3;
    private const int _defaultInitialDelayMilliseconds = 10;
    private int _maxRetries;
    private TimeSpan _initialDelay;

    public RetryBlockingCollection(int maxRetries = _defaultMaxRetries, int initialDelayMilliseconds =_defaultInitialDelayMilliseconds)
    {
        _maxRetries = maxRetries;
        _initialDelay = TimeSpan.FromMilliseconds(initialDelayMilliseconds);
    }

    public RetryBlockingCollection(IProducerConsumerCollection<T> collection, int maxRetries = _defaultMaxRetries, int initialDelayMilliseconds = _defaultInitialDelayMilliseconds)
        : base(collection)
    {
        _maxRetries = maxRetries;
        _initialDelay = TimeSpan.FromMilliseconds(initialDelayMilliseconds);
    }

    public RetryBlockingCollection(int boundedCapacity, int maxRetries = _defaultMaxRetries, int initialDelayMilliseconds = _defaultInitialDelayMilliseconds)
        : base(boundedCapacity)
    {
        _maxRetries = maxRetries;
        _initialDelay = TimeSpan.FromMilliseconds(initialDelayMilliseconds);
    }

    public RetryBlockingCollection(IProducerConsumerCollection<T> collection, int boundedCapacity, int maxRetries = _defaultMaxRetries, int initialDelayMilliseconds = _defaultInitialDelayMilliseconds)
        : base(collection, boundedCapacity)
    {
        _maxRetries = maxRetries;
        _initialDelay = TimeSpan.FromMilliseconds(initialDelayMilliseconds);
    }

    public async Task<(bool, T?)> TryTakeWithRetryAsync(T? item)
    {
        int retries = 0;

        while (retries < _maxRetries)
        {
            if (this.IsCompleted)
            {
                return (false, default(T));
            }

            if (this.TryTake(out item, _initialDelay))
            {
                return (true, item);
            }

            retries++;
            await Task.Delay(_initialDelay);
            _initialDelay = TimeSpan.FromTicks(_initialDelay.Ticks * 2);
        }

        return (false, default(T));
    }
}


}
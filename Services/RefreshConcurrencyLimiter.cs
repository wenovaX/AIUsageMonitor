namespace AIUsageMonitor.Services;

public sealed class RefreshConcurrencyLimiter
{
    private readonly object _sync = new();
    private readonly Queue<TaskCompletionSource<IDisposable>> _waiters = new();
    private int _maxConcurrency;
    private int _activeCount;

    public RefreshConcurrencyLimiter(int maxConcurrency)
    {
        SetMaxConcurrency(maxConcurrency);
    }

    public void SetMaxConcurrency(int maxConcurrency)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxConcurrency, 1);

        lock (_sync)
        {
            _maxConcurrency = maxConcurrency;
            DispatchWaiters();
        }
    }

    public ValueTask<IDisposable> AcquireAsync()
    {
        lock (_sync)
        {
            if (_activeCount < _maxConcurrency && _waiters.Count == 0)
            {
                _activeCount++;
                return ValueTask.FromResult<IDisposable>(new Lease(this));
            }

            var waiter = new TaskCompletionSource<IDisposable>(TaskCreationOptions.RunContinuationsAsynchronously);
            _waiters.Enqueue(waiter);
            return new ValueTask<IDisposable>(waiter.Task);
        }
    }

    private void Release()
    {
        lock (_sync)
        {
            _activeCount--;
            DispatchWaiters();
        }
    }

    private void DispatchWaiters()
    {
        while (_activeCount < _maxConcurrency && _waiters.TryDequeue(out var waiter))
        {
            _activeCount++;
            waiter.TrySetResult(new Lease(this));
        }
    }

    private sealed class Lease(RefreshConcurrencyLimiter owner) : IDisposable
    {
        private RefreshConcurrencyLimiter? _owner = owner;

        public void Dispose() => Interlocked.Exchange(ref _owner, null)?.Release();
    }
}

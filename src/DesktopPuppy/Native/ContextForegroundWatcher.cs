namespace DesktopPuppy.Native;

// Own one hook for the app lifetime; usage collection may attach or detach independently.
public sealed class ContextForegroundWatcher : IForegroundWindowWatcher
{
    private readonly IForegroundWindowWatcher _inner;
    private readonly string _ownProcessName;
    private readonly object _gate = new();
    private ForegroundWindowSnapshot? _lastExternal;
    private Action<ForegroundWindowSnapshot>? _collector;
    private bool _disposed;

    public ContextForegroundWatcher(IForegroundWindowWatcher inner, string ownProcessName)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentException.ThrowIfNullOrWhiteSpace(ownProcessName);
        _inner = inner;
        _ownProcessName = ownProcessName;
        _inner.Start(Observe);
        _inner.RefreshForegroundWindow();
    }

    public ForegroundWindowSnapshot? LastExternal => Volatile.Read(ref _lastExternal);

    public void Start(Action<ForegroundWindowSnapshot> onForegroundChanged)
    {
        ArgumentNullException.ThrowIfNull(onForegroundChanged);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            Volatile.Write(ref _collector, onForegroundChanged);
        }
    }

    private void Observe(ForegroundWindowSnapshot snapshot)
    {
        Action<ForegroundWindowSnapshot>? collector;
        lock (_gate)
        {
            if (_disposed) return;
            if (!string.Equals(snapshot.ProcessName, _ownProcessName, StringComparison.OrdinalIgnoreCase))
                Volatile.Write(ref _lastExternal, snapshot);
            collector = _collector;
        }
        // Never take the collector's lock while holding ours. A racing callback already
        // dispatched before Stop is also rejected by UsageCollector's stopped-state guard.
        if (collector is not null && ReferenceEquals(collector, Volatile.Read(ref _collector))) collector(snapshot);
    }

    public void RefreshForegroundWindow()
    {
        lock (_gate) { if (_disposed) return; }
        _inner.RefreshForegroundWindow();
    }

    public void Stop()
    {
        lock (_gate) Volatile.Write(ref _collector, null);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            Volatile.Write(ref _collector, null);
        }
        try { _inner.Stop(); }
        finally { _inner.Dispose(); }
    }
}

using DesktopPuppy.Core.Clock;
using DesktopPuppy.Native;

namespace DesktopPuppy.Usage;

public interface IUsageEventSink
{
    void AppendSession(ForegroundSession session);

    void UpsertWindowTitleInterval(WindowTitleInterval interval);
}

public sealed class UsageCollector : IDisposable
{
    private readonly object _gate = new();
    private readonly string _ownProcessName;
    private readonly IClock _clock;
    private readonly IForegroundWindowWatcher _foregroundWatcher;
    private readonly ForegroundSessionTracker _sessionTracker;
    private readonly WindowTitleDebouncer _titleDebouncer;
    private bool _started;
    private bool _disposed;
    private bool _foregroundCollectionPaused;

    public UsageCollector(
        string ownProcessName,
        IClock clock,
        IForegroundWindowWatcher foregroundWatcher,
        IUsageEventSink sink,
        TimeSpan? titleStabilityWindow = null)
    {
        if (string.IsNullOrWhiteSpace(ownProcessName))
        {
            throw new ArgumentException("The own process name is required.", nameof(ownProcessName));
        }

        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(foregroundWatcher);
        ArgumentNullException.ThrowIfNull(sink);

        _ownProcessName = ownProcessName;
        _clock = clock;
        _foregroundWatcher = foregroundWatcher;
        _sessionTracker = new ForegroundSessionTracker(ownProcessName, clock, sink.AppendSession);
        _titleDebouncer = new WindowTitleDebouncer(
            titleStabilityWindow ?? TimeSpan.FromSeconds(2),
            sink.UpsertWindowTitleInterval);
    }

    public bool IsStarted
    {
        get
        {
            lock (_gate)
            {
                return _started;
            }
        }
    }

    public void Start()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_started)
            {
                return;
            }

            _foregroundCollectionPaused = false;
            _foregroundWatcher.Start(OnForegroundChanged);
            _started = true;
        }
    }

    public void Advance(DateTimeOffset at)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!_started)
            {
                return;
            }

            _titleDebouncer.Advance(at);
        }
    }

    public void OnSessionBoundary(SessionBoundary boundary, DateTimeOffset? at = null)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!_started)
            {
                return;
            }

            var occurredAt = at ?? _clock.UtcNow;
            _sessionTracker.OnSessionBoundary(boundary, occurredAt);
            _foregroundCollectionPaused = true;
            _titleDebouncer.OnForegroundWindowLeft(occurredAt);
        }
    }

    public void RefreshForegroundWindow()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!_started)
            {
                return;
            }

            _foregroundCollectionPaused = false;
            _foregroundWatcher.RefreshForegroundWindow();
        }
    }

    public void Stop()
    {
        lock (_gate)
        {
            if (!_started)
            {
                return;
            }

            var at = _clock.UtcNow;
            _foregroundCollectionPaused = true;
            _foregroundWatcher.Stop();
            _sessionTracker.OnSessionBoundary(SessionBoundary.Shutdown, at);
            _titleDebouncer.OnForegroundWindowLeft(at);
            _started = false;
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            Stop();
            _disposed = true;
        }

        _foregroundWatcher.Dispose();
    }

    private void OnForegroundChanged(ForegroundWindowSnapshot snapshot)
    {
        lock (_gate)
        {
            if (!_started || _disposed || _foregroundCollectionPaused)
            {
                return;
            }

            var at = _clock.UtcNow;
            _sessionTracker.OnForegroundApplicationChanged(snapshot, at);

            if (string.Equals(snapshot.ProcessName, _ownProcessName, StringComparison.OrdinalIgnoreCase))
            {
                _titleDebouncer.OnForegroundWindowLeft(at);
                return;
            }

            _titleDebouncer.OnTitleChanged(snapshot, at);
        }
    }
}

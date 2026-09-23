using DesktopPuppy.Core.Clock;
using DesktopPuppy.Native;

namespace DesktopPuppy.Usage;

public enum SessionBoundary
{
    Lock,
    Sleep,
    Hibernate,
    Shutdown
}

public sealed record ForegroundSession(
    Guid Id,
    ForegroundWindowSnapshot Snapshot,
    DateTimeOffset StartedAt,
    DateTimeOffset? EndedAt)
{
    public uint ProcessId => Snapshot.ProcessId;

    public string ProcessName => Snapshot.ProcessName;

    public string WindowTitle => Snapshot.WindowTitle;
}

public sealed class ForegroundSessionTracker
{
    private readonly string _ownProcessName;
    private readonly IClock _clock;
    private readonly Action<ForegroundSession> _onClosed;
    private ForegroundSession? _openSession;

    public ForegroundSessionTracker(
        string ownProcessName,
        IClock clock,
        Action<ForegroundSession> onClosed)
    {
        if (string.IsNullOrWhiteSpace(ownProcessName))
        {
            throw new ArgumentException("The own process name is required.", nameof(ownProcessName));
        }

        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(onClosed);
        _ownProcessName = ownProcessName;
        _clock = clock;
        _onClosed = onClosed;
    }

    public bool HasOpenSession => _openSession is not null;

    public ForegroundSession? OpenSession => _openSession;

    public void OnForegroundApplicationChanged(ForegroundWindowSnapshot snapshot, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        if (string.Equals(snapshot.ProcessName, _ownProcessName, StringComparison.OrdinalIgnoreCase))
        {
            CloseOpenSession(at);
            return;
        }

        if (_openSession is not null && _openSession.ProcessId == snapshot.ProcessId)
        {
            return;
        }

        CloseOpenSession(at);
        _openSession = new ForegroundSession(Guid.NewGuid(), snapshot, at, EndedAt: null);
    }

    public void OnSessionBoundary(SessionBoundary boundary, DateTimeOffset at)
    {
        CloseOpenSession(at);
    }

    public void OnSessionBoundary(SessionBoundary boundary)
    {
        OnSessionBoundary(boundary, _clock.UtcNow);
    }

    private void CloseOpenSession(DateTimeOffset at)
    {
        if (_openSession is null)
        {
            return;
        }

        var closed = _openSession with { EndedAt = at };
        _openSession = null;
        _onClosed(closed);
    }
}

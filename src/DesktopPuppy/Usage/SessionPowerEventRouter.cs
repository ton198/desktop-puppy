namespace DesktopPuppy.Usage;

public enum SessionPowerEvent
{
    Locked,
    Unlocked,
    Suspend,
    Hibernate,
    Resume,
    Shutdown
}

public sealed class SessionPowerEventRouter
{
    private readonly object _gate = new();
    private readonly Action<SessionBoundary, DateTimeOffset> _onBoundary;
    private readonly Action _onForegroundRefresh;
    private bool _isLocked;

    public SessionPowerEventRouter(
        Action<SessionBoundary, DateTimeOffset> onBoundary,
        Action onForegroundRefresh)
    {
        ArgumentNullException.ThrowIfNull(onBoundary);
        ArgumentNullException.ThrowIfNull(onForegroundRefresh);
        _onBoundary = onBoundary;
        _onForegroundRefresh = onForegroundRefresh;
    }

    public void OnEvent(SessionPowerEvent sessionEvent, DateTimeOffset at)
    {
        SessionBoundary? boundary = null;
        var refreshForeground = false;

        lock (_gate)
        {
            switch (sessionEvent)
            {
                case SessionPowerEvent.Locked:
                    _isLocked = true;
                    boundary = SessionBoundary.Lock;
                    break;
                case SessionPowerEvent.Unlocked:
                    _isLocked = false;
                    refreshForeground = true;
                    break;
                case SessionPowerEvent.Suspend:
                    boundary = SessionBoundary.Suspend;
                    break;
                case SessionPowerEvent.Hibernate:
                    boundary = SessionBoundary.Hibernate;
                    break;
                case SessionPowerEvent.Shutdown:
                    boundary = SessionBoundary.Shutdown;
                    break;
                case SessionPowerEvent.Resume:
                    refreshForeground = !_isLocked;
                    break;
            }
        }

        if (boundary is not null)
        {
            _onBoundary(boundary.Value, at);
        }

        if (refreshForeground)
        {
            _onForegroundRefresh();
        }
    }
}

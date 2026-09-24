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
    private readonly Action<SessionBoundary, DateTimeOffset> _onBoundary;

    public SessionPowerEventRouter(Action<SessionBoundary, DateTimeOffset> onBoundary)
    {
        ArgumentNullException.ThrowIfNull(onBoundary);
        _onBoundary = onBoundary;
    }

    public void OnEvent(SessionPowerEvent sessionEvent, DateTimeOffset at)
    {
        var boundary = sessionEvent switch
        {
            SessionPowerEvent.Locked => SessionBoundary.Lock,
            SessionPowerEvent.Suspend => SessionBoundary.Sleep,
            SessionPowerEvent.Hibernate => SessionBoundary.Hibernate,
            SessionPowerEvent.Shutdown => SessionBoundary.Shutdown,
            _ => (SessionBoundary?)null
        };

        if (boundary is not null)
        {
            _onBoundary(boundary.Value, at);
        }
    }
}

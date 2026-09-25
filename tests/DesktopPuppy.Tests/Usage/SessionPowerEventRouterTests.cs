using DesktopPuppy.Core.Clock;
using DesktopPuppy.Native;
using DesktopPuppy.Usage;
using Xunit;

namespace DesktopPuppy.Tests.Usage;

public sealed class SessionPowerEventRouterTests
{
    private static readonly DateTimeOffset Start = new(2026, 9, 24, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Locked_event_routes_lock_boundary_at_event_time()
    {
        var routed = new List<(SessionBoundary Boundary, DateTimeOffset At)>();
        var router = new SessionPowerEventRouter((boundary, at) => routed.Add((boundary, at)), () => { });
        var at = Start.AddMinutes(3);

        router.OnEvent(SessionPowerEvent.Locked, at);

        Assert.Equal((SessionBoundary.Lock, at), Assert.Single(routed));
    }

    [Fact]
    public void Suspend_event_routes_generic_suspend_boundary()
    {
        var routed = new List<(SessionBoundary Boundary, DateTimeOffset At)>();
        var router = new SessionPowerEventRouter((boundary, at) => routed.Add((boundary, at)), () => { });
        var at = Start.AddMinutes(3);

        router.OnEvent(SessionPowerEvent.Suspend, at);

        Assert.Equal((SessionBoundary.Suspend, at), Assert.Single(routed));
    }

    [Fact]
    public void Hibernate_event_routes_hibernate_boundary()
    {
        var routed = new List<(SessionBoundary Boundary, DateTimeOffset At)>();
        var router = new SessionPowerEventRouter((boundary, at) => routed.Add((boundary, at)), () => { });
        var at = Start.AddMinutes(3);

        router.OnEvent(SessionPowerEvent.Hibernate, at);

        Assert.Equal((SessionBoundary.Hibernate, at), Assert.Single(routed));
    }

    [Fact]
    public void Shutdown_event_routes_shutdown_boundary()
    {
        var routed = new List<(SessionBoundary Boundary, DateTimeOffset At)>();
        var router = new SessionPowerEventRouter((boundary, at) => routed.Add((boundary, at)), () => { });
        var at = Start.AddMinutes(3);

        router.OnEvent(SessionPowerEvent.Shutdown, at);

        Assert.Equal((SessionBoundary.Shutdown, at), Assert.Single(routed));
    }

    [Fact]
    public void Unlock_requests_a_foreground_refresh_without_creating_a_boundary()
    {
        var routed = new List<(SessionBoundary Boundary, DateTimeOffset At)>();
        var refreshCount = 0;
        var router = new SessionPowerEventRouter(
            (boundary, at) => routed.Add((boundary, at)),
            () => refreshCount++);

        router.OnEvent(SessionPowerEvent.Unlocked, Start.AddMinutes(4));

        Assert.Empty(routed);
        Assert.Equal(1, refreshCount);
    }

    [Fact]
    public void Resume_requests_a_foreground_refresh_when_the_session_is_unlocked()
    {
        var routed = new List<(SessionBoundary Boundary, DateTimeOffset At)>();
        var refreshCount = 0;
        var router = new SessionPowerEventRouter(
            (boundary, at) => routed.Add((boundary, at)),
            () => refreshCount++);

        router.OnEvent(SessionPowerEvent.Suspend, Start.AddMinutes(3));
        router.OnEvent(SessionPowerEvent.Resume, Start.AddMinutes(4));

        Assert.Equal((SessionBoundary.Suspend, Start.AddMinutes(3)), Assert.Single(routed));
        Assert.Equal(1, refreshCount);
    }

    [Fact]
    public void Resume_while_locked_waits_until_unlock_before_refreshing_foreground()
    {
        var routed = new List<(SessionBoundary Boundary, DateTimeOffset At)>();
        var refreshCount = 0;
        var router = new SessionPowerEventRouter(
            (boundary, at) => routed.Add((boundary, at)),
            () => refreshCount++);

        router.OnEvent(SessionPowerEvent.Locked, Start.AddMinutes(2));
        router.OnEvent(SessionPowerEvent.Suspend, Start.AddMinutes(3));
        router.OnEvent(SessionPowerEvent.Resume, Start.AddMinutes(4));

        Assert.Equal(0, refreshCount);
        Assert.Equal(2, routed.Count);

        router.OnEvent(SessionPowerEvent.Unlocked, Start.AddMinutes(5));

        Assert.Equal(1, refreshCount);
        Assert.Equal(2, routed.Count);
    }

    [Fact]
    public void Unknown_session_event_is_ignored()
    {
        var routed = new List<(SessionBoundary Boundary, DateTimeOffset At)>();
        var refreshCount = 0;
        var router = new SessionPowerEventRouter(
            (boundary, at) => routed.Add((boundary, at)),
            () => refreshCount++);

        router.OnEvent((SessionPowerEvent)999, Start);

        Assert.Empty(routed);
        Assert.Equal(0, refreshCount);
    }

    [Fact]
    public void Repeated_boundary_without_open_session_does_not_emit_duplicate_close()
    {
        var closed = new List<ForegroundSession>();
        var tracker = CreateTracker(closed);
        var router = CreateRouter(tracker, () => { });
        tracker.OnForegroundApplicationChanged(Snapshot(10, "Code"), Start);

        router.OnEvent(SessionPowerEvent.Locked, Start.AddMinutes(3));
        router.OnEvent(SessionPowerEvent.Locked, Start.AddMinutes(4));

        var session = Assert.Single(closed);
        Assert.Equal(Start.AddMinutes(3), session.EndedAt);
        Assert.False(tracker.HasOpenSession);
    }

    private static SessionPowerEventRouter CreateRouter(ForegroundSessionTracker tracker, Action refresh)
    {
        return new SessionPowerEventRouter((boundary, at) => tracker.OnSessionBoundary(boundary, at), refresh);
    }

    private static ForegroundSessionTracker CreateTracker(List<ForegroundSession> closed)
    {
        return new ForegroundSessionTracker("DesktopPuppy", new FixedClock(Start), closed.Add);
    }

    private static ForegroundWindowSnapshot Snapshot(uint processId, string processName)
    {
        return new ForegroundWindowSnapshot((nint)processId, processId, processName, "Window");
    }

    private sealed class FixedClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }
}

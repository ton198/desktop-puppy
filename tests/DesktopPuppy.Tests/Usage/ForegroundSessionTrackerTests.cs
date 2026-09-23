using DesktopPuppy.Core.Clock;
using DesktopPuppy.Native;
using DesktopPuppy.Usage;
using Xunit;

namespace DesktopPuppy.Tests.Usage;

public sealed class ForegroundSessionTrackerTests
{
    private static readonly DateTimeOffset Start = new(2026, 9, 23, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void First_foreground_event_opens_a_session_without_emitting_one()
    {
        var closed = new List<ForegroundSession>();
        var tracker = CreateTracker(closed);

        tracker.OnForegroundApplicationChanged(Snapshot(10, "Code", "main.cs"), Start);

        Assert.True(tracker.HasOpenSession);
        Assert.Empty(closed);
    }

    [Fact]
    public void Changing_application_closes_the_previous_session_and_opens_the_next_one()
    {
        var closed = new List<ForegroundSession>();
        var tracker = CreateTracker(closed);

        tracker.OnForegroundApplicationChanged(Snapshot(10, "Code", "main.cs"), Start);
        tracker.OnForegroundApplicationChanged(Snapshot(20, "Browser", "Docs"), Start.AddMinutes(5));

        var session = Assert.Single(closed);
        Assert.Equal("Code", session.ProcessName);
        Assert.Equal(Start, session.StartedAt);
        Assert.Equal(Start.AddMinutes(5), session.EndedAt);
        Assert.True(tracker.HasOpenSession);
    }

    [Fact]
    public void Repeating_the_same_foreground_application_does_not_create_a_new_session()
    {
        var closed = new List<ForegroundSession>();
        var tracker = CreateTracker(closed);
        var snapshot = Snapshot(10, "Code", "main.cs");

        tracker.OnForegroundApplicationChanged(snapshot, Start);
        tracker.OnForegroundApplicationChanged(snapshot with { WindowTitle = "other.cs" }, Start.AddMinutes(1));

        Assert.Empty(closed);
        Assert.True(tracker.HasOpenSession);
    }

    [Fact]
    public void Desktop_Puppy_is_excluded_and_closes_the_previous_session()
    {
        var closed = new List<ForegroundSession>();
        var tracker = CreateTracker(closed);

        tracker.OnForegroundApplicationChanged(Snapshot(10, "Code", "main.cs"), Start);
        tracker.OnForegroundApplicationChanged(Snapshot(99, "DesktopPuppy", "Desktop Puppy"), Start.AddMinutes(2));

        var session = Assert.Single(closed);
        Assert.Equal("Code", session.ProcessName);
        Assert.False(tracker.HasOpenSession);
    }

    [Theory]
    [InlineData(SessionBoundary.Lock)]
    [InlineData(SessionBoundary.Sleep)]
    [InlineData(SessionBoundary.Hibernate)]
    [InlineData(SessionBoundary.Shutdown)]
    public void Session_boundaries_close_the_open_session(SessionBoundary boundary)
    {
        var closed = new List<ForegroundSession>();
        var tracker = CreateTracker(closed);

        tracker.OnForegroundApplicationChanged(Snapshot(10, "Code", "main.cs"), Start);
        tracker.OnSessionBoundary(boundary, Start.AddMinutes(3));

        var session = Assert.Single(closed);
        Assert.Equal(Start.AddMinutes(3), session.EndedAt);
        Assert.False(tracker.HasOpenSession);
    }

    [Fact]
    public void A_foreground_event_after_unlock_starts_a_new_session()
    {
        var closed = new List<ForegroundSession>();
        var tracker = CreateTracker(closed);

        tracker.OnForegroundApplicationChanged(Snapshot(10, "Code", "main.cs"), Start);
        tracker.OnSessionBoundary(SessionBoundary.Lock, Start.AddMinutes(3));
        tracker.OnForegroundApplicationChanged(Snapshot(20, "Browser", "Docs"), Start.AddMinutes(4));

        Assert.Single(closed);
        Assert.True(tracker.HasOpenSession);
        Assert.Equal("Browser", tracker.OpenSession!.ProcessName);
    }

    private static ForegroundSessionTracker CreateTracker(List<ForegroundSession> closed)
    {
        return new ForegroundSessionTracker(
            ownProcessName: "DesktopPuppy",
            new FixedClock(Start),
            closed.Add);
    }

    private static ForegroundWindowSnapshot Snapshot(uint processId, string processName, string title)
    {
        return new ForegroundWindowSnapshot((nint)processId, processId, processName, title);
    }

    private sealed class FixedClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }
}

using DesktopPuppy.Core.Clock;
using DesktopPuppy.Native;
using DesktopPuppy.Usage;
using Xunit;

namespace DesktopPuppy.Tests.Usage;

public sealed class UsageCollectorTests
{
    private static readonly DateTimeOffset Start = new(2026, 9, 23, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Foreground_changes_flow_into_sessions_and_stable_title_intervals()
    {
        var watcher = new FakeForegroundWindowWatcher();
        var sink = new RecordingUsageSink();
        var clock = new FixedClock(Start);
        using var collector = new UsageCollector("DesktopPuppy", clock, watcher, sink);

        collector.Start();
        watcher.Emit(Snapshot(10, "Code", "main.cs"));
        collector.Advance(Start.AddSeconds(2));
        clock.Set(Start.AddMinutes(5));
        watcher.Emit(Snapshot(20, "Browser", "Docs"));
        collector.Stop();

        var session = Assert.Single(sink.Sessions, session => session.ProcessName == "Code");
        Assert.Equal(Start.AddMinutes(5), session.EndedAt);
        Assert.Contains(sink.Sessions, session => session.ProcessName == "Browser" && session.EndedAt == Start.AddMinutes(5));
        var titleInterval = Assert.Single(sink.TitleIntervals, interval => interval.WindowTitle == "main.cs");
        Assert.Equal(Start, titleInterval.StartedAt);
        Assert.Equal(Start.AddMinutes(5), titleInterval.EndedAt);
    }

    [Theory]
    [InlineData(SessionBoundary.Lock)]
    [InlineData(SessionBoundary.Sleep)]
    [InlineData(SessionBoundary.Hibernate)]
    public void Power_boundaries_close_the_session_and_title_interval(SessionBoundary boundary)
    {
        var watcher = new FakeForegroundWindowWatcher();
        var sink = new RecordingUsageSink();
        var clock = new FixedClock(Start);
        using var collector = new UsageCollector("DesktopPuppy", clock, watcher, sink);

        collector.Start();
        watcher.Emit(Snapshot(10, "Code", "main.cs"));
        collector.Advance(Start.AddSeconds(2));
        var boundaryAt = Start.AddMinutes(3);
        clock.Set(boundaryAt);
        collector.OnSessionBoundary(boundary);

        var session = Assert.Single(sink.Sessions);
        Assert.Equal(boundaryAt, session.EndedAt);
        var interval = Assert.Single(sink.TitleIntervals);
        Assert.Equal(boundaryAt, interval.EndedAt);
    }

    [Fact]
    public void Resume_refreshes_the_current_window_and_starts_a_new_usage_session()
    {
        var watcher = new FakeForegroundWindowWatcher();
        var sink = new RecordingUsageSink();
        var clock = new FixedClock(Start);
        using var collector = new UsageCollector("DesktopPuppy", clock, watcher, sink);
        var router = new SessionPowerEventRouter(
            (boundary, at) => collector.OnSessionBoundary(boundary, at),
            collector.RefreshForegroundWindow);

        collector.Start();
        watcher.Emit(Snapshot(10, "Code", "main.cs"));
        clock.Set(Start.AddMinutes(3));
        router.OnEvent(SessionPowerEvent.Suspend, clock.UtcNow);
        clock.Set(Start.AddMinutes(5));
        router.OnEvent(SessionPowerEvent.Resume, clock.UtcNow);
        clock.Set(Start.AddMinutes(6));
        collector.Stop();

        Assert.Collection(
            sink.Sessions,
            first =>
            {
                Assert.Equal("Code", first.ProcessName);
                Assert.Equal(Start, first.StartedAt);
                Assert.Equal(Start.AddMinutes(3), first.EndedAt);
            },
            second =>
            {
                Assert.Equal("Code", second.ProcessName);
                Assert.Equal(Start.AddMinutes(5), second.StartedAt);
                Assert.Equal(Start.AddMinutes(6), second.EndedAt);
            });
        Assert.Equal(1, watcher.RefreshCount);
    }

    [Fact]
    public void Resume_while_locked_does_not_count_the_lock_screen_and_unlock_refreshes_usage()
    {
        var watcher = new FakeForegroundWindowWatcher();
        var sink = new RecordingUsageSink();
        var clock = new FixedClock(Start);
        using var collector = new UsageCollector("DesktopPuppy", clock, watcher, sink);
        var router = new SessionPowerEventRouter(
            (boundary, at) => collector.OnSessionBoundary(boundary, at),
            collector.RefreshForegroundWindow);

        collector.Start();
        watcher.Emit(Snapshot(10, "Code", "main.cs"));
        clock.Set(Start.AddMinutes(2));
        router.OnEvent(SessionPowerEvent.Locked, clock.UtcNow);
        watcher.Emit(Snapshot(77, "LockApp", "Windows Lock Screen"));
        clock.Set(Start.AddMinutes(3));
        router.OnEvent(SessionPowerEvent.Suspend, clock.UtcNow);
        clock.Set(Start.AddMinutes(4));
        router.OnEvent(SessionPowerEvent.Resume, clock.UtcNow);
        Assert.Equal(0, watcher.RefreshCount);
        watcher.SetCurrentSnapshot(Snapshot(10, "Code", "main.cs"));
        clock.Set(Start.AddMinutes(5));
        router.OnEvent(SessionPowerEvent.Unlocked, clock.UtcNow);
        clock.Set(Start.AddMinutes(6));
        collector.Stop();

        Assert.Collection(
            sink.Sessions,
            first => Assert.Equal(Start.AddMinutes(2), first.EndedAt),
            second =>
            {
                Assert.Equal(Start.AddMinutes(5), second.StartedAt);
                Assert.Equal(Start.AddMinutes(6), second.EndedAt);
            });
        Assert.Equal(1, watcher.RefreshCount);
    }
    [Fact]
    public void Desktop_Puppy_foreground_events_close_tracking_without_recording_the_pet()
    {
        var watcher = new FakeForegroundWindowWatcher();
        var sink = new RecordingUsageSink();
        var clock = new FixedClock(Start);
        using var collector = new UsageCollector("DesktopPuppy", clock, watcher, sink);

        collector.Start();
        watcher.Emit(Snapshot(10, "Code", "main.cs"));
        collector.Advance(Start.AddSeconds(2));
        clock.Set(Start.AddMinutes(1));
        watcher.Emit(Snapshot(99, "DesktopPuppy", "Desktop Puppy"));
        collector.Stop();

        Assert.DoesNotContain(sink.Sessions, session => session.ProcessName == "DesktopPuppy");
        Assert.DoesNotContain(sink.TitleIntervals, interval => interval.ProcessName == "DesktopPuppy");
        Assert.Single(sink.Sessions);
    }

    private static ForegroundWindowSnapshot Snapshot(uint processId, string processName, string title)
    {
        return new ForegroundWindowSnapshot((nint)processId, processId, processName, title);
    }

    private sealed class FixedClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; private set; } = utcNow;

        public void Set(DateTimeOffset utcNow) => UtcNow = utcNow;
    }

    private sealed class FakeForegroundWindowWatcher : IForegroundWindowWatcher
    {
        private Action<ForegroundWindowSnapshot>? _callback;

        public void Start(Action<ForegroundWindowSnapshot> onForegroundChanged) => _callback = onForegroundChanged;

        private ForegroundWindowSnapshot? _currentSnapshot;

        public int RefreshCount { get; private set; }

        public void RefreshForegroundWindow()
        {
            if (_currentSnapshot is null)
            {
                return;
            }

            RefreshCount++;
            _callback?.Invoke(_currentSnapshot);
        }

        public void SetCurrentSnapshot(ForegroundWindowSnapshot snapshot) => _currentSnapshot = snapshot;

        public void Stop() => _callback = null;

        public void Dispose() => Stop();

        public void Emit(ForegroundWindowSnapshot snapshot)
        {
            _currentSnapshot = snapshot;
            _callback?.Invoke(snapshot);
        }
    }

    private sealed class RecordingUsageSink : IUsageEventSink
    {
        public List<ForegroundSession> Sessions { get; } = [];

        public List<WindowTitleInterval> TitleIntervals { get; } = [];

        public void AppendSession(ForegroundSession session) => Sessions.Add(session);

        public void UpsertWindowTitleInterval(WindowTitleInterval interval)
        {
            var existingIndex = TitleIntervals.FindIndex(existing => existing.Id == interval.Id);
            if (existingIndex >= 0)
            {
                TitleIntervals[existingIndex] = interval;
            }
            else
            {
                TitleIntervals.Add(interval);
            }
        }
    }
}

using DesktopPuppy.Core.Clock;
using DesktopPuppy.Native;
using DesktopPuppy.Usage;
using Xunit;

namespace DesktopPuppy.Tests.Usage;

public sealed class UsageCollectorTests
{
    private static readonly DateTimeOffset Start = new(2026, 9, 23, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Statistics_snapshot_captures_clock_and_open_session_together()
    {
        // Purpose: make the live statistics timestamp and active session one coherent observation.
        // Coverage/target: a fixed clock and foreground event return the same AsOfUtc and open process.
        var watcher = new FakeForegroundWindowWatcher();
        var sink = new RecordingUsageSink();
        var clock = new FixedClock(Start);
        using var collector = new UsageCollector("DesktopPuppy", clock, watcher, sink);

        collector.Start();
        watcher.Emit(Snapshot(10, "Code", "main.cs"));
        var asOfUtc = Start.AddMinutes(3);
        clock.Set(asOfUtc);

        var snapshot = collector.CaptureStatisticsSnapshot();

        Assert.Equal(asOfUtc, snapshot.AsOfUtc);
        Assert.NotNull(snapshot.ActiveSession);
        Assert.Equal("Code", snapshot.ActiveSession.ProcessName);
        Assert.Equal(Start, snapshot.ActiveSession.StartedAt);
        Assert.Null(snapshot.ActiveSession.EndedAt);
    }

    [Fact]
    public void Statistics_snapshot_has_no_active_session_before_foreground_or_after_boundary()
    {
        // Purpose: absent or boundary-closed foreground work must never appear as a live session.
        // Coverage/target: snapshots are null before foreground and after lock; the closed session persists once.
        var watcher = new FakeForegroundWindowWatcher();
        var sink = new RecordingUsageSink();
        var clock = new FixedClock(Start);
        using var collector = new UsageCollector("DesktopPuppy", clock, watcher, sink);

        collector.Start();

        Assert.Null(collector.CaptureStatisticsSnapshot().ActiveSession);

        watcher.Emit(Snapshot(10, "Code", "main.cs"));
        var boundaryAt = Start.AddMinutes(3);
        clock.Set(boundaryAt);
        collector.OnSessionBoundary(SessionBoundary.Lock);

        Assert.Null(collector.CaptureStatisticsSnapshot().ActiveSession);
        var closed = Assert.Single(sink.Sessions);
        Assert.Equal("Code", closed.ProcessName);
        Assert.Equal(boundaryAt, closed.EndedAt);
    }

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

    [Fact]
    public void Background_sampling_detects_title_changes_without_a_foreground_event()
    {
        // Break caught: advancing only the debounce clock leaves the old title open.
        var watcher = new FakeForegroundWindowWatcher();
        var sink = new RecordingUsageSink();
        var clock = new FixedClock(Start);
        using var collector = new UsageCollector("DesktopPuppy", clock, watcher, sink);
        collector.Start();
        watcher.Emit(Snapshot(10, "Browser", "First tab"));
        clock.Set(Start.AddSeconds(2));
        collector.Advance(clock.UtcNow);
        watcher.SetCurrentSnapshot(Snapshot(10, "Browser", "Second tab"));
        clock.Set(Start.AddSeconds(5));
        collector.Advance(clock.UtcNow);
        clock.Set(Start.AddSeconds(7));
        collector.Advance(clock.UtcNow);
        clock.Set(Start.AddSeconds(10));
        collector.Stop();

        Assert.Collection(sink.TitleIntervals,
            first =>
            {
                Assert.Equal("First tab", first.WindowTitle);
                Assert.Equal(Start.AddSeconds(5), first.EndedAt);
            },
            second =>
            {
                Assert.Equal("Second tab", second.WindowTitle);
                Assert.Equal(Start.AddSeconds(5), second.StartedAt);
                Assert.Equal(Start.AddSeconds(10), second.EndedAt);
            });
        Assert.Equal(TimeSpan.FromSeconds(10), Assert.Single(sink.Sessions).EndedAt - Start);
    }

    [Fact]
    public void Sampling_discards_a_title_that_changes_before_the_stability_deadline()
    {
        // Break caught: advancing before sampling records a stale pending title.
        var watcher = new FakeForegroundWindowWatcher();
        var sink = new RecordingUsageSink();
        var clock = new FixedClock(Start);
        using var collector = new UsageCollector("DesktopPuppy", clock, watcher, sink);
        collector.Start();
        watcher.Emit(Snapshot(10, "Browser", "Transient"));
        clock.Set(Start.AddSeconds(1));
        watcher.SetCurrentSnapshot(Snapshot(10, "Browser", "Stable"));
        collector.Advance(clock.UtcNow);
        clock.Set(Start.AddSeconds(2));
        collector.Advance(clock.UtcNow);
        Assert.Empty(sink.TitleIntervals);
        clock.Set(Start.AddSeconds(3));
        collector.Advance(clock.UtcNow);
        Assert.Equal("Stable", Assert.Single(sink.TitleIntervals).WindowTitle);
    }

    [Theory]
    [InlineData(SessionBoundary.Lock)]
    [InlineData(SessionBoundary.Suspend)]
    public void Background_sampling_does_not_resume_collection_after_a_boundary(SessionBoundary boundary)
    {
        var watcher = new FakeForegroundWindowWatcher();
        var sink = new RecordingUsageSink();
        var clock = new FixedClock(Start);
        using var collector = new UsageCollector("DesktopPuppy", clock, watcher, sink);
        collector.Start();
        watcher.Emit(Snapshot(10, "Code", "main.cs"));
        clock.Set(Start.AddSeconds(2));
        collector.Advance(clock.UtcNow);
        clock.Set(Start.AddSeconds(5));
        collector.OnSessionBoundary(boundary);
        watcher.SetCurrentSnapshot(Snapshot(77, "LockApp", "Lock screen"));
        clock.Set(Start.AddMinutes(5));
        collector.Advance(clock.UtcNow);

        Assert.Null(collector.CaptureStatisticsSnapshot().ActiveSession);
        Assert.Equal("Code", Assert.Single(sink.Sessions).ProcessName);
        Assert.Equal(Start.AddSeconds(5), Assert.Single(sink.TitleIntervals).EndedAt);
        Assert.DoesNotContain(sink.TitleIntervals, interval => interval.ProcessName == "LockApp");
    }

    [Fact]
    public void Start_samples_the_existing_foreground_window_without_waiting_for_a_switch()
    {
        var watcher = new FakeForegroundWindowWatcher();
        watcher.SetCurrentSnapshot(Snapshot(10, "Code", "main.cs"));
        var sink = new RecordingUsageSink();
        var clock = new FixedClock(Start);
        using var collector = new UsageCollector("DesktopPuppy", clock, watcher, sink);
        collector.Start();
        clock.Set(Start.AddSeconds(2));
        collector.Advance(clock.UtcNow);
        clock.Set(Start.AddMinutes(1));
        collector.Stop();

        var session = Assert.Single(sink.Sessions);
        Assert.Equal(Start, session.StartedAt);
        Assert.Equal(Start.AddMinutes(1), session.EndedAt);
        Assert.Equal("main.cs", Assert.Single(sink.TitleIntervals).WindowTitle);
    }

    [Fact]
    public void Failed_sampling_does_not_promote_an_unconfirmed_pending_title()
    {
        var watcher = new FakeForegroundWindowWatcher();
        var sink = new RecordingUsageSink();
        var clock = new FixedClock(Start);
        using var collector = new UsageCollector("DesktopPuppy", clock, watcher, sink);
        collector.Start();
        watcher.Emit(Snapshot(10, "Browser", "Old"));
        watcher.SetCurrentSnapshot(Snapshot(10, "Browser", "New"));
        watcher.RefreshEnabled = false;
        clock.Set(Start.AddSeconds(2));
        collector.Advance(clock.UtcNow);
        Assert.Empty(sink.TitleIntervals);
        watcher.RefreshEnabled = true;
        clock.Set(Start.AddSeconds(3));
        collector.Advance(clock.UtcNow);
        clock.Set(Start.AddSeconds(5));
        collector.Advance(clock.UtcNow);
        var title = Assert.Single(sink.TitleIntervals);
        Assert.Equal("New", title.WindowTitle);
        Assert.Equal(Start.AddSeconds(3), title.StartedAt);
    }

    [Fact]
    public void Checkpoint_failure_is_contained_and_the_next_sample_retries_the_same_interval()
    {
        var watcher = new FakeForegroundWindowWatcher();
        var sink = new RecordingUsageSink();
        var clock = new FixedClock(Start);
        using var collector = new UsageCollector("DesktopPuppy", clock, watcher, sink);
        collector.Start();
        watcher.Emit(Snapshot(10, "Code", "main.cs"));
        clock.Set(Start.AddSeconds(2));
        collector.Advance(clock.UtcNow);
        var id = Assert.Single(sink.TitleIntervals).Id;
        sink.FailNextUpsert = true;
        clock.Set(Start.AddSeconds(32));
        collector.Advance(clock.UtcNow); // Must not escape a recurring timer callback.
        Assert.Equal(Start.AddSeconds(2), Assert.Single(sink.TitleIntervals).LastConfirmedAt);
        clock.Set(Start.AddSeconds(32.25));
        collector.Advance(clock.UtcNow);
        var retried = Assert.Single(sink.TitleIntervals);
        Assert.Equal(id, retried.Id);
        Assert.Equal(Start.AddSeconds(32.25), retried.LastConfirmedAt);
        Assert.Null(retried.EndedAt);
    }

    [Fact]
    public void Failed_lock_close_retries_the_original_boundary_before_reopening_the_same_title()
    {
        var watcher = new FakeForegroundWindowWatcher();
        var sink = new RecordingUsageSink();
        var clock = new FixedClock(Start);
        using var collector = new UsageCollector("DesktopPuppy", clock, watcher, sink);
        collector.Start();
        watcher.Emit(Snapshot(10, "Code", "main.cs"));
        clock.Set(Start.AddSeconds(2));
        collector.Advance(clock.UtcNow);
        var originalId = Assert.Single(sink.TitleIntervals).Id;
        sink.FailNextUpsert = true;
        clock.Set(Start.AddSeconds(5));
        Assert.Throws<InvalidOperationException>(() => collector.OnSessionBoundary(SessionBoundary.Lock));
        clock.Set(Start.AddMinutes(1));
        collector.Advance(clock.UtcNow);
        Assert.Null(collector.CaptureStatisticsSnapshot().ActiveSession);
        clock.Set(Start.AddSeconds(65));
        collector.RefreshForegroundWindow();
        clock.Set(Start.AddSeconds(67));
        collector.Advance(clock.UtcNow);

        Assert.Collection(sink.TitleIntervals,
            closed =>
            {
                Assert.Equal(originalId, closed.Id);
                Assert.Equal(Start.AddSeconds(5), closed.EndedAt);
            },
            reopened =>
            {
                Assert.NotEqual(originalId, reopened.Id);
                Assert.Equal(Start.AddSeconds(65), reopened.StartedAt);
                Assert.Null(reopened.EndedAt);
            });
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

        public bool RefreshEnabled { get; set; } = true;

        public void RefreshForegroundWindow()
        {
            if (!RefreshEnabled || _currentSnapshot is null)
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

        public bool FailNextUpsert { get; set; }

        public void AppendSession(ForegroundSession session) => Sessions.Add(session);

        public void UpsertWindowTitleInterval(WindowTitleInterval interval)
        {
            if (FailNextUpsert)
            {
                FailNextUpsert = false;
                throw new InvalidOperationException("Simulated persistence failure.");
            }
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

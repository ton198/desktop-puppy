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

        public void Stop() => _callback = null;

        public void Dispose() => Stop();

        public void Emit(ForegroundWindowSnapshot snapshot) => _callback?.Invoke(snapshot);
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








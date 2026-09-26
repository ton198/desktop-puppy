using DesktopPuppy.Core.Clock;
using DesktopPuppy.Native;
using DesktopPuppy.Usage;
using Xunit;

namespace DesktopPuppy.Tests.Usage;

public sealed class UsageStatisticsQueryServiceTests
{
    private static readonly DateTimeOffset Start = At(2026, 9, 24, 10);
    private static readonly DateOnly SelectedDate = new(2026, 9, 24);

    [Fact]
    public async Task Query_counts_active_session_only_through_snapshot_as_of()
    {
        // Purpose: expose current application time without closing its foreground session.
        // Coverage/target: a live session contributes exactly StartedAt..AsOfUtc and no future duration.
        using var database = new TemporaryDatabase();
        using var store = new UsageSqliteStore(database.Path);
        var clock = new FixedClock(Start);
        var watcher = new FakeForegroundWindowWatcher();
        using var collector = new UsageCollector("DesktopPuppy", clock, watcher, store);
        collector.Start();
        watcher.Emit(Snapshot("Code", "main.cs"));
        clock.Set(Start.AddMinutes(30));
        var service = new UsageStatisticsQueryService(store, collector, TimeZoneInfo.Utc);

        var result = await service.QueryAsync(SelectedDate);

        Assert.Equal(SelectedDate, result.LocalDate);
        Assert.Equal(TimeSpan.FromMinutes(30), result.TotalDuration);
        var application = Assert.Single(result.Applications);
        Assert.Equal("Code", application.ProcessName);
        Assert.Equal(TimeSpan.FromMinutes(30), application.Duration);
        Assert.Empty(application.WindowTitles);
    }

    [Fact]
    public async Task Query_deduplicates_active_session_when_closed_row_appears_during_read()
    {
        // Purpose: prevent double counting while a live session transitions into persisted storage.
        // Coverage/target: matching IDs prefer one persisted row, capped to the snapshot timestamp.
        using var database = new TemporaryDatabase();
        using var store = new UsageSqliteStore(database.Path);
        var clock = new FixedClock(Start);
        var watcher = new FakeForegroundWindowWatcher();
        using var collector = new UsageCollector("DesktopPuppy", clock, watcher, new NullUsageSink());
        collector.Start();
        watcher.Emit(Snapshot("Code", "main.cs"));
        var asOfUtc = Start.AddMinutes(30);
        clock.Set(asOfUtc);
        var activeSession = Assert.IsType<ForegroundSession>(collector.CaptureStatisticsSnapshot().ActiveSession);
        store.AppendSession(activeSession with { EndedAt = asOfUtc.AddMinutes(10) });
        var service = new UsageStatisticsQueryService(store, collector, TimeZoneInfo.Utc);

        var result = await service.QueryAsync(SelectedDate);

        var application = Assert.Single(result.Applications);
        Assert.Equal("Code", application.ProcessName);
        Assert.Equal(TimeSpan.FromMinutes(30), application.Duration);
        Assert.Equal(TimeSpan.FromMinutes(30), result.TotalDuration);
    }

    [Fact]
    public async Task Query_clips_title_interval_closed_after_snapshot_as_of()
    {
        // Purpose: keep a title closed during the read from extending beyond the refresh instant.
        // Coverage/target: a persisted future end is normalized to AsOfUtc before aggregation.
        using var database = new TemporaryDatabase();
        using var store = new UsageSqliteStore(database.Path);
        var clock = new FixedClock(Start.AddMinutes(20));
        var watcher = new FakeForegroundWindowWatcher();
        using var collector = new UsageCollector("DesktopPuppy", clock, watcher, store);
        collector.Start();
        store.AppendSession(Session("Code", "main.cs", Start, Start.AddMinutes(20)));
        store.UpsertWindowTitleInterval(Title("Code", "main.cs", Start, Start.AddMinutes(35)));
        var service = new UsageStatisticsQueryService(store, collector, TimeZoneInfo.Utc);

        var result = await service.QueryAsync(SelectedDate);

        var application = Assert.Single(result.Applications);
        var title = Assert.Single(application.WindowTitles);
        Assert.Equal("main.cs", title.WindowTitle);
        Assert.Equal(TimeSpan.FromMinutes(20), title.Duration);
    }

    [Fact]
    public async Task Query_returns_selected_local_date_with_ranked_apps_and_title_details()
    {
        // Purpose: produce the complete daily process/title hierarchy for the requested local date.
        // Coverage/target: repeated rows are grouped, adjacent dates excluded, and durations ranked descending.
        using var database = new TemporaryDatabase();
        using var store = new UsageSqliteStore(database.Path);
        var clock = new FixedClock(At(2026, 9, 25, 1));
        var watcher = new FakeForegroundWindowWatcher();
        using var collector = new UsageCollector("DesktopPuppy", clock, watcher, store);
        collector.Start();

        store.AppendSession(Session("Browser", "Docs", At(2026, 9, 24, 9), At(2026, 9, 24, 10)));
        store.AppendSession(Session("Code", "main.cs", At(2026, 9, 24, 10), At(2026, 9, 24, 10, 20)));
        store.AppendSession(Session("Code", "notes.txt", At(2026, 9, 24, 11), At(2026, 9, 24, 11, 25)));
        store.AppendSession(Session("Browser", "Yesterday", At(2026, 9, 23, 20), At(2026, 9, 23, 20, 30)));

        store.UpsertWindowTitleInterval(Title("Browser", "Docs", At(2026, 9, 24, 9), At(2026, 9, 24, 9, 30)));
        store.UpsertWindowTitleInterval(Title("Browser", "Docs", At(2026, 9, 24, 9, 30), At(2026, 9, 24, 9, 40)));
        store.UpsertWindowTitleInterval(Title("Browser", "News", At(2026, 9, 24, 9, 40), At(2026, 9, 24, 10)));
        store.UpsertWindowTitleInterval(Title("Code", "main.cs", At(2026, 9, 24, 10), At(2026, 9, 24, 10, 20)));
        store.UpsertWindowTitleInterval(Title("Code", "main.cs", At(2026, 9, 24, 11), At(2026, 9, 24, 11, 10)));
        store.UpsertWindowTitleInterval(Title("Code", "notes.txt", At(2026, 9, 24, 11, 10), At(2026, 9, 24, 11, 25)));
        store.UpsertWindowTitleInterval(Title("Browser", "Yesterday", At(2026, 9, 23, 20), At(2026, 9, 23, 20, 30)));

        var service = new UsageStatisticsQueryService(store, collector, TimeZoneInfo.Utc);

        var result = await service.QueryAsync(SelectedDate);

        Assert.Equal(SelectedDate, result.LocalDate);
        Assert.Equal(TimeSpan.FromMinutes(105), result.TotalDuration);
        Assert.Collection(
            result.Applications,
            browser =>
            {
                Assert.Equal("Browser", browser.ProcessName);
                Assert.Equal(TimeSpan.FromMinutes(60), browser.Duration);
                Assert.Collection(
                    browser.WindowTitles,
                    docs =>
                    {
                        Assert.Equal("Docs", docs.WindowTitle);
                        Assert.Equal(TimeSpan.FromMinutes(40), docs.Duration);
                    },
                    news =>
                    {
                        Assert.Equal("News", news.WindowTitle);
                        Assert.Equal(TimeSpan.FromMinutes(20), news.Duration);
                    });
            },
            code =>
            {
                Assert.Equal("Code", code.ProcessName);
                Assert.Equal(TimeSpan.FromMinutes(45), code.Duration);
                Assert.Collection(
                    code.WindowTitles,
                    main =>
                    {
                        Assert.Equal("main.cs", main.WindowTitle);
                        Assert.Equal(TimeSpan.FromMinutes(30), main.Duration);
                    },
                    notes =>
                    {
                        Assert.Equal("notes.txt", notes.WindowTitle);
                        Assert.Equal(TimeSpan.FromMinutes(15), notes.Duration);
                    });
            });
    }

    [Fact]
    public async Task Query_returns_empty_daily_statistics_when_date_has_no_usage()
    {
        // Purpose: represent a valid no-activity date without reporting a storage failure.
        // Coverage/target: an empty store returns the requested date, zero total, and no application rows.
        using var database = new TemporaryDatabase();
        using var store = new UsageSqliteStore(database.Path);
        var clock = new FixedClock(Start);
        var watcher = new FakeForegroundWindowWatcher();
        using var collector = new UsageCollector("DesktopPuppy", clock, watcher, store);
        collector.Start();
        var service = new UsageStatisticsQueryService(store, collector, TimeZoneInfo.Utc);

        var result = await service.QueryAsync(SelectedDate);

        Assert.Equal(SelectedDate, result.LocalDate);
        Assert.Equal(TimeSpan.Zero, result.TotalDuration);
        Assert.Empty(result.Applications);
    }

    [Fact]
    public async Task Query_propagates_store_read_failure_for_the_view_to_report()
    {
        // Purpose: preserve storage failures so presentation can distinguish unavailable data from true zero usage.
        // Coverage/target: querying a disposed store faults with its ObjectDisposedException.
        using var database = new TemporaryDatabase();
        var store = new UsageSqliteStore(database.Path);
        store.Initialize();
        var clock = new FixedClock(Start);
        var watcher = new FakeForegroundWindowWatcher();
        using var collector = new UsageCollector("DesktopPuppy", clock, watcher, new NullUsageSink());
        collector.Start();
        store.Dispose();
        var service = new UsageStatisticsQueryService(store, collector, TimeZoneInfo.Utc);

        await Assert.ThrowsAsync<ObjectDisposedException>(() => service.QueryAsync(SelectedDate));
    }

    private static ForegroundSession Session(
        string processName,
        string title,
        DateTimeOffset startedAt,
        DateTimeOffset endedAt,
        Guid? id = null)
    {
        return new ForegroundSession(id ?? Guid.NewGuid(), Snapshot(processName, title), startedAt, endedAt);
    }

    private static WindowTitleInterval Title(
        string processName,
        string title,
        DateTimeOffset startedAt,
        DateTimeOffset? endedAt)
    {
        return new WindowTitleInterval(Guid.NewGuid(), Snapshot(processName, title), startedAt, endedAt);
    }

    private static ForegroundWindowSnapshot Snapshot(string processName, string title, uint processId = 42)
    {
        return new ForegroundWindowSnapshot((nint)processId, processId, processName, title);
    }

    private static DateTimeOffset At(int year, int month, int day, int hour, int minute = 0)
    {
        return new DateTimeOffset(year, month, day, hour, minute, 0, TimeSpan.Zero);
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

        public void RefreshForegroundWindow()
        {
        }

        public void Stop() => _callback = null;

        public void Dispose() => Stop();

        public void Emit(ForegroundWindowSnapshot snapshot) => _callback?.Invoke(snapshot);
    }

    private sealed class NullUsageSink : IUsageEventSink
    {
        public void AppendSession(ForegroundSession session)
        {
        }

        public void UpsertWindowTitleInterval(WindowTitleInterval interval)
        {
        }
    }

    private sealed class TemporaryDatabase : IDisposable
    {
        private readonly string _directory = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "DesktopPuppy.Tests",
            Guid.NewGuid().ToString("N"));

        public string Path => System.IO.Path.Combine(_directory, "usage.db");

        public void Dispose()
        {
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }
        }
    }
}

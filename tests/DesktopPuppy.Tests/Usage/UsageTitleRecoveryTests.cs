using DesktopPuppy.Native;
using DesktopPuppy.Usage;
using Microsoft.Data.Sqlite;
using Xunit;

namespace DesktopPuppy.Tests.Usage;

public sealed class UsageTitleRecoveryTests : IDisposable
{
    private static readonly DateTimeOffset Start = new(2026, 9, 23, 12, 0, 0, TimeSpan.Zero);
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "DesktopPuppy.Tests", Guid.NewGuid().ToString("N"));
    private string DatabasePath => Path.Combine(_directory, "usage.db");

    [Fact]
    public void Restart_seals_an_uncheckpointed_open_title_at_its_start_without_inventing_time()
    {
        using (var previous = new UsageSqliteStore(DatabasePath))
        {
            previous.Initialize();
            previous.UpsertWindowTitleInterval(OpenTitle());
        }
        using var restarted = new UsageSqliteStore(DatabasePath);
        restarted.Initialize();
        restarted.RecoverAbandonedTitleIntervals();
        var recovered = Assert.Single(restarted.ReadWindowTitleIntervals());
        Assert.Equal(Start, recovered.EndedAt);
        Assert.Empty(DailyUsageAggregator.Aggregate([], [recovered], Start.AddDays(1), TimeZoneInfo.Utc).TitleUsage);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(32)]
    public void Restart_seals_at_the_last_durable_observation_not_at_restart_time(int lastObservedSecond)
    {
        // Real debounce + SQLite, deliberately abandon the interval without normal close.
        using (var previous = new UsageSqliteStore(DatabasePath))
        {
            previous.Initialize();
            var debouncer = new WindowTitleDebouncer(TimeSpan.FromSeconds(2), previous.UpsertWindowTitleInterval);
            debouncer.OnTitleChanged(Snapshot(), Start);
            debouncer.OnTitleChanged(Snapshot(), Start.AddSeconds(2));
            debouncer.Advance(Start.AddSeconds(2));
            if (lastObservedSecond == 32)
            {
                debouncer.OnTitleChanged(Snapshot(), Start.AddSeconds(32));
                debouncer.Advance(Start.AddSeconds(32));
            }
        }
        using var restarted = new UsageSqliteStore(DatabasePath);
        restarted.Initialize();
        restarted.RecoverAbandonedTitleIntervals();
        var recovered = Assert.Single(restarted.ReadWindowTitleIntervals());
        Assert.Equal(Start.AddSeconds(lastObservedSecond), recovered.EndedAt);
        var report = DailyUsageAggregator.Aggregate([], [recovered], Start.AddDays(1), TimeZoneInfo.Utc);
        Assert.Equal(TimeSpan.FromSeconds(lastObservedSecond), Assert.Single(report.TitleUsage).Duration);
    }

    [Fact]
    public void Advancing_the_clock_without_a_new_observation_does_not_extend_recovery_time()
    {
        using (var previous = new UsageSqliteStore(DatabasePath))
        {
            previous.Initialize();
            var debouncer = new WindowTitleDebouncer(TimeSpan.FromSeconds(2), previous.UpsertWindowTitleInterval);
            debouncer.OnTitleChanged(Snapshot(), Start);
            debouncer.OnTitleChanged(Snapshot(), Start.AddSeconds(2));
            debouncer.Advance(Start.AddSeconds(2));
            debouncer.Advance(Start.AddMinutes(10));
        }
        using var restarted = new UsageSqliteStore(DatabasePath);
        restarted.Initialize();
        restarted.RecoverAbandonedTitleIntervals();
        Assert.Equal(Start.AddSeconds(2), Assert.Single(restarted.ReadWindowTitleIntervals()).EndedAt);
    }

    [Fact]
    public void Reinitializing_the_live_store_keeps_its_current_title_open()
    {
        using var store = new UsageSqliteStore(DatabasePath);
        store.Initialize();
        store.UpsertWindowTitleInterval(OpenTitle());
        store.Initialize();
        Assert.Null(Assert.Single(store.ReadWindowTitleIntervals()).EndedAt);
    }

    [Fact]
    public void Restart_preserves_a_normally_closed_title_end()
    {
        using (var previous = new UsageSqliteStore(DatabasePath))
        {
            previous.Initialize();
            previous.UpsertWindowTitleInterval(OpenTitle() with { EndedAt = Start.AddMinutes(5) });
        }
        using var restarted = new UsageSqliteStore(DatabasePath);
        restarted.Initialize();
        restarted.RecoverAbandonedTitleIntervals();
        Assert.Equal(Start.AddMinutes(5), Assert.Single(restarted.ReadWindowTitleIntervals()).EndedAt);
    }

    [Fact]
    public void Legacy_schema_is_migrated_and_open_titles_are_conservatively_sealed()
    {
        Directory.CreateDirectory(_directory);
        using (var connection = new SqliteConnection($"Data Source={DatabasePath};Pooling=False"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE window_title_intervals (
                    id TEXT PRIMARY KEY NOT NULL, window_handle INTEGER NOT NULL,
                    process_id INTEGER NOT NULL, process_name TEXT NOT NULL,
                    window_title TEXT NOT NULL, started_at_utc TEXT NOT NULL,
                    ended_at_utc TEXT NULL);
                INSERT INTO window_title_intervals VALUES ($id, 42, 42, 'Code', 'main.cs', $start, NULL);
                CREATE TABLE settings (value TEXT NOT NULL);
                INSERT INTO settings VALUES ('preserved');
                """;
            command.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("D"));
            command.Parameters.AddWithValue("$start", "2026-09-23T12:00:00.0000000+00:00");
            command.ExecuteNonQuery();
        }
        using var store = new UsageSqliteStore(DatabasePath);
        store.Initialize();
        store.RecoverAbandonedTitleIntervals();
        Assert.Equal(Start, Assert.Single(store.ReadWindowTitleIntervals()).EndedAt);
        using var verify = new SqliteConnection($"Data Source={DatabasePath};Pooling=False");
        verify.Open();
        using var check = verify.CreateCommand();
        check.CommandText = "SELECT value FROM settings";
        Assert.Equal("preserved", check.ExecuteScalar());
    }

    [Fact]
    public void Initializing_an_observer_store_does_not_close_the_owners_live_title()
    {
        using var owner = new UsageSqliteStore(DatabasePath);
        owner.Initialize();
        owner.UpsertWindowTitleInterval(OpenTitle());
        using var observer = new UsageSqliteStore(DatabasePath);
        observer.Initialize();
        Assert.Null(Assert.Single(observer.ReadWindowTitleIntervals()).EndedAt);
        Assert.Null(Assert.Single(owner.ReadWindowTitleIntervals()).EndedAt);
    }

    private static ForegroundWindowSnapshot Snapshot() => new((nint)42, 42, "Code", "main.cs");
    private static WindowTitleInterval OpenTitle() => new(Guid.NewGuid(), Snapshot(), Start, null);

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }
}

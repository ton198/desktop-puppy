using Microsoft.Data.Sqlite;
using DesktopPuppy.Native;
using DesktopPuppy.Usage;
using Xunit;

namespace DesktopPuppy.Tests.Usage;

public sealed class UsageSqliteStoreRetentionTests : IDisposable
{
    private readonly string _databasePath = Path.Combine(
        Path.GetTempPath(), "DesktopPuppy.Tests", Guid.NewGuid().ToString("N"), "usage.db");

    private static readonly TimeZoneInfo Eastern = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");

    public void Dispose()
    {
        var directory = Path.GetDirectoryName(_databasePath);
        if (directory is not null && Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Retention_cutoff_is_midnight_of_oldest_of_fifteen_local_dates()
    {
        var now = At(2026, 9, 24, 15);

        var cutoff = UsageRetentionPolicy.GetCutoffUtc(now, TimeZoneInfo.Utc);

        Assert.Equal(At(2026, 9, 10, 0), cutoff);
    }

    [Fact]
    public void Session_ending_exactly_at_cutoff_is_deleted_but_session_starting_at_cutoff_is_kept()
    {
        var cutoff = At(2026, 9, 10, 0);
        using var store = CreateStore();
        store.AppendSession(Session("Expired", cutoff.AddMinutes(-10), cutoff));
        var retained = Session("Retained", cutoff, cutoff.AddMinutes(10));
        store.AppendSession(retained);

        store.PruneBefore(cutoff);

        var result = Assert.Single(store.ReadSessions());
        Assert.Equal(retained.Id, result.Id);
        Assert.Equal(cutoff, result.StartedAt);
    }

    [Fact]
    public void Sessions_and_closed_titles_wholly_older_than_cutoff_are_deleted()
    {
        var cutoff = At(2026, 9, 10, 0);
        using var store = CreateStore();
        store.AppendSession(Session("Old", cutoff.AddHours(-2), cutoff.AddHours(-1)));
        store.AppendSession(Session("New", cutoff, cutoff.AddMinutes(5)));
        store.UpsertWindowTitleInterval(Title("Old", "old", cutoff.AddHours(-2), cutoff.AddHours(-1)));
        store.UpsertWindowTitleInterval(Title("New", "new", cutoff, cutoff.AddMinutes(5)));

        store.PruneBefore(cutoff);

        Assert.Equal("New", Assert.Single(store.ReadSessions()).ProcessName);
        Assert.Equal("new", Assert.Single(store.ReadWindowTitleIntervals()).WindowTitle);
    }

    [Fact]
    public void Session_crossing_cutoff_is_clipped_and_keeps_only_retained_duration()
    {
        var cutoff = At(2026, 9, 10, 0);
        using var store = CreateStore();
        var session = Session("Code", cutoff.AddHours(-1), cutoff.AddHours(2));
        store.AppendSession(session);

        store.PruneBefore(cutoff);

        var result = Assert.Single(store.ReadSessions());
        Assert.Equal(session.Id, result.Id);
        Assert.Equal(cutoff, result.StartedAt);
        Assert.Equal(cutoff.AddHours(2), result.EndedAt);
        Assert.Equal(TimeSpan.FromHours(2), result.EndedAt - result.StartedAt);
    }

    [Fact]
    public void Closed_title_interval_crossing_cutoff_is_clipped_without_changing_its_end()
    {
        var cutoff = At(2026, 9, 10, 0);
        using var store = CreateStore();
        var interval = Title("Code", "main.cs", cutoff.AddMinutes(-15), cutoff.AddMinutes(30));
        store.UpsertWindowTitleInterval(interval);

        store.PruneBefore(cutoff);

        var result = Assert.Single(store.ReadWindowTitleIntervals());
        Assert.Equal(interval.Id, result.Id);
        Assert.Equal(cutoff, result.StartedAt);
        Assert.Equal(interval.EndedAt, result.EndedAt);
        Assert.Equal("main.cs", result.WindowTitle);
    }

    [Fact]
    public void Open_title_interval_crossing_cutoff_is_clipped_and_remains_open()
    {
        var cutoff = At(2026, 9, 10, 0);
        using var store = CreateStore();
        var interval = Title("Code", "main.cs", cutoff.AddHours(-1), null);
        store.UpsertWindowTitleInterval(interval);

        store.PruneBefore(cutoff);

        var clipped = Assert.Single(store.ReadWindowTitleIntervals());
        Assert.Equal(cutoff, clipped.StartedAt);
        Assert.Null(clipped.EndedAt);
        store.UpsertWindowTitleInterval(clipped with { EndedAt = cutoff.AddMinutes(30) });
        var closed = Assert.Single(store.ReadWindowTitleIntervals());
        Assert.Equal(cutoff, closed.StartedAt);
        Assert.Equal(cutoff.AddMinutes(30), closed.EndedAt);
    }

    [Fact]
    public void Checkpoint_upsert_does_not_restore_a_title_start_removed_by_retention()
    {
        var cutoff = At(2026, 9, 10, 0);
        using var store = CreateStore();
        var original = Title("Code", "main.cs", cutoff.AddHours(-1), null);
        store.UpsertWindowTitleInterval(original);
        store.PruneBefore(cutoff);
        // The debouncer still holds the original start when its next checkpoint is written.
        store.UpsertWindowTitleInterval(original with { LastConfirmedAt = cutoff.AddMinutes(1) });
        var checkpointed = Assert.Single(store.ReadWindowTitleIntervals());
        Assert.Equal(cutoff, checkpointed.StartedAt);
        Assert.Null(checkpointed.EndedAt);
    }

    [Fact]
    public void Repeated_retention_cleanup_is_idempotent()
    {
        var cutoff = At(2026, 9, 10, 0);
        using var store = CreateStore();
        store.AppendSession(Session("Old", cutoff.AddHours(-2), cutoff.AddHours(-1)));
        store.AppendSession(Session("Code", cutoff.AddHours(-1), cutoff.AddHours(1)));

        store.PruneBefore(cutoff);
        var first = store.ReadSessions();
        store.PruneBefore(cutoff);

        Assert.Equal(first, store.ReadSessions());
    }

    [Fact]
    public void Retention_cleanup_does_not_modify_unrelated_tables()
    {
        var cutoff = At(2026, 9, 10, 0);
        using var store = CreateStore();
        store.AppendSession(Session("Old", cutoff.AddHours(-2), cutoff.AddHours(-1)));
        using (var connection = OpenRawConnection())
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "CREATE TABLE settings (id TEXT PRIMARY KEY, value TEXT NOT NULL); INSERT INTO settings VALUES ('theme', 'dark');";
            command.ExecuteNonQuery();
        }

        store.PruneBefore(cutoff);

        using var verify = OpenRawConnection();
        using var verifyCommand = verify.CreateCommand();
        verifyCommand.CommandText = "SELECT value FROM settings WHERE id = 'theme';";
        Assert.Equal("dark", verifyCommand.ExecuteScalar());
        Assert.Empty(store.ReadSessions());
    }

    [Fact]
    public void Retention_cutoff_tracks_local_calendar_across_daylight_saving_change()
    {
        var nowUtc = At(2026, 3, 20, 15);

        var cutoff = UsageRetentionPolicy.GetCutoffUtc(nowUtc, Eastern);

        Assert.Equal(At(2026, 3, 6, 5), cutoff);
        Assert.Equal(new DateOnly(2026, 3, 6), DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(cutoff, Eastern).DateTime));
        var todayLocalMidnightUtc = At(2026, 3, 20, 4);
        Assert.Equal(TimeSpan.FromHours(335), todayLocalMidnightUtc - cutoff);
    }

    [Fact]
    public void Clipped_timestamps_round_trip_as_utc_instants()
    {
        var nowUtc = At(2026, 9, 24, 16);
        var cutoff = UsageRetentionPolicy.GetCutoffUtc(nowUtc, Eastern);
        using var store = CreateStore();
        var originalEndUtc = cutoff.AddHours(2);
        var session = Session(
            "Code",
            cutoff.AddHours(-1).ToOffset(TimeSpan.FromHours(-4)),
            originalEndUtc.ToOffset(TimeSpan.FromHours(-4)));
        store.AppendSession(session);

        store.PruneBefore(cutoff);

        var result = Assert.Single(store.ReadSessions());
        Assert.Equal(cutoff, result.StartedAt);
        Assert.Equal(originalEndUtc, result.EndedAt);
        Assert.Equal(TimeSpan.Zero, result.StartedAt.Offset);
        Assert.Equal(TimeSpan.Zero, result.EndedAt!.Value.Offset);
    }

    private UsageSqliteStore CreateStore()
    {
        var store = new UsageSqliteStore(_databasePath);
        store.Initialize();
        return store;
    }

    private SqliteConnection OpenRawConnection()
    {
        var connection = new SqliteConnection($"Data Source={_databasePath};Pooling=False");
        connection.Open();
        return connection;
    }

    private static ForegroundSession Session(string processName, DateTimeOffset start, DateTimeOffset end)
    {
        return new ForegroundSession(Guid.NewGuid(), Snapshot(processName, "session"), start, end);
    }

    private static WindowTitleInterval Title(string processName, string title, DateTimeOffset start, DateTimeOffset? end)
    {
        return new WindowTitleInterval(Guid.NewGuid(), Snapshot(processName, title), start, end);
    }

    private static ForegroundWindowSnapshot Snapshot(string processName, string title)
    {
        return new ForegroundWindowSnapshot((nint)42, 42, processName, title);
    }

    private static DateTimeOffset At(int year, int month, int day, int hour, int minute = 0)
    {
        return new DateTimeOffset(year, month, day, hour, minute, 0, TimeSpan.Zero);
    }
}

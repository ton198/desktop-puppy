using DesktopPuppy.Native;
using DesktopPuppy.Usage;
using Xunit;

namespace DesktopPuppy.Tests.Usage;

public sealed class UsageSqliteStoreTests : IDisposable
{
    private readonly string _databasePath = Path.Combine(
        Path.GetTempPath(),
        "DesktopPuppy.Tests",
        Guid.NewGuid().ToString("N"),
        "usage.db");

    public void Dispose()
    {
        var directory = Path.GetDirectoryName(_databasePath);
        if (directory is not null && Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Session_round_trip_preserves_process_title_and_utc_instants()
    {
        using var store = new UsageSqliteStore(_databasePath);
        store.Initialize();
        var startedAt = new DateTimeOffset(2026, 9, 23, 16, 0, 0, TimeSpan.Zero);
        var endedAt = startedAt.AddMinutes(5);
        var session = new ForegroundSession(
            Guid.NewGuid(),
            new ForegroundWindowSnapshot((nint)42, 42, "Code", "main.cs"),
            startedAt,
            endedAt);

        store.AppendSession(session);

        var result = Assert.Single(store.ReadSessions());
        Assert.Equal(session.Id, result.Id);
        Assert.Equal("Code", result.ProcessName);
        Assert.Equal("main.cs", result.WindowTitle);
        Assert.Equal(startedAt, result.StartedAt);
        Assert.Equal(endedAt, result.EndedAt);
    }

    [Fact]
    public void Stable_title_interval_is_upserted_by_id()
    {
        using var store = new UsageSqliteStore(_databasePath);
        store.Initialize();
        var startedAt = new DateTimeOffset(2026, 9, 23, 16, 0, 0, TimeSpan.Zero);
        var interval = new WindowTitleInterval(
            Guid.NewGuid(),
            new ForegroundWindowSnapshot((nint)42, 42, "Code", "main.cs"),
            startedAt,
            EndedAt: null);

        store.UpsertWindowTitleInterval(interval);
        store.UpsertWindowTitleInterval(interval with { EndedAt = startedAt.AddMinutes(2) });

        var result = Assert.Single(store.ReadWindowTitleIntervals());
        Assert.Equal(interval.Id, result.Id);
        Assert.Equal(startedAt.AddMinutes(2), result.EndedAt);
    }

    [Fact]
    public void Initialize_is_idempotent_and_does_not_delete_existing_records()
    {
        using var store = new UsageSqliteStore(_databasePath);
        store.Initialize();
        var session = new ForegroundSession(
            Guid.NewGuid(),
            new ForegroundWindowSnapshot((nint)42, 42, "Code", "main.cs"),
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow.AddMinutes(1));
        store.AppendSession(session);

        store.Initialize();

        Assert.Single(store.ReadSessions());
    }
}

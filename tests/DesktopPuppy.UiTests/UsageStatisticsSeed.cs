using System.IO;
using DesktopPuppy.Native;
using DesktopPuppy.Usage;

namespace DesktopPuppy.UiTests;

public sealed class UsageStatisticsSeed : IDisposable
{
    private readonly string _directory;

    public UsageStatisticsSeed()
    {
        _directory = Path.Combine(Path.GetTempPath(), "DesktopPuppy.UiTests", Guid.NewGuid().ToString("N"));
        DatabasePath = Path.Combine(_directory, "usage.db");
        using var store = OpenStore();
    }

    public string DatabasePath { get; }

    public DateOnly Yesterday => DateOnly.FromDateTime(DateTime.Now).AddDays(-1);

    public DateOnly DayBeforeYesterday => Yesterday.AddDays(-1);

    public DateTimeOffset YesterdayStartUtc => StartOfLocalDateUtc(Yesterday);

    public void SeedRanking()
    {
        var start = YesterdayStartUtc.AddHours(9);
        AppendUsage("BrowserApp", "Documentation", start, TimeSpan.FromMinutes(60));
        AppendUsage("BrowserApp", "Dashboard", start.AddMinutes(60), TimeSpan.FromMinutes(30));
        AppendUsage("CodeApp", "Editor", start.AddHours(2), TimeSpan.FromMinutes(45));
    }

    public void AppendUsage(string processName, string title, DateTimeOffset start, TimeSpan duration)
    {
        using var store = OpenStore();
        var snapshot = Snapshot(processName, title);
        store.AppendSession(new ForegroundSession(Guid.NewGuid(), snapshot, start, start + duration));
        store.UpsertWindowTitleInterval(new WindowTitleInterval(Guid.NewGuid(), snapshot, start, start + duration));
    }

    public (Guid ExpiredId, Guid CrossingId, DateTimeOffset CutoffUtc) SeedRetentionRows()
    {
        var cutoff = UsageRetentionPolicy.GetCutoffUtc(DateTimeOffset.UtcNow, TimeZoneInfo.Local);
        var expired = new ForegroundSession(
            Guid.NewGuid(), Snapshot("ExpiredApp", "expired"), cutoff.AddHours(-2), cutoff.AddMinutes(-1));
        var crossing = new ForegroundSession(
            Guid.NewGuid(), Snapshot("CrossingApp", "crossing"), cutoff.AddHours(-1), cutoff.AddHours(1));
        using var store = OpenStore();
        store.AppendSession(expired);
        store.AppendSession(crossing);
        return (expired.Id, crossing.Id, cutoff);
    }

    public IReadOnlyList<ForegroundSession> ReadSessions()
    {
        using var store = OpenStore();
        return store.ReadSessions();
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private static DateTimeOffset StartOfLocalDateUtc(DateOnly localDate)
    {
        var localMidnight = DateTime.SpecifyKind(localDate.ToDateTime(TimeOnly.MinValue), DateTimeKind.Unspecified);
        return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(localMidnight, TimeZoneInfo.Local), TimeSpan.Zero);
    }

    private UsageSqliteStore OpenStore()
    {
        var store = new UsageSqliteStore(DatabasePath);
        store.Initialize();
        return store;
    }

    private static ForegroundWindowSnapshot Snapshot(string processName, string title)
    {
        var processId = unchecked((uint)Math.Abs(processName.GetHashCode(StringComparison.Ordinal)));
        return new ForegroundWindowSnapshot((nint)processId, processId, processName, title);
    }
}

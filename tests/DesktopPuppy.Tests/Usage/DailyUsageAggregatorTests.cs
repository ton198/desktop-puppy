using DesktopPuppy.Native;
using DesktopPuppy.Usage;
using Xunit;

namespace DesktopPuppy.Tests.Usage;

public sealed class DailyUsageAggregatorTests
{
    private static readonly TimeZoneInfo Eastern = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");

    [Fact]
    public void Same_day_session_contributes_full_duration_to_one_local_day()
    {
        var report = Aggregate(
            [Session("Code", At(2026, 9, 24, 9), At(2026, 9, 24, 10, 30))],
            [],
            At(2026, 9, 24, 11),
            TimeZoneInfo.Utc);

        var usage = Assert.Single(report.ProcessUsage);
        Assert.Equal(new DateOnly(2026, 9, 24), usage.LocalDate);
        Assert.Equal("Code", usage.ProcessName);
        Assert.Equal(TimeSpan.FromMinutes(90), usage.Duration);
    }

    [Fact]
    public void Session_crossing_local_midnight_is_split_between_both_dates()
    {
        var report = Aggregate(
            [Session("Code", At(2026, 9, 23, 23, 30), At(2026, 9, 24, 0, 30))],
            [],
            At(2026, 9, 24, 1),
            TimeZoneInfo.Utc);

        Assert.Collection(
            report.ProcessUsage.OrderBy(item => item.LocalDate),
            first =>
            {
                Assert.Equal(new DateOnly(2026, 9, 23), first.LocalDate);
                Assert.Equal(TimeSpan.FromMinutes(30), first.Duration);
            },
            second =>
            {
                Assert.Equal(new DateOnly(2026, 9, 24), second.LocalDate);
                Assert.Equal(TimeSpan.FromMinutes(30), second.Duration);
            });
        Assert.Equal(TimeSpan.FromHours(1), report.ProcessUsage.Aggregate(TimeSpan.Zero, (sum, item) => sum + item.Duration));
    }

    [Fact]
    public void Session_ending_at_midnight_does_not_create_zero_duration_next_day_bucket()
    {
        var report = Aggregate(
            [Session("Code", At(2026, 9, 23, 23, 45), At(2026, 9, 24, 0))],
            [],
            At(2026, 9, 24, 1),
            TimeZoneInfo.Utc);

        var usage = Assert.Single(report.ProcessUsage);
        Assert.Equal(new DateOnly(2026, 9, 23), usage.LocalDate);
        Assert.Equal(TimeSpan.FromMinutes(15), usage.Duration);
    }

    [Fact]
    public void Aggregation_uses_injected_local_timezone_instead_of_UTC_date()
    {
        var report = Aggregate(
            [Session("Code", At(2026, 9, 24, 2, 30), At(2026, 9, 24, 3, 30))],
            [],
            At(2026, 9, 24, 4),
            Eastern);

        var usage = Assert.Single(report.ProcessUsage);
        Assert.Equal(new DateOnly(2026, 9, 23), usage.LocalDate);
        Assert.Equal(TimeSpan.FromHours(1), usage.Duration);
    }

    [Fact]
    public void Multiple_sessions_for_same_process_are_summed_without_merging_other_processes()
    {
        var report = Aggregate(
            [
                Session("Code", At(2026, 9, 24, 9), At(2026, 9, 24, 9, 10)),
                Session("Code", At(2026, 9, 24, 10), At(2026, 9, 24, 10, 20)),
                Session("Browser", At(2026, 9, 24, 11), At(2026, 9, 24, 11, 15))
            ],
            [],
            At(2026, 9, 24, 12),
            TimeZoneInfo.Utc);

        Assert.Equal(2, report.ProcessUsage.Count);
        Assert.Equal(TimeSpan.FromMinutes(30), Assert.Single(report.ProcessUsage, item => item.ProcessName == "Code").Duration);
        Assert.Equal(TimeSpan.FromMinutes(15), Assert.Single(report.ProcessUsage, item => item.ProcessName == "Browser").Duration);
    }

    [Fact]
    public void Title_intervals_split_at_midnight_and_remain_separate_by_title()
    {
        var session = Session("Code", At(2026, 9, 23, 23, 30), At(2026, 9, 24, 1));
        var titles = new[]
        {
            Title("Code", "main.cs", At(2026, 9, 23, 23, 30), At(2026, 9, 24, 0, 30)),
            Title("Code", "notes.txt", At(2026, 9, 24, 0, 30), At(2026, 9, 24, 1))
        };

        var report = Aggregate([session], titles, At(2026, 9, 24, 2), TimeZoneInfo.Utc);

        Assert.Equal(3, report.TitleUsage.Count);
        Assert.Contains(report.TitleUsage, item => item.LocalDate == new DateOnly(2026, 9, 23)
            && item.WindowTitle == "main.cs" && item.Duration == TimeSpan.FromMinutes(30));
        Assert.Contains(report.TitleUsage, item => item.LocalDate == new DateOnly(2026, 9, 24)
            && item.WindowTitle == "main.cs" && item.Duration == TimeSpan.FromMinutes(30));
        Assert.Contains(report.TitleUsage, item => item.LocalDate == new DateOnly(2026, 9, 24)
            && item.WindowTitle == "notes.txt" && item.Duration == TimeSpan.FromMinutes(30));
        foreach (var processUsage in report.ProcessUsage)
        {
            var titleTotal = report.TitleUsage
                .Where(item => item.LocalDate == processUsage.LocalDate && item.ProcessName == processUsage.ProcessName)
                .Aggregate(TimeSpan.Zero, (sum, item) => sum + item.Duration);
            Assert.Equal(processUsage.Duration, titleTotal);
        }
    }

    [Fact]
    public void Open_title_interval_is_capped_at_explicit_as_of_time()
    {
        var start = At(2026, 9, 24, 10);
        var asOf = start.AddMinutes(20);
        var title = Title("Code", "main.cs", start, null);

        var first = Aggregate([], [title], asOf, TimeZoneInfo.Utc);
        var second = Aggregate([], [title], asOf, TimeZoneInfo.Utc);

        Assert.Equal(TimeSpan.FromMinutes(20), Assert.Single(first.TitleUsage).Duration);
        Assert.Equal(first.ProcessUsage, second.ProcessUsage);
        Assert.Equal(first.TitleUsage, second.TitleUsage);
    }

    [Fact]
    public void Spring_forward_day_uses_elapsed_utc_duration_and_one_local_date()
    {
        var report = Aggregate(
            [Session("Code", At(2026, 3, 8, 6, 30), At(2026, 3, 8, 7, 30))],
            [],
            At(2026, 3, 8, 8),
            Eastern);

        var usage = Assert.Single(report.ProcessUsage);
        Assert.Equal(new DateOnly(2026, 3, 8), usage.LocalDate);
        Assert.Equal(TimeSpan.FromMinutes(60), usage.Duration);
    }

    [Fact]
    public void Fall_back_day_counts_repeated_local_hour_once_per_elapsed_interval()
    {
        var report = Aggregate(
            [Session("Code", At(2026, 11, 1, 5, 30), At(2026, 11, 1, 7, 30))],
            [],
            At(2026, 11, 1, 8),
            Eastern);

        var usage = Assert.Single(report.ProcessUsage);
        Assert.Equal(new DateOnly(2026, 11, 1), usage.LocalDate);
        Assert.Equal(TimeSpan.FromMinutes(120), usage.Duration);
    }

    private static DailyUsageReport Aggregate(
        IReadOnlyCollection<ForegroundSession> sessions,
        IReadOnlyCollection<WindowTitleInterval> titles,
        DateTimeOffset asOfUtc,
        TimeZoneInfo timeZone)
    {
        return DailyUsageAggregator.Aggregate(sessions, titles, asOfUtc, timeZone);
    }

    private static ForegroundSession Session(string processName, DateTimeOffset start, DateTimeOffset end)
    {
        var snapshot = Snapshot(processName, "session title");
        return new ForegroundSession(Guid.NewGuid(), snapshot, start, end);
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




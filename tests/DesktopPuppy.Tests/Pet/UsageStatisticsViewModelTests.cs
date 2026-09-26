using DesktopPuppy.Pet.Statistics;
using DesktopPuppy.Usage;
using Xunit;

namespace DesktopPuppy.Tests.Pet;

public sealed class UsageStatisticsViewModelTests
{
    private static readonly DateTimeOffset FixedUtcNow =
        new(2026, 9, 25, 12, 30, 0, TimeSpan.Zero);

    // Purpose: Match the date selector exactly to the retention window.
    // Coverage: A fixed UTC clock near a local-date boundary and an injected UTC+14 time zone.
    // Target effect: Fifteen consecutive local dates appear newest first, with local today selected.
    [Fact]
    public void View_model_exposes_today_and_previous_fourteen_local_dates()
    {
        var timeZone = TimeZoneInfo.CreateCustomTimeZone(
            "UsageStatisticsViewModelTests-UTC+14",
            TimeSpan.FromHours(14),
            "UTC+14",
            "UTC+14");
        var viewModel = new UsageStatisticsViewModel(
            new RecordingQuery(),
            timeZone,
            () => FixedUtcNow);

        Assert.Equal(15, viewModel.AvailableDates.Count);
        Assert.Equal(new DateOnly(2026, 9, 26), viewModel.SelectedDate);
        Assert.Equal(
            [
                new DateOnly(2026, 9, 26),
                new DateOnly(2026, 9, 25),
                new DateOnly(2026, 9, 24),
                new DateOnly(2026, 9, 23),
                new DateOnly(2026, 9, 22),
                new DateOnly(2026, 9, 21),
                new DateOnly(2026, 9, 20),
                new DateOnly(2026, 9, 19),
                new DateOnly(2026, 9, 18),
                new DateOnly(2026, 9, 17),
                new DateOnly(2026, 9, 16),
                new DateOnly(2026, 9, 15),
                new DateOnly(2026, 9, 14),
                new DateOnly(2026, 9, 13),
                new DateOnly(2026, 9, 12)
            ],
            viewModel.AvailableDates);
    }

    // Purpose: Ensure the user can navigate retained history.
    // Coverage: Selecting a prior date and receiving a fake query response for that date.
    // Target effect: The requested date, SelectedDate, and Statistics.LocalDate all match.
    [Fact]
    public async Task Selecting_date_queries_and_displays_that_date()
    {
        var selectedDate = new DateOnly(2026, 9, 22);
        var query = new RecordingQuery(
            date => Task.FromResult(EmptyStatistics(date)));
        var viewModel = new UsageStatisticsViewModel(
            query,
            TimeZoneInfo.Utc,
            () => FixedUtcNow);

        await viewModel.SelectDateAsync(selectedDate);

        Assert.Equal([selectedDate], query.RequestedDates);
        Assert.Equal(selectedDate, viewModel.SelectedDate);
        Assert.Equal(selectedDate, Assert.IsType<DailyUsageStatistics>(viewModel.Statistics).LocalDate);
    }

    // Purpose: Keep window-title details tied to the application chosen by the user.
    // Coverage: Two application summaries with different title rows and repeated selection changes.
    // Target effect: SelectedApplication exposes only the chosen app's titles without changing daily totals.
    [Fact]
    public async Task Selecting_application_exposes_only_its_title_details()
    {
        var localDate = new DateOnly(2026, 9, 25);
        var totalDuration = TimeSpan.FromHours(3);
        var browserTitles = new[]
        {
            new WindowTitleStatistics("Docs", TimeSpan.FromHours(1)),
            new WindowTitleStatistics("News", TimeSpan.FromMinutes(30))
        };
        var codeTitles = new[]
        {
            new WindowTitleStatistics("main.cs", TimeSpan.FromMinutes(45))
        };
        var statistics = new DailyUsageStatistics(
            localDate,
            totalDuration,
            [
                new ApplicationStatistics("Browser", TimeSpan.FromHours(2), browserTitles),
                new ApplicationStatistics("Code", TimeSpan.FromHours(1), codeTitles)
            ]);
        var viewModel = new UsageStatisticsViewModel(
            new RecordingQuery(_ => Task.FromResult(statistics)),
            TimeZoneInfo.Utc,
            () => FixedUtcNow);
        await viewModel.LoadAsync();

        viewModel.SelectApplication("Browser");

        Assert.Equal("Browser", Assert.IsType<ApplicationStatistics>(viewModel.SelectedApplication).ProcessName);
        Assert.Equal(browserTitles, viewModel.SelectedApplication.WindowTitles);
        Assert.Equal(totalDuration, Assert.IsType<DailyUsageStatistics>(viewModel.Statistics).TotalDuration);

        viewModel.SelectApplication("Code");

        Assert.Equal("Code", Assert.IsType<ApplicationStatistics>(viewModel.SelectedApplication).ProcessName);
        Assert.Equal(codeTitles, viewModel.SelectedApplication.WindowTitles);
        Assert.Equal(totalDuration, Assert.IsType<DailyUsageStatistics>(viewModel.Statistics).TotalDuration);
    }

    // Purpose: Prevent a slow background read from repainting a date the user has left.
    // Coverage: Two controllable query tasks completed in reverse request order.
    // Target effect: Only the newest request updates date, statistics, loading, and error state.
    [Fact]
    public async Task Older_query_completion_cannot_replace_newer_date_selection()
    {
        var today = new DateOnly(2026, 9, 25);
        var priorDate = new DateOnly(2026, 9, 24);
        var todayCompletion = NewCompletion();
        var priorCompletion = NewCompletion();
        var query = new RecordingQuery(date => date == today
            ? todayCompletion.Task
            : priorCompletion.Task);
        var viewModel = new UsageStatisticsViewModel(
            query,
            TimeZoneInfo.Utc,
            () => FixedUtcNow);

        var olderRequest = viewModel.LoadAsync();
        var newerRequest = viewModel.SelectDateAsync(priorDate);
        priorCompletion.SetResult(new DailyUsageStatistics(
            priorDate,
            TimeSpan.FromMinutes(20),
            [new ApplicationStatistics("Newer", TimeSpan.FromMinutes(20), [])]));
        await newerRequest;
        todayCompletion.SetResult(new DailyUsageStatistics(
            today,
            TimeSpan.FromHours(8),
            [new ApplicationStatistics("Older", TimeSpan.FromHours(8), [])]));
        await olderRequest;

        Assert.Equal([today, priorDate], query.RequestedDates);
        Assert.Equal(priorDate, viewModel.SelectedDate);
        var displayed = Assert.IsType<DailyUsageStatistics>(viewModel.Statistics);
        Assert.Equal(priorDate, displayed.LocalDate);
        Assert.Equal("Newer", Assert.Single(displayed.Applications).ProcessName);
        Assert.False(viewModel.IsLoading);
        Assert.Null(viewModel.ErrorMessage);
    }

    // Purpose: Treat a valid day with no tracked activity as normal data rather than a read error.
    // Coverage: A successful query returning the requested date, zero duration, and no applications.
    // Target effect: Statistics contains the empty report while ErrorMessage remains clear.
    [Fact]
    public async Task Empty_result_produces_normal_zero_state()
    {
        var viewModel = new UsageStatisticsViewModel(
            new RecordingQuery(date => Task.FromResult(EmptyStatistics(date))),
            TimeZoneInfo.Utc,
            () => FixedUtcNow);

        await viewModel.LoadAsync();

        var statistics = Assert.IsType<DailyUsageStatistics>(viewModel.Statistics);
        Assert.Equal(new DateOnly(2026, 9, 25), statistics.LocalDate);
        Assert.Equal(TimeSpan.Zero, statistics.TotalDuration);
        Assert.Empty(statistics.Applications);
        Assert.Null(viewModel.ErrorMessage);
        Assert.False(viewModel.IsLoading);
    }

    // Purpose: Report an unavailable query distinctly instead of fabricating an empty daily report.
    // Coverage: A faulted query task with an operational exception.
    // Target effect: ErrorMessage is populated, Statistics stays null, and loading completes.
    [Fact]
    public async Task Query_failure_produces_error_without_fabricated_empty_report()
    {
        var viewModel = new UsageStatisticsViewModel(
            new RecordingQuery(_ => Task.FromException<DailyUsageStatistics>(
                new IOException("database unavailable"))),
            TimeZoneInfo.Utc,
            () => FixedUtcNow);

        await viewModel.LoadAsync();

        Assert.Contains("database unavailable", viewModel.ErrorMessage);
        Assert.Null(viewModel.Statistics);
        Assert.Null(viewModel.SelectedApplication);
        Assert.False(viewModel.IsLoading);
    }

    private static DailyUsageStatistics EmptyStatistics(DateOnly date)
    {
        return new DailyUsageStatistics(date, TimeSpan.Zero, []);
    }

    private static TaskCompletionSource<DailyUsageStatistics> NewCompletion()
    {
        return new TaskCompletionSource<DailyUsageStatistics>(
            TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed class RecordingQuery(
        Func<DateOnly, Task<DailyUsageStatistics>>? query = null) : IUsageStatisticsQuery
    {
        private readonly Func<DateOnly, Task<DailyUsageStatistics>> _query =
            query ?? (date => Task.FromResult(EmptyStatistics(date)));

        public List<DateOnly> RequestedDates { get; } = [];

        public Task<DailyUsageStatistics> QueryAsync(
            DateOnly localDate,
            CancellationToken cancellationToken = default)
        {
            RequestedDates.Add(localDate);
            return _query(localDate);
        }
    }
}

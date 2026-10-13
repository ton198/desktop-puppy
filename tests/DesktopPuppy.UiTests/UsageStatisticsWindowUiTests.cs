using System.IO;
using DesktopPuppy.Usage;
using Xunit;

namespace DesktopPuppy.UiTests;

public sealed class UsageStatisticsWindowUiTests
{
    // Purpose: Verify the visible feature is reachable without enlarging or replacing the pet.
    // Coverage: Real application process, pet Automation ID, Statistics button invocation, and separate window Automation ID.
    // Target effect: Both windows are visible in the same process; the pet remains available and unchanged in behavior.
    [Fact]
    [Trait("Category", "Ui")]
    public void Statistics_button_opens_separate_statistics_window()
    {
        using var seed = new UsageStatisticsSeed();
        using var harness = StartHarness(seed.DatabasePath);
        var petWindow = harness.WaitForPetWindow(TimeSpan.FromSeconds(5));

        OpenStatistics(harness, petWindow);

        var statisticsWindow = harness.WaitForTopLevelWindow("UsageStatisticsWindow", TimeSpan.FromSeconds(5));
        Assert.False(statisticsWindow.Current.IsOffscreen);
        OpenStatistics(harness, petWindow);
        Assert.Equal(1, harness.CountTopLevelWindows("UsageStatisticsWindow"));
        Assert.False(harness.WaitForPetWindow(TimeSpan.FromSeconds(1)).Current.IsOffscreen);
    }

    // Purpose: Verify that WPF bindings present real stored data and date/application selections reach the query.
    // Coverage: Isolated SQLite data for two prior-date applications, date selection, ranking, application selection, and title detail.
    // Target effect: The seeded date, total, descending application order, and selected application's title durations are visible and correct.
    [Fact]
    [Trait("Category", "Ui")]
    public void Statistics_window_shows_seeded_date_ranking_and_selected_title_detail()
    {
        using var seed = new UsageStatisticsSeed();
        seed.SeedRanking();
        using var harness = StartHarness(seed.DatabasePath);
        var petWindow = harness.WaitForPetWindow(TimeSpan.FromSeconds(5));
        OpenStatistics(harness, petWindow);
        var statisticsWindow = harness.WaitForTopLevelWindow("UsageStatisticsWindow", TimeSpan.FromSeconds(5));

        harness.SelectComboBoxItem(statisticsWindow, "StatisticsDateSelector", seed.Yesterday.ToString("yyyy-MM-dd"));

        Assert.Equal(
            "2 小时 15 分钟",
            harness.WaitForAutomationName(
                statisticsWindow,
                "StatisticsTotalDuration",
                name => name == "2 小时 15 分钟",
                TimeSpan.FromSeconds(5)));
        var applications = harness.WaitForListItemNames(
            statisticsWindow, "StatisticsApplicationList", 2, TimeSpan.FromSeconds(5));
        Assert.Contains("BrowserApp", applications[0]);
        Assert.Contains("1 小时 30 分钟", applications[0]);
        Assert.Contains("CodeApp", applications[1]);
        Assert.Contains("0 小时 45 分钟", applications[1]);

        harness.SelectListItemContaining(statisticsWindow, "StatisticsApplicationList", "BrowserApp");
        var titles = harness.WaitForListItemNames(
            statisticsWindow, "StatisticsTitleList", 2, TimeSpan.FromSeconds(5));
        Assert.Contains(titles, item => item.Contains("Documentation") && item.Contains("1 小时 0 分钟"));
        Assert.Contains(titles, item => item.Contains("Dashboard") && item.Contains("0 小时 30 分钟"));
    }

    // Purpose: Verify a normal no-activity day is not presented as an error.
    // Coverage: Date selection for an unseeded retained local date and the distinct empty/error automation states.
    // Target effect: Empty-state text is visible and error-state text is absent.
    [Fact]
    [Trait("Category", "Ui")]
    public void Statistics_window_shows_empty_state_for_a_date_without_usage()
    {
        using var seed = new UsageStatisticsSeed();
        seed.SeedRanking();
        using var harness = StartHarness(seed.DatabasePath);
        var petWindow = harness.WaitForPetWindow(TimeSpan.FromSeconds(5));
        OpenStatistics(harness, petWindow);
        var statisticsWindow = harness.WaitForTopLevelWindow("UsageStatisticsWindow", TimeSpan.FromSeconds(5));

        harness.SelectComboBoxItem(statisticsWindow, "StatisticsDateSelector", seed.DayBeforeYesterday.ToString("yyyy-MM-dd"));

        var emptyState = harness.WaitForAutomationId(statisticsWindow, "StatisticsEmptyState", TimeSpan.FromSeconds(5));
        Assert.Equal("统计空状态", emptyState.Current.Name);
        harness.WaitForAutomationIdToDisappear(statisticsWindow, "StatisticsErrorState", TimeSpan.FromSeconds(2));
    }

    // Purpose: Preserve pet startup when optional tracking or database access fails.
    // Coverage: Real launch with an existing directory as the database path, pet entry, statistics window, and unavailable state.
    // Target effect: Pet and statistics windows open while statistics explicitly reports unavailable data.
    [Fact]
    [Trait("Category", "Ui")]
    public void Usage_tracking_startup_failure_does_not_prevent_statistics_window_from_opening()
    {
        var directoryPath = Path.Combine(Path.GetTempPath(), "DesktopPuppy.UiTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directoryPath);
        try
        {
            using var harness = StartHarness(directoryPath);
            var petWindow = harness.WaitForPetWindow(TimeSpan.FromSeconds(5));
            OpenStatistics(harness, petWindow);
            var statisticsWindow = harness.WaitForTopLevelWindow("UsageStatisticsWindow", TimeSpan.FromSeconds(5));

            Assert.Contains(
                "不可用",
                harness.WaitForAutomationName(
                    statisticsWindow,
                    "StatisticsErrorState",
                    name => name.Contains("不可用", StringComparison.Ordinal),
                    TimeSpan.FromSeconds(5)));
            harness.WaitForAutomationIdToDisappear(statisticsWindow, "StatisticsEmptyState", TimeSpan.FromSeconds(2));
            Assert.False(harness.WaitForPetWindow(TimeSpan.FromSeconds(1)).Current.IsOffscreen);
        }
        finally
        {
            Directory.Delete(directoryPath, recursive: true);
        }
    }

    // Purpose: Ensure the statistics window lifecycle is reusable and starts a fresh query after reopening.
    // Coverage: Open, close, mutate the isolated database, reopen from the pet, and count visible statistics windows.
    // Target effect: The reopened single window shows current data rather than the prior stale result.
    [Fact]
    [Trait("Category", "Ui")]
    public void Closing_and_reopening_statistics_window_reloads_current_data()
    {
        using var seed = new UsageStatisticsSeed();
        var start = seed.YesterdayStartUtc.AddHours(9);
        seed.AppendUsage("BrowserApp", "Initial", start, TimeSpan.FromHours(1));
        using var harness = StartHarness(seed.DatabasePath);
        var petWindow = harness.WaitForPetWindow(TimeSpan.FromSeconds(5));
        OpenStatistics(harness, petWindow);
        var firstWindow = harness.WaitForTopLevelWindow("UsageStatisticsWindow", TimeSpan.FromSeconds(5));
        harness.SelectComboBoxItem(firstWindow, "StatisticsDateSelector", seed.Yesterday.ToString("yyyy-MM-dd"));
        var firstItems = harness.WaitForListItemNames(firstWindow, "StatisticsApplicationList", 1, TimeSpan.FromSeconds(5));
        Assert.Contains("1 小时 0 分钟", firstItems[0]);

        harness.CloseWindow(firstWindow);
        harness.WaitForTopLevelWindowToDisappear("UsageStatisticsWindow", TimeSpan.FromSeconds(3));
        seed.AppendUsage("CodeApp", "Added", start.AddHours(2), TimeSpan.FromMinutes(30));
        OpenStatistics(harness, petWindow);
        var reopenedWindow = harness.WaitForTopLevelWindow("UsageStatisticsWindow", TimeSpan.FromSeconds(5));
        harness.SelectComboBoxItem(reopenedWindow, "StatisticsDateSelector", seed.Yesterday.ToString("yyyy-MM-dd"));

        var reopenedItems = harness.WaitForListItemNames(reopenedWindow, "StatisticsApplicationList", 2, TimeSpan.FromSeconds(5));
        Assert.Contains(reopenedItems, item => item.Contains("BrowserApp") && item.Contains("1 小时 0 分钟"));
        Assert.Contains(reopenedItems, item => item.Contains("CodeApp") && item.Contains("0 小时 30 分钟"));
        Assert.Equal(1, harness.CountTopLevelWindows("UsageStatisticsWindow"));
    }

    // Purpose: Prove application startup invokes retention after SQLite initialization.
    // Coverage: Expired and cutoff-crossing rows seeded using the live local-zone cutoff, real app startup, and post-launch database inspection.
    // Target effect: The expired row is deleted, the crossing row starts at the exact cutoff, and the pet remains running.
    [Fact]
    [Trait("Category", "Ui")]
    public void App_startup_prunes_expired_rows_and_clips_retained_boundary_rows()
    {
        using var seed = new UsageStatisticsSeed();
        var seeded = seed.SeedRetentionRows();
        using var harness = StartHarness(seed.DatabasePath);
        var petWindow = harness.WaitForPetWindow(TimeSpan.FromSeconds(5));

        var sessions = WaitForSessions(
            seed,
            rows => rows.All(row => row.Id != seeded.ExpiredId)
                && rows.Any(row => row.Id == seeded.CrossingId && row.StartedAt == seeded.CutoffUtc),
            TimeSpan.FromSeconds(5));

        // Initial foreground sampling may legitimately add unrelated current-day sessions.
        // Retention owns the seeded IDs, not the total number of live database rows.
        var retained = Assert.Single(sessions, session => session.Id == seeded.CrossingId);
        Assert.Equal(seeded.CrossingId, retained.Id);
        Assert.Equal(seeded.CutoffUtc, retained.StartedAt);
        Assert.DoesNotContain(sessions, session => session.Id == seeded.ExpiredId);
        Assert.False(petWindow.Current.IsOffscreen);
        Assert.True(harness.IsProcessRunning);
    }

    // Purpose: Keep all seeded date calculations on one local-date baseline captured at construction.
    // Coverage: Explicit local-date baseline, Yesterday, DayBeforeYesterday, and YesterdayStartUtc.
    // Target effect: Repeated seed properties remain anchored to the supplied construction date.
    [Fact]
    [Trait("Category", "Ui")]
    public void Usage_statistics_seed_uses_fixed_local_date_baseline()
    {
        using var seed = new UsageStatisticsSeed(new DateOnly(2000, 1, 15));

        Assert.Equal(new DateOnly(2000, 1, 14), seed.Yesterday);
        Assert.Equal(new DateOnly(2000, 1, 13), seed.DayBeforeYesterday);
        Assert.Equal(
            new DateOnly(2000, 1, 14),
            DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(seed.YesterdayStartUtc, TimeZoneInfo.Local).DateTime));
    }
    private static void OpenStatistics(DesktopPuppyUiHarness harness, System.Windows.Automation.AutomationElement petWindow)
    {
        harness.InvokeTrayMenu("Usage Statistics");
    }

    private static IReadOnlyList<ForegroundSession> WaitForSessions(
        UsageStatisticsSeed seed,
        Func<IReadOnlyList<ForegroundSession>, bool> predicate,
        TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        IReadOnlyList<ForegroundSession> sessions = [];
        while (DateTime.UtcNow < deadline)
        {
            sessions = seed.ReadSessions();
            if (predicate(sessions))
            {
                return sessions;
            }

            Thread.Sleep(50);
        }

        return sessions;
    }

    private static DesktopPuppyUiHarness StartHarness(string usageDatabasePath)
    {
        var executable = Environment.GetEnvironmentVariable("DESKTOP_PUPPY_EXE");
        Assert.False(
            string.IsNullOrWhiteSpace(executable),
            "Set DESKTOP_PUPPY_EXE to a built DesktopPuppy.exe to run Windows UI tests.");
        Assert.True(File.Exists(executable), $"DesktopPuppy.exe was not found at '{executable}'.");
        return DesktopPuppyUiHarness.Start(executable!, usageDatabasePath);
    }
}

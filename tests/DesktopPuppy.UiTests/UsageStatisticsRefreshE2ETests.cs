using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Automation;
using Xunit;

namespace DesktopPuppy.UiTests;

public sealed class UsageStatisticsRefreshE2ETests
{
    // Purpose: Prove an already-open real WPF statistics window refreshes through its
    //   product-owned 30-second DispatcherTimer, not a manual query, window activation,
    //   reopen, restart, or ViewModel test.
    // Coverage: Release app process; isolated SQLite; fixed retained prior local date;
    //   baseline closed session; a second-connection insert while the real window remains
    //   open; read-only UI Automation polling.
    // Target effect: Within 60 seconds after insertion the total changes from 30 to 45
    //   minutes and the new application row appears while the pet and statistics windows
    //   stay open.
    [Fact]
    [Trait("Category", "LongRunningE2E")]
    public void Statistics_window_refreshes_new_sqlite_data_on_its_real_timer()
    {
        using var seed = new UsageStatisticsSeed();
        seed.AppendUsage(
            "BrowserApp",
            "Documentation",
            LocalInstant(seed.Yesterday, new TimeOnly(9, 0)),
            TimeSpan.FromMinutes(30));

        using var harness = StartHarness(seed.DatabasePath);
        var petWindow = harness.WaitForPetWindow(TimeSpan.FromSeconds(5));
        OpenStatistics(harness, petWindow);
        var statisticsWindow = harness.WaitForTopLevelWindow("UsageStatisticsWindow", TimeSpan.FromSeconds(5));

        harness.SelectComboBoxItem(
            statisticsWindow,
            "StatisticsDateSelector",
            seed.Yesterday.ToString("yyyy-MM-dd"));

        Assert.Equal(
            "0 小时 30 分钟",
            harness.WaitForAutomationName(
                statisticsWindow,
                "StatisticsTotalDuration",
                name => name == "0 小时 30 分钟",
                TimeSpan.FromSeconds(10)));
        var baselineRows = harness.WaitForListItemNames(
            statisticsWindow,
            "StatisticsApplicationList",
            1,
            TimeSpan.FromSeconds(10));
        Assert.Contains("BrowserApp", baselineRows[0]);

        seed.AppendUsage(
            "RefreshProbeApp",
            "Probe",
            LocalInstant(seed.Yesterday, new TimeOnly(11, 0)),
            TimeSpan.FromMinutes(15));

        // Everything below is read-only: no focus, activation, reopen, or invocation is issued
        // after the second insertion, so only the product's own 30-second timer can refresh it.
        var startedUtc = DateTime.UtcNow;
        var deadlineUtc = startedUtc + TimeSpan.FromSeconds(60);
        var lastTotalName = string.Empty;
        IReadOnlyList<string> lastApplicationRows = [];
        var refreshed = false;
        while (DateTime.UtcNow < deadlineUtc)
        {
            try
            {
                lastTotalName = harness
                    .FindByAutomationId(statisticsWindow, "StatisticsTotalDuration")
                    .Current.Name;
                lastApplicationRows = harness.ReadListItemNames(
                    statisticsWindow,
                    "StatisticsApplicationList");
                if (lastTotalName == "0 小时 45 分钟"
                    && lastApplicationRows.Count == 2
                    && lastApplicationRows.Any(row => row.Contains("BrowserApp", StringComparison.Ordinal))
                    && lastApplicationRows.Any(row => row.Contains("RefreshProbeApp", StringComparison.Ordinal)))
                {
                    refreshed = true;
                    break;
                }
            }
            catch (InvalidOperationException)
            {
                // Retry until the list is populated.
            }
            catch (COMException)
            {
                // Retry while UI Automation refreshes the WPF tree.
            }
            catch (ElementNotAvailableException)
            {
                // Retry while WPF replaces the bound element.
            }

            Thread.Sleep(250);
        }

        if (!refreshed)
        {
            Assert.Fail(BuildTimeoutDiagnostics(
                harness,
                seed.DatabasePath,
                petWindow,
                statisticsWindow,
                lastTotalName,
                lastApplicationRows,
                DateTime.UtcNow - startedUtc));
        }

        Assert.Contains(lastApplicationRows, row => row.Contains("RefreshProbeApp", StringComparison.Ordinal));
        Assert.True(harness.IsProcessRunning, "The Desktop Puppy process exited while waiting for the timer refresh.");
        Assert.False(petWindow.Current.IsOffscreen, "The pet window became offscreen while waiting for the timer refresh.");
        Assert.False(
            statisticsWindow.Current.IsOffscreen,
            "The statistics window became offscreen while waiting for the timer refresh.");
        Assert.Equal(1, harness.CountTopLevelWindows("UsageStatisticsWindow"));
    }

    private static DateTimeOffset LocalInstant(DateOnly date, TimeOnly time)
    {
        var localUnspecified = DateTime.SpecifyKind(date.ToDateTime(time), DateTimeKind.Unspecified);
        return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(localUnspecified, TimeZoneInfo.Local), TimeSpan.Zero);
    }

    private static void OpenStatistics(DesktopPuppyUiHarness harness, AutomationElement petWindow)
    {
        harness.Invoke(harness.FindByAutomationId(petWindow, "StatisticsButton"));
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

    private static string BuildTimeoutDiagnostics(
        DesktopPuppyUiHarness harness,
        string databasePath,
        AutomationElement petWindow,
        AutomationElement statisticsWindow,
        string lastTotalName,
        IReadOnlyList<string> lastApplicationRows,
        TimeSpan elapsed)
    {
        static string Safe(Func<string> read)
        {
            try
            {
                return read();
            }
            catch (Exception exception)
            {
                return $"<unavailable: {exception.GetType().Name}>";
            }
        }

        return string.Join(
            Environment.NewLine,
            "The statistics window did not refresh the appended SQLite session within 60 seconds.",
            $"Last total name: '{lastTotalName}'.",
            $"Last application rows ({lastApplicationRows.Count}): {string.Join(" | ", lastApplicationRows)}.",
            $"Elapsed wait: {elapsed.TotalSeconds:F1}s.",
            $"Process running: {Safe(() => harness.IsProcessRunning.ToString())}.",
            $"Pet window visible: {Safe(() => (!petWindow.Current.IsOffscreen).ToString())}.",
            $"Statistics window visible: {Safe(() => (!statisticsWindow.Current.IsOffscreen).ToString())}.",
            $"Statistics window count: {Safe(() => harness.CountTopLevelWindows("UsageStatisticsWindow").ToString())}.",
            $"Database path: {databasePath}.");
    }
}

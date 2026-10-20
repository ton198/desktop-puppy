using System.Windows.Automation;
using DesktopPuppy.Reports;
using Xunit;

namespace DesktopPuppy.UiTests;
public sealed class MvpFeaturesUiTests
{
    private static string Executable => Environment.GetEnvironmentVariable("DESKTOP_PUPPY_EXE") ?? throw new InvalidOperationException("Set DESKTOP_PUPPY_EXE.");
    [Fact, Trait("Category","Ui")]
    public void New_database_starts_in_primary_work_area_center()
    {
        using var harness=DesktopPuppyUiHarness.Start(Executable);
        var pet=harness.WaitForPetWindow(TimeSpan.FromSeconds(8));
        using var displays=new DesktopPuppy.Native.Win32MonitorDisplayProvider();
        var work=displays.GetMonitors().First(m=>m.IsPrimary).WorkArea;
        var bounds=harness.GetWindowRect();
        Assert.InRange((bounds.Left+bounds.Right)/2d,(work.Left+work.Right)/2d-2,(work.Left+work.Right)/2d+2);
        Assert.InRange((bounds.Top+bounds.Bottom)/2d,(work.Top+work.Bottom)/2d-2,(work.Top+work.Bottom)/2d+2);
    }
    [Fact, Trait("Category","Ui")]
    public async Task Saved_position_is_restored_on_a_second_launch()
    {
        using var seed=new UsageStatisticsSeed();
        using var displays=new DesktopPuppy.Native.Win32MonitorDisplayProvider();
        var monitor=displays.GetMonitors().First(m=>m.IsPrimary);
        var placement=new DesktopPuppy.Settings.PetPlacementStore(seed.DatabasePath);
        await placement.SaveAsync(new(monitor.DeviceId,150,150,"None"));
        WindowRect firstBounds;
        using(var first=DesktopPuppyUiHarness.Start(Executable,seed.DatabasePath))
        {
            var pet=first.WaitForPetWindow(TimeSpan.FromSeconds(8));
            first.WaitForAutomationName(pet,"PetAnimation",n=>n=="idle",TimeSpan.FromSeconds(5));
            firstBounds=first.GetWindowRect();
            var scale=new DesktopPuppy.Native.Win32WindowApi().GetDpiForWindow(new IntPtr(pet.Current.NativeWindowHandle))/96d;
            Assert.InRange((firstBounds.Left-monitor.WorkArea.Left)/scale,148,152);
            Assert.InRange((firstBounds.Top-monitor.WorkArea.Top)/scale,148,152);
            first.CloseWindow(pet);Assert.True(first.WaitForExit(TimeSpan.FromSeconds(5)));
        }
        using var second=DesktopPuppyUiHarness.Start(Executable,seed.DatabasePath);
        var restored=second.WaitForPetWindow(TimeSpan.FromSeconds(8));
        second.WaitForAutomationName(restored,"PetAnimation",n=>n=="idle",TimeSpan.FromSeconds(5));
        var bounds=second.GetWindowRect();
        Assert.InRange(bounds.Left,firstBounds.Left-2,firstBounds.Left+2);
        Assert.InRange(bounds.Top,firstBounds.Top-2,firstBounds.Top+2);
    }
    [Fact, Trait("Category","Ui")]
    public void Unsupported_database_stays_unchanged_and_shows_failure_notice()
    {
        using var seed=new UsageStatisticsSeed();
        using (var connection=new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={seed.DatabasePath};Pooling=False"))
        {
            connection.Open();using var command=connection.CreateCommand();
            command.CommandText="CREATE TABLE report_schema_version(id INTEGER PRIMARY KEY,version INTEGER); INSERT INTO report_schema_version VALUES(1,99);";command.ExecuteNonQuery();
        }
        var before=System.IO.File.ReadAllBytes(seed.DatabasePath);
        using var harness=DesktopPuppyUiHarness.Start(Executable,seed.DatabasePath);
        var pet=harness.WaitForPetWindow(TimeSpan.FromSeconds(8));
        harness.WaitForAutomationName(harness.WaitForSpeechWindow(),"AiNotice",n=>n.Contains("初始化失败"),TimeSpan.FromSeconds(4));
        harness.CloseWindow(pet);Assert.True(harness.WaitForExit(TimeSpan.FromSeconds(5)));
        Assert.Equal(before,System.IO.File.ReadAllBytes(seed.DatabasePath));
    }
    [Fact, Trait("Category","Ui")]
    public async Task Disabled_statistics_does_not_record_a_startup_interval()
    {
        using var seed=new UsageStatisticsSeed();
        var settings=new DesktopPuppy.Settings.SettingsService(seed.DatabasePath);
        await settings.SaveAsync(new DesktopPuppy.Core.Settings.AppSettings { Statistics=new(){Enabled=false}, Activity=new(){Enabled=false} });
        using var harness=DesktopPuppyUiHarness.Start(Executable,seed.DatabasePath);
        var pet=harness.WaitForPetWindow(TimeSpan.FromSeconds(8));
        Thread.Sleep(2200);
        harness.CloseWindow(pet);
        Assert.True(harness.WaitForExit(TimeSpan.FromSeconds(5)));
        using var store=new DesktopPuppy.Usage.UsageSqliteStore(seed.DatabasePath);
        Assert.Empty(store.ReadSessions());Assert.Empty(store.ReadWindowTitleIntervals());
    }
    [Fact, Trait("Category","Ui")]
    public void Second_instance_activates_existing_pet_and_exits()
    {
        using var seed=new UsageStatisticsSeed();
        using var first=DesktopPuppyUiHarness.Start(Executable,seed.DatabasePath);
        var pet=first.WaitForPetWindow(TimeSpan.FromSeconds(8));
        first.InvokeTrayMenu("隐藏桌宠");
        using var second=DesktopPuppyUiHarness.Start(Executable,seed.DatabasePath);
        Assert.True(second.WaitForExit(TimeSpan.FromSeconds(5)));
        first.WaitForPetWindow(TimeSpan.FromSeconds(5));
        Assert.True(first.IsProcessRunning);
    }
    [Fact, Trait("Category","Ui")]
    public void Show_from_tray_finishes_startup_wait_immediately()
    {
        using var seed=new UsageStatisticsSeed();
        using var harness=DesktopPuppyUiHarness.Start(Executable,seed.DatabasePath,"--startup");
        Assert.False(harness.HasVisiblePet);
        var watch=System.Diagnostics.Stopwatch.StartNew();
        harness.InvokeTrayMenu("显示桌宠");
        harness.WaitForPetWindow(TimeSpan.FromSeconds(3));
        Assert.True(watch.Elapsed<TimeSpan.FromSeconds(9));
    }
    [Fact, Trait("Category","Ui")]
    public void Report_history_displays_saved_text_and_local_facts()
    {
        using var seed=new UsageStatisticsSeed();
        var date=DateOnly.FromDateTime(DateTime.Now).AddDays(-1);
        var store=new DailyReportStore(seed.DatabasePath);
        var plan=new ReportPlan(date,DateTimeOffset.UtcNow,"常用应用","战报","tease","{\"totalMinutes\":45}");
        store.Save(new(date,ReportState.Success,plan,"测试日报：今天的窗口又被小狗巡逻过了。","",DateTimeOffset.UtcNow));
        // At/after 18:20 the app also creates today's entry and history selects it first.
        // Seed that case explicitly so this test checks the requested historical entry at any hour.
        store.Save(new(date.AddDays(1),ReportState.StatisticsUnavailable,null,"","",DateTimeOffset.UtcNow));
        using var harness=DesktopPuppyUiHarness.Start(Executable,seed.DatabasePath);
        harness.WaitForPetWindow(TimeSpan.FromSeconds(8));harness.InvokeTrayMenu("日报历史");
        var window=harness.WaitForTopLevelWindow("DailyReportWindow",TimeSpan.FromSeconds(5));
        harness.WaitForAutomationName(window,"ReportStatus",n=>!string.IsNullOrWhiteSpace(n),TimeSpan.FromSeconds(5));
        harness.SelectListItemContaining(window,"ReportDateList",date.ToString("MM-dd"));
        harness.WaitForAutomationName(window,"ReportBody",n=>n.Contains("测试日报"),TimeSpan.FromSeconds(5));
        Assert.Equal(1,harness.CountTopLevelWindows("DailyReportWindow"));
    }
    [Fact, Trait("Category","Ui")]
    public void Hide_during_startup_delay_is_not_reversed_by_timeout()
    {
        using var seed=new UsageStatisticsSeed();
        using var harness=DesktopPuppyUiHarness.Start(Executable,seed.DatabasePath,"--startup");
        harness.InvokeTrayMenu("隐藏桌宠");
        Thread.Sleep(11000);
        Assert.False(harness.HasVisiblePet);
        harness.InvokeTrayMenu("显示桌宠");
        harness.WaitForPetWindow(TimeSpan.FromSeconds(3));
    }
    [Fact, Trait("Category","Ui")]
    public void Pausing_keeps_local_click_and_closable_bubble_available()
    {
        using var harness=DesktopPuppyUiHarness.Start(Executable);
        var pet=harness.WaitForPetWindow(TimeSpan.FromSeconds(8));
        harness.InvokeTrayMenu("暂停 / 恢复");
        harness.SingleClickCenter();
        var speech=harness.WaitForSpeechWindow();
        Assert.NotNull(harness.WaitForAutomationId(speech,"SpeechBubbleText",TimeSpan.FromSeconds(3)));
        harness.Invoke(harness.FindByAutomationId(speech,"CloseBubbleButton"));
        harness.InvokeTrayMenu("暂停 / 恢复");
        Assert.True(harness.IsProcessRunning);
    }
}

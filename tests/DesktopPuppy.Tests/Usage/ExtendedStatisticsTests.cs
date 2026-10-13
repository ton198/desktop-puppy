using DesktopPuppy.Usage;
using DesktopPuppy.Data;
using DesktopPuppy.Native;
using Microsoft.Data.Sqlite;
using Xunit;

namespace DesktopPuppy.Tests.Usage;

public sealed class ExtendedStatisticsTests : IDisposable
{
    [Fact]
    public async Task ReportViewsAreIncludedInActualAiDisplays()
    {
        var store=new DesktopPuppy.Ai.AiSqliteStore(Db);store.Initialize();
        var at=DateTimeOffset.Parse("2026-10-09T18:30:00Z");
        store.RecordRequest(Guid.NewGuid(),at,"NotDisplayed","",1,1,DesktopPuppy.Ai.AiSceneKind.DailyReport);
        // Report view IDs are stable per report date and independent of generation IDs.
        store.RecordDisplayed(Guid.NewGuid(),at.AddMinutes(10),new("report","synthetic report","tease"),"","",DesktopPuppy.Ai.AiSceneKind.DailyReport);
        var facts=await new UsageFactsService(Db,TimeZoneInfo.Utc).BuildAsync(new(2026,10,9),TimeOnly.MaxValue);
        Assert.Equal(1,facts.AiSuccessCount);Assert.Equal(1,facts.AiDisplayedCount);
    }
    [Fact]
    public async Task ActualDisplayBelongsToDisplayDateRatherThanRequestDate()
    {
        var store=new DesktopPuppy.Ai.AiSqliteStore(Db);store.Initialize();var id=Guid.NewGuid();
        store.RecordRequest(id,DateTimeOffset.Parse("2026-10-09T23:59:59Z"),"Displayed","",1,1);
        store.RecordDisplayed(id,DateTimeOffset.Parse("2026-10-10T00:00:02Z"),new("line","synthetic line","tease"),"editor","test");
        var service=new UsageFactsService(Db,TimeZoneInfo.Utc);
        Assert.Equal(0,(await service.BuildAsync(new(2026,10,9),TimeOnly.MaxValue)).AiDisplayedCount);
        Assert.Equal(1,(await service.BuildAsync(new(2026,10,10),TimeOnly.MaxValue)).AiDisplayedCount);
        Assert.Equal(0,(await service.BuildAsync(new(2026,10,10),new(0,0,1))).AiDisplayedCount);
    }
    [Fact]
    public async Task DisplayStatusWithoutActualDisplayHistoryDoesNotCreateDisplayFact()
    {
        var store=new DesktopPuppy.Ai.AiSqliteStore(Db);store.Initialize();
        store.RecordRequest(Guid.NewGuid(),DateTimeOffset.Parse("2026-10-09T09:00:00Z"),"Displayed","",1,1);
        var facts=await new UsageFactsService(Db,TimeZoneInfo.Utc).BuildAsync(new(2026,10,9),TimeOnly.MaxValue);
        Assert.Equal(1,facts.AiSuccessCount);Assert.Equal(0,facts.AiDisplayedCount);
    }
    [Fact]
    public async Task ExportWritesDailySummaryAndAppsAndOnlyIncludesTitlesWhenExplicitlySelected()
    {
        Session("editor", "2026-10-09T07:30:00Z", "2026-10-09T09:30:00Z");
        Session("game", "2026-10-09T09:30:00Z", "2026-10-09T10:00:00Z");
        using (var store=new UsageSqliteStore(Db)) store.UpsertWindowTitleInterval(new(Guid.NewGuid(),new(1,1,"editor","private-title"),DateTimeOffset.Parse("2026-10-09T07:30:00Z"),DateTimeOffset.Parse("2026-10-09T09:30:00Z")));
        var history=new HistoryMaintenanceService(Db,TimeZoneInfo.Utc);
        history.RecordInteraction("SingleClick",DateTimeOffset.Parse("2026-10-09T09:00:00Z"));
        var ai=new DesktopPuppy.Ai.AiSqliteStore(Db); ai.Initialize();
        var request=Guid.NewGuid();
        ai.RecordRequest(request,DateTimeOffset.Parse("2026-10-09T09:00:00Z"),"Displayed","",2,1);
        ai.RecordDisplayed(request,DateTimeOffset.Parse("2026-10-09T09:00:00Z"),new("a","private-ai-text","tease"),"editor","private-title");
        ai.RecordRequest(Guid.NewGuid(),DateTimeOffset.Parse("2026-10-09T09:01:00Z"),"Failed","Timeout",3,1);
        var categories=new Dictionary<string,string>{{"EDITOR","Work"},{"game","Entertainment"}};
        await history.ExportAsync(new(2026,10,9),new(2026,10,9),false,_directory,categories);
        Assert.Equal(2,Directory.GetFiles(_directory,"*.csv").Length);
        var summary=File.ReadAllLines(Directory.GetFiles(_directory,"summary-*.csv").Single());
        var headings=summary[0].Split(',').Select(heading=>heading.Trim('"')).ToArray(); var values=summary[1].Split(',');
        string Value(string name)=>values[Array.IndexOf(headings,name)].Trim('"');
        Assert.Equal("9000",Value("TotalSeconds"));
        Assert.Equal("7200",Value("LongestContinuousSeconds"));
        Assert.Equal("1",Value("ApplicationSwitchCount"));
        Assert.Equal("1800",Value("DeepNightSeconds"));
        Assert.Equal("7200",Value("WorkSeconds"));
        Assert.Equal("1800",Value("EntertainmentSeconds"));
        Assert.Equal("0",Value("UnclassifiedSeconds"));
        Assert.Equal("1",Value("AiSuccessCount"));
        Assert.Equal("1",Value("AiFailureCount"));
        Assert.Equal("3",Value("AiRetryCount"));
        Assert.Equal("1",Value("AiDisplayedCount"));
        Assert.Equal("1",Value("Interaction_SingleClick"));
        Assert.Equal("3600",Value("Hour08Seconds"));
        Assert.All(Directory.GetFiles(_directory,"*.csv"),file=>
        {
            Assert.Equal(new byte[]{239,187,191},File.ReadAllBytes(file).Take(3));
            Assert.DoesNotContain("private-title",File.ReadAllText(file));
            Assert.DoesNotContain("private-ai-text",File.ReadAllText(file));
        });
        await history.ExportAsync(new(2026,10,9),new(2026,10,9),true,_directory,categories);
        Assert.Equal(3,Directory.GetFiles(_directory,"*.csv").Length);
        Assert.Contains("private-title",File.ReadAllText(Directory.GetFiles(_directory,"titles-*.csv").Single()));
    }
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "puppy-facts-" + Guid.NewGuid());
    private string Db => Path.Combine(_directory, "facts.db");
    public ExtendedStatisticsTests() => Directory.CreateDirectory(_directory);
    private void Session(string process, string start, string end)
    {
        using var store = new UsageSqliteStore(Db);
        store.AppendSession(new ForegroundSession(Guid.NewGuid(), new ForegroundWindowSnapshot(1, 1, process, "title"), DateTimeOffset.Parse(start), DateTimeOffset.Parse(end)));
    }
    [Fact]
    public async Task FactsClipToCutoffAndSplitHoursAndClassifyCaseInsensitively()
    {
        Session("editor", "2026-10-09T07:30:00Z", "2026-10-09T09:30:00Z");
        Session("game", "2026-10-09T09:30:00Z", "2026-10-09T11:00:00Z");
        var facts = await new UsageFactsService(Db, TimeZoneInfo.Utc).BuildAsync(new(2026,10,9), new(10,0), new Dictionary<string,string>{{"EDITOR","Work"},{"game","Entertainment"}});
        Assert.Equal(TimeSpan.FromMinutes(150), facts.TotalDuration);
        Assert.Equal(TimeSpan.FromHours(2), facts.LongestContinuousDuration);
        Assert.Equal(1, facts.ApplicationSwitchCount);
        Assert.Equal(TimeSpan.FromMinutes(30), facts.DeepNightDuration);
        Assert.Equal(TimeSpan.FromHours(2), facts.CategoryDurations["Work"]);
        Assert.Equal(TimeSpan.FromHours(1), facts.HourlyDurations[8]);
    }
    [Fact]
    public async Task FallBackHourCountsBothOccurrencesInElapsedTime()
    {
        Session("editor", "2026-11-01T04:00:00Z", "2026-11-01T08:00:00Z");
        var facts = await new UsageFactsService(Db, TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time")).BuildAsync(new(2026,11,1), new(4,0));
        Assert.Equal(TimeSpan.FromHours(4), facts.TotalDuration);
        Assert.Equal(TimeSpan.FromHours(2), facts.HourlyDurations[1]);
    }
    [Fact]
    public async Task HistoryClearPreservesSettingsAndExportIsSafeBomCsv()
    {
        Session("=FORMULA", "2026-10-09T09:00:00Z", "2026-10-09T10:00:00Z");
        var maintenance = new HistoryMaintenanceService(Db, TimeZoneInfo.Utc);
        maintenance.Initialize();
        maintenance.RecordInteraction("Click", DateTimeOffset.Parse("2026-10-09T09:30:00Z"));
        await maintenance.ExportAsync(new(2026,10,9), new(2026,10,9), false, _directory);
        var file = Directory.GetFiles(_directory, "applications-*.csv").Single();
        Assert.Equal(new byte[]{239,187,191}, File.ReadAllBytes(file).Take(3));
        Assert.Contains("'=FORMULA", File.ReadAllText(file));
        using (var connection = new SqliteConnection($"Data Source={Db}"))
        {
            connection.Open(); using var command = connection.CreateCommand();
            command.CommandText = "CREATE TABLE app_settings(id INTEGER); INSERT INTO app_settings VALUES(1);"; command.ExecuteNonQuery();
        }
        await maintenance.ClearAsync();
        using var store = new UsageSqliteStore(Db);
        Assert.Empty(store.ReadSessions());
        using var db = new SqliteConnection($"Data Source={Db}"); db.Open(); using var check = db.CreateCommand();
        check.CommandText = "SELECT count(*) FROM app_settings"; Assert.Equal(1L, check.ExecuteScalar());
    }
    [Fact]
    public async Task GapsDoNotCountAsApplicationSwitchesAndRetentionKeepsFifteenLocalDates()
    {
        Session("editor", "2026-09-24T23:00:00Z", "2026-09-25T01:00:00Z");
        Session("editor", "2026-10-09T09:00:00Z", "2026-10-09T10:00:00Z");
        Session("game", "2026-10-09T11:00:00Z", "2026-10-09T12:00:00Z");
        var history = new HistoryMaintenanceService(Db, TimeZoneInfo.Utc);
        history.RecordInteraction("Click", DateTimeOffset.Parse("2026-09-24T23:59:00Z"));
        history.RecordInteraction("Click", DateTimeOffset.Parse("2026-09-25T00:00:00Z"));
        await history.PruneAsync(DateTimeOffset.Parse("2026-10-09T12:00:00Z"));
        using var store = new UsageSqliteStore(Db);
        Assert.Equal(DateTimeOffset.Parse("2026-09-25T00:00:00Z"), store.ReadSessions()[0].StartedAt);
        var facts = await new UsageFactsService(Db, TimeZoneInfo.Utc).BuildAsync(new(2026,10,9), TimeOnly.MaxValue);
        Assert.Equal(0, facts.ApplicationSwitchCount);
        var old = await new UsageFactsService(Db, TimeZoneInfo.Utc).BuildAsync(new(2026,9,25), TimeOnly.MaxValue);
        Assert.Equal(1, old.Interactions["Click"]);
    }
    [Fact]
    public async Task ExplicitBoundariesPreventSameInstantSessionMergeAndActiveSnapshotClipsAtCutoff()
    {
        Session("editor", "2026-10-09T09:00:00Z", "2026-10-09T10:00:00Z");
        Session("editor", "2026-10-09T10:00:00Z", "2026-10-09T11:00:00Z");
        new HistoryMaintenanceService(Db,TimeZoneInfo.Utc).RecordInteraction("Lock",DateTimeOffset.Parse("2026-10-09T10:00:00Z"));
        var active=new ForegroundSession(Guid.NewGuid(),new ForegroundWindowSnapshot(1,1,"game","title"),DateTimeOffset.Parse("2026-10-09T11:00:00Z"),null);
        var service=new UsageFactsService(Db,TimeZoneInfo.Utc,snapshot:()=>new UsageCollectorStatisticsSnapshot(DateTimeOffset.Parse("2026-10-09T12:00:00Z"),active));
        var facts=await service.BuildAsync(new(2026,10,9),new(11,30));
        Assert.Equal(TimeSpan.FromHours(1),facts.LongestContinuousDuration);
        Assert.Equal(TimeSpan.FromMinutes(150),facts.TotalDuration);
        Assert.Equal(1,facts.ApplicationSwitchCount);
        Assert.Empty(facts.Interactions);
    }
    [Fact]
    public async Task MidnightEventBelongsOnlyToNewLocalDate()
    {
        var history=new HistoryMaintenanceService(Db,TimeZoneInfo.Utc);
        history.RecordInteraction("Click",DateTimeOffset.Parse("2026-10-10T00:00:00Z"));
        var previous=await new UsageFactsService(Db,TimeZoneInfo.Utc).BuildAsync(new(2026,10,9),TimeOnly.MaxValue);
        Assert.Empty(previous.Interactions);
        var next=await new UsageFactsService(Db,TimeZoneInfo.Utc).BuildAsync(new(2026,10,10),TimeOnly.MaxValue);
        Assert.Equal(1,next.Interactions["Click"]);
    }
    [Fact]
    public async Task HistoricalDisabledIntervalsMakeReportsIncompleteEvenWhenEnabledNow()
    {
        Session("editor", "2026-10-09T09:00:00Z", "2026-10-09T10:00:00Z");
        var history=new HistoryMaintenanceService(Db,TimeZoneInfo.Utc);
        history.RecordInteraction("StatisticsDisabled",DateTimeOffset.Parse("2026-10-09T10:00:00Z"));
        history.RecordInteraction("StatisticsEnabled",DateTimeOffset.Parse("2026-10-09T11:00:00Z"));
        Assert.False(await history.IsStatisticsCompleteAsync(new(2026,10,9),new(18,20)));
        Session("editor", "2026-10-10T09:00:00Z", "2026-10-10T10:00:00Z");
        Assert.True(await history.IsStatisticsCompleteAsync(new(2026,10,10),new(18,20)));
    }
    [Fact]
    public async Task DisabledAfterCutoffDoesNotInvalidateAndMissingDaysAreUnavailable()
    {
        Session("editor", "2026-10-09T09:00:00Z", "2026-10-09T10:00:00Z");
        var history=new HistoryMaintenanceService(Db,TimeZoneInfo.Utc);
        history.RecordInteraction("StatisticsDisabled",DateTimeOffset.Parse("2026-10-09T18:21:00Z"));
        Assert.True(await history.IsStatisticsCompleteAsync(new(2026,10,9),new(18,20)));
        Assert.False(await history.IsStatisticsCompleteAsync(new(2026,10,8),new(18,20)));
        Session("editor", "2026-10-10T09:00:00Z", "2026-10-10T10:00:00Z");
        Assert.False(await history.IsStatisticsCompleteAsync(new(2026,10,10),new(18,20)));
    }
    [Fact]
    public async Task EnableAtMidnightClearsPreviousDayDisabledState()
    {
        Session("editor", "2026-10-10T09:00:00Z", "2026-10-10T10:00:00Z");
        var history=new HistoryMaintenanceService(Db,TimeZoneInfo.Utc);
        history.RecordInteraction("StatisticsDisabled",DateTimeOffset.Parse("2026-10-09T22:00:00Z"));
        history.RecordInteraction("StatisticsEnabled",DateTimeOffset.Parse("2026-10-10T00:00:00Z"));
        Assert.True(await history.IsStatisticsCompleteAsync(new(2026,10,10),new(18,20)));
    }
    [Fact]
    public async Task RetentionPreservesDisabledStateCrossingOldestRetainedDate()
    {
        Session("editor", "2026-10-09T09:00:00Z", "2026-10-09T10:00:00Z");
        var history=new HistoryMaintenanceService(Db,TimeZoneInfo.Utc);
        history.RecordInteraction("StatisticsDisabled",DateTimeOffset.Parse("2026-09-24T22:00:00Z"));
        history.RecordInteraction("StatisticsEnabled",DateTimeOffset.Parse("2026-10-09T11:00:00Z"));
        await history.PruneAsync(DateTimeOffset.Parse("2026-10-09T12:00:00Z"));
        Assert.False(await history.IsStatisticsCompleteAsync(new(2026,10,9),new(18,20)));
    }
    [Fact]
    public async Task KnownLiveUsageAllowsCompletenessWithoutClosedSessionButStillHonorsDisabledState()
    {
        var history=new HistoryMaintenanceService(Db,TimeZoneInfo.Utc);
        Assert.True(await history.IsStatisticsCompleteAsync(new(2026,10,9),new(18,20),hasKnownUsage:true));
        history.RecordInteraction("StatisticsDisabled",DateTimeOffset.Parse("2026-10-09T09:00:00Z"));
        Assert.False(await history.IsStatisticsCompleteAsync(new(2026,10,9),new(18,20),hasKnownUsage:true));
    }
    [Fact]
    public async Task ExportIncludesKnownActiveForegroundSession()
    {
        var active=new ForegroundSession(Guid.NewGuid(),new ForegroundWindowSnapshot(1,1,"editor","title"),DateTimeOffset.Parse("2026-10-09T09:00:00Z"),null);
        var history=new HistoryMaintenanceService(Db,TimeZoneInfo.Utc,()=>new UsageCollectorStatisticsSnapshot(DateTimeOffset.Parse("2026-10-09T10:00:00Z"),active));
        await history.ExportAsync(new(2026,10,9),new(2026,10,9),false,_directory);
        Assert.Contains("\"editor\",3600",File.ReadAllText(Directory.GetFiles(_directory,"applications-*.csv").Single()));
    }
    public void Dispose() { SqliteConnection.ClearAllPools(); Directory.Delete(_directory,true); }
}









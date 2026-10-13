using System.Globalization;
using Microsoft.Data.Sqlite;

namespace DesktopPuppy.Usage;

public sealed record UsageFacts(
    DateOnly LocalDate, DateTimeOffset CutoffUtc, TimeSpan TotalDuration,
    int ApplicationSwitchCount, TimeSpan LongestContinuousDuration, TimeSpan DeepNightDuration,
    IReadOnlyDictionary<int,TimeSpan> HourlyDurations, IReadOnlyDictionary<string,TimeSpan> CategoryDurations,
    IReadOnlyList<ApplicationStatistics> Applications, IReadOnlyDictionary<string,int> Interactions,
    int AiSuccessCount, int AiFailureCount, int AiRetryCount, int AiDisplayedCount)
{
    public IReadOnlyDictionary<string,double> CategoryShares => CategoryDurations.ToDictionary(p=>p.Key,p=>TotalDuration.Ticks==0 ? 0d : (double)p.Value.Ticks/TotalDuration.Ticks);
}

public sealed class UsageFactsService
{
    private readonly string _path;
    private readonly TimeZoneInfo _zone;
    private readonly Func<UsageCollectorStatisticsSnapshot>? _snapshot;
    public UsageFactsService(string dbPath, TimeZoneInfo zone, IUsageStatisticsQuery? liveQuery=null, Func<UsageCollectorStatisticsSnapshot>? snapshot=null)
    {
        _path=dbPath; _zone=zone; _snapshot=snapshot;
        // A daily aggregate cannot reconstruct cutoff/hourly facts. Supply snapshot for live intervals.
        _ = liveQuery;
    }
    public Task<UsageFacts> BuildAsync(DateOnly date, TimeOnly cutoff, IReadOnlyDictionary<string,string>? categories=null, CancellationToken ct=default)
    {
        var snapshot=_snapshot?.Invoke();
        return Task.Run(()=>Build(date,cutoff,categories,snapshot,ct),ct);
    }
    private UsageFacts Build(DateOnly date, TimeOnly cutoff, IReadOnlyDictionary<string,string>? categories,UsageCollectorStatisticsSnapshot? snapshot,CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var store=new UsageSqliteStore(_path);
        var start=LocalDayBoundaryPolicy.StartOfDateUtc(date,_zone);
        var end=cutoff==TimeOnly.MaxValue ? LocalDayBoundaryPolicy.StartOfDateUtc(date.AddDays(1),_zone) : Resolve(date.ToDateTime(cutoff));
        if(snapshot is not null && end>snapshot.AsOfUtc) end=snapshot.AsOfUtc;
        var sessions=store.ReadSessions().ToList();
        if(snapshot?.ActiveSession is { } active && !sessions.Any(s=>s.Id==active.Id)) sessions.Add(active with{EndedAt=snapshot.AsOfUtc});
        var spans=sessions.Select(s=>new Span(s.ProcessName, s.StartedAt>start?s.StartedAt:start, s.EndedAt is { } e && e<end ? e:end)).Where(s=>s.End>s.Start).OrderBy(s=>s.Start).ToArray();
        var hour=Enumerable.Range(0,24).ToDictionary(h=>h,h=>TimeSpan.Zero);
        var category=new Dictionary<string,TimeSpan>{{"Work",TimeSpan.Zero},{"Entertainment",TimeSpan.Zero},{"Unclassified",TimeSpan.Zero}};
        var classification=categories?.ToDictionary(p=>p.Key,p=>p.Value,StringComparer.OrdinalIgnoreCase) ?? new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        var totals=new Dictionary<string,TimeSpan>(StringComparer.OrdinalIgnoreCase);
        var interactions=new Dictionary<string,int>(StringComparer.OrdinalIgnoreCase);
        var boundaries=new HashSet<DateTimeOffset>();
        using var db=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=_path,Pooling=false}.ToString());db.Open();
        if(Exists(db,"interaction_events"))
        {
            using var q=db.CreateCommand();q.CommandText="SELECT kind,at_utc FROM interaction_events"; using var r=q.ExecuteReader();
            while(r.Read())
            {
                var at=DateTimeOffset.Parse(r.GetString(1),CultureInfo.InvariantCulture); var kind=r.GetString(0);
                if(at>=start&&at<end)
                {
                    if(kind is "Lock" or "Sleep" or "Suspend" or "Hibernate" or "StatisticsDisabled" or "Shutdown") boundaries.Add(at);
                    else if(kind != "StatisticsEnabled") interactions[kind]=interactions.GetValueOrDefault(kind)+1;
                }
            }
        }
        TimeSpan longest=TimeSpan.Zero,night=TimeSpan.Zero,continuous=TimeSpan.Zero;
        int switches=0; Span? previous=null;
        foreach(var span in spans)
        {
            ct.ThrowIfCancellationRequested(); var duration=span.End-span.Start;
            totals[span.Process]=totals.GetValueOrDefault(span.Process)+duration;
            var label=classification.GetValueOrDefault(span.Process,"Unclassified"); if(!category.ContainsKey(label))label="Unclassified"; category[label]+=duration;
            bool contiguous=previous is not null && previous.End==span.Start && !boundaries.Contains(span.Start);
            if(contiguous && !string.Equals(previous!.Process,span.Process,StringComparison.OrdinalIgnoreCase)) switches++;
            continuous=contiguous && string.Equals(previous!.Process,span.Process,StringComparison.OrdinalIgnoreCase) ? continuous+duration:duration;
            if(continuous>longest)longest=continuous;
            for(var at=span.Start;at<span.End;)
            {
                var next=new DateTimeOffset(at.UtcDateTime.AddTicks(TimeSpan.TicksPerMinute-at.UtcDateTime.Ticks%TimeSpan.TicksPerMinute),TimeSpan.Zero);
                if(next>span.End)next=span.End;
                var local=TimeZoneInfo.ConvertTime(at,_zone); var part=next-at;hour[local.Hour]+=part;
                if(local.Hour>=23||local.Hour<8)night+=part; at=next;
            }
            previous=span;
        }
        var titleTotals=new Dictionary<(string Process,string Title),TimeSpan>();
        foreach(var title in store.ReadWindowTitleIntervals())
        {
            var a=title.StartedAt>start?title.StartedAt:start; var b=title.EndedAt is {} e&&e<end?e:end;
            if(b<=a)continue;var key=(title.ProcessName,title.WindowTitle);titleTotals[key]=titleTotals.GetValueOrDefault(key)+(b-a);
        }
        var apps=totals.OrderByDescending(p=>p.Value).ThenBy(p=>p.Key,StringComparer.OrdinalIgnoreCase).Select(p=>new ApplicationStatistics(p.Key,p.Value,titleTotals.Where(t=>string.Equals(t.Key.Process,p.Key,StringComparison.OrdinalIgnoreCase)).GroupBy(t=>t.Key.Title,StringComparer.Ordinal).Select(g=>new WindowTitleStatistics(g.Key,g.Aggregate(TimeSpan.Zero,(sum,t)=>sum+t.Value))).OrderByDescending(t=>t.Duration).ToArray())).ToArray();
        int success=0,failed=0,retries=0,displayed=0;
        if(Exists(db,"ai_requests"))
        {
            using var q=db.CreateCommand();q.CommandText="SELECT at_utc,status,generation_attempts,selection_attempts FROM ai_requests";using var r=q.ExecuteReader();
            while(r.Read())
            {
                var at=DateTimeOffset.Parse(r.GetString(0),CultureInfo.InvariantCulture);if(at<start||at>=end)continue;
                var status=r.GetString(1);if(status is "Displayed" or "NotDisplayed" or "Succeeded")success++;if(status=="Failed")failed++;
                retries+=Math.Max(0,r.GetInt32(2)-1)+Math.Max(0,r.GetInt32(3)-1);
            }
        }
        // Display facts are dated by actual delivery, including independent report views.
        // Request status/creation time cannot establish when content was shown.
        if(Exists(db,"content_history"))
        {
            using var q=db.CreateCommand();q.CommandText="SELECT at_utc FROM content_history";using var r=q.ExecuteReader();
            while(r.Read())
            {
                var at=DateTimeOffset.Parse(r.GetString(0),CultureInfo.InvariantCulture);
                if(at>=start&&at<end)displayed++;
            }
        }
        return new(date,end,totals.Values.Aggregate(TimeSpan.Zero,(a,b)=>a+b),switches,longest,night,hour,category,apps,interactions,success,failed,retries,displayed);
    }
    private DateTimeOffset Resolve(DateTime local)
    {
        while(_zone.IsInvalidTime(local))local=local.AddMinutes(1);
        if(_zone.IsAmbiguousTime(local))return new DateTimeOffset(local,_zone.GetAmbiguousTimeOffsets(local).Max()).ToUniversalTime();
        return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local,_zone));
    }
    private static bool Exists(SqliteConnection c,string table){using var q=c.CreateCommand();q.CommandText="SELECT count(*) FROM sqlite_master WHERE type='table' AND name=$name";q.Parameters.AddWithValue("$name",table);return (long)q.ExecuteScalar()!>0;}
    private sealed record Span(string Process,DateTimeOffset Start,DateTimeOffset End);
}




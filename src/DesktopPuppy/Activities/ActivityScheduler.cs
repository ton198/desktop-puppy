using System.Globalization;
using System.IO;
using Microsoft.Data.Sqlite;
namespace DesktopPuppy.Activities;

public sealed record ActivityContext(DateTimeOffset AtUtc, TimeZoneInfo TimeZone,
    bool Enabled=true, bool Visible=true, bool Paused=false, bool Fullscreen=false, bool BubbleOccupied=false, bool SpeechInFlight=false,
    bool QuietEnabled=true, TimeOnly? QuietStart=null, TimeOnly? QuietEnd=null, string ProcessName="", IReadOnlyCollection<string>? Blacklist=null,
    IReadOnlyCollection<string>? EnabledActivities=null, string Category="unclassified", double ContinuousMinutes=0, string Frequency="normal", int GlobalCooldownMinutes=20);
public sealed record ActivityDecision(string Animation, string Reason);

public sealed class ActivityStateStore
{
    private readonly string path;
    public ActivityStateStore(string databasePath) {
        path=databasePath; SQLitePCL.Batteries_V2.Init(); Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var c=Open(); using var transaction=c.BeginTransaction(); using var q=c.CreateCommand();q.Transaction=transaction;
        q.CommandText="SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='activity_schema_version'";
        if(Convert.ToInt64(q.ExecuteScalar(),CultureInfo.InvariantCulture)>0)
        {
            q.CommandText="SELECT version FROM activity_schema_version WHERE id=1";
            var version=q.ExecuteScalar();
            if(version is not null && version is not DBNull && Convert.ToInt64(version,CultureInfo.InvariantCulture)>1)
                throw new InvalidOperationException("Activity database version is unsupported; the original database was preserved.");
        }
        q.CommandText="CREATE TABLE IF NOT EXISTS activity_state(key TEXT PRIMARY KEY,at_utc TEXT NOT NULL); CREATE TABLE IF NOT EXISTS greeting_state(local_date TEXT PRIMARY KEY,at_utc TEXT NOT NULL); CREATE TABLE IF NOT EXISTS activity_schema_version(id INTEGER PRIMARY KEY CHECK(id=1),version INTEGER NOT NULL); INSERT INTO activity_schema_version VALUES(1,1) ON CONFLICT(id) DO NOTHING;"; q.ExecuteNonQuery();transaction.Commit();
    }
    private SqliteConnection Open() { var c=new SqliteConnection(new SqliteConnectionStringBuilder {DataSource=path,Pooling=false,DefaultTimeout=5}.ToString()); c.Open(); return c; }
    public DateTimeOffset? Read(string key) { using var c=Open(); using var q=c.CreateCommand(); q.CommandText="SELECT at_utc FROM activity_state WHERE key=$key"; q.Parameters.AddWithValue("$key",key); return q.ExecuteScalar() is string value?DateTimeOffset.Parse(value,CultureInfo.InvariantCulture):null; }
    public void Mark(string key,DateTimeOffset at) { using var c=Open(); using var t=c.BeginTransaction(); foreach(var k in new[]{key,"global"}) { using var q=c.CreateCommand(); q.Transaction=t; q.CommandText="INSERT INTO activity_state VALUES($key,$at) ON CONFLICT(key) DO UPDATE SET at_utc=$at"; q.Parameters.AddWithValue("$key",k); q.Parameters.AddWithValue("$at",at.ToUniversalTime().ToString("O")); q.ExecuteNonQuery(); } t.Commit(); }
    public bool HasGreeting(DateOnly date) { using var c=Open(); using var q=c.CreateCommand(); q.CommandText="SELECT COUNT(*) FROM greeting_state WHERE local_date=$date"; q.Parameters.AddWithValue("$date",date.ToString("yyyy-MM-dd")); return (long)q.ExecuteScalar()!>0; }
    public void MarkGreeting(DateOnly date,DateTimeOffset at) { using var c=Open(); using var t=c.BeginTransaction(); using var q=c.CreateCommand(); q.Transaction=t; q.CommandText="INSERT OR IGNORE INTO greeting_state VALUES($date,$at); INSERT INTO activity_state VALUES('global',$at) ON CONFLICT(key) DO UPDATE SET at_utc=$at;"; q.Parameters.AddWithValue("$date",date.ToString("yyyy-MM-dd")); q.Parameters.AddWithValue("$at",at.ToUniversalTime().ToString("O")); q.ExecuteNonQuery(); t.Commit(); }
}
public sealed class GreetingService(string databasePath)
{
    private readonly ActivityStateStore store=new(databasePath);
    public bool CanTrigger(DateOnly date,bool eligible)=>eligible && !store.HasGreeting(date);
    public void MarkTriggered(DateOnly date,DateTimeOffset at)=>store.MarkGreeting(date,at);
}
public sealed class ActivityScheduler(string databasePath,Random? random=null)
{
    private readonly ActivityStateStore store=new(databasePath);
    private readonly Random randomSource=random??Random.Shared;
    public static bool IsEligible(ActivityContext c) {
        if(!c.Enabled||!c.Visible||c.Paused||c.Fullscreen||c.BubbleOccupied||c.SpeechInFlight) return false;
        if(c.Blacklist?.Any(p=>string.Equals(Path.GetFileNameWithoutExtension(p),Path.GetFileNameWithoutExtension(c.ProcessName),StringComparison.OrdinalIgnoreCase))==true) return false;
        var time=TimeOnly.FromDateTime(TimeZoneInfo.ConvertTime(c.AtUtc,c.TimeZone).DateTime); var start=c.QuietStart??new(23,0); var end=c.QuietEnd??new(8,0);
        return !c.QuietEnabled || (start==end ? true : start<end ? time<start||time>=end : time<start&&time>=end);
    }
    public ActivityDecision? Evaluate(ActivityContext context) {
        if(!CanTriggerGlobal(context)) return null;
        var cooldown=context.Frequency.ToLowerInvariant() switch {"low"=>90,"high"=>30,_=>60};
        var available=new[]{"special-glance","special-smile","special-tongue"}.Where(a=>context.EnabledActivities is null||context.EnabledActivities.Contains(a)).Where(a=>store.Read(a) is not {} at||context.AtUtc-at>=TimeSpan.FromMinutes(cooldown)).ToArray();
        if(available.Length==0) return null;
        var preferred=context.ContinuousMinutes>=60?"special-glance":context.Category=="entertainment"?"special-tongue":context.Category=="work"?"special-smile":null;
        var animation=preferred is not null&&available.Contains(preferred)?preferred:available[randomSource.Next(available.Length)];
        return new(animation,context.ContinuousMinutes>=60?"continuous-use":context.Category);
    }
    public void MarkTriggered(ActivityDecision decision,DateTimeOffset at)=>store.Mark(decision.Animation,at);
    public bool CanTriggerGlobal(ActivityContext context)=>IsEligible(context) && (store.Read("global") is not {} last || context.AtUtc-last>=TimeSpan.FromMinutes(context.GlobalCooldownMinutes));
}

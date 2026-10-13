using System.Globalization;
using System.IO;
using System.Text;
using DesktopPuppy.Usage;
using Microsoft.Data.Sqlite;

namespace DesktopPuppy.Data;

/// <summary>Callers must stop and drain producers before clearing history.</summary>
public sealed class HistoryMaintenanceService(string databasePath, TimeZoneInfo timeZone, Func<UsageCollectorStatisticsSnapshot>? snapshot = null)
{
    private readonly object _gate = new();
    private static readonly string[] OperationalTables = ["content_history", "content_candidates", "ai_requests", "interaction_events", "usage_sessions", "window_title_intervals", "activity_state", "greeting_state", "daily_reports"];
    public void Initialize()
    {
        lock (_gate)
        {
            SQLitePCL.Batteries_V2.Init();
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(databasePath))!);
            using var c = Open(); using var q = c.CreateCommand();
            q.CommandText = "CREATE TABLE IF NOT EXISTS interaction_events(id TEXT PRIMARY KEY,kind TEXT NOT NULL,scene_kind TEXT NOT NULL,at_utc TEXT NOT NULL); CREATE INDEX IF NOT EXISTS ix_interaction_events_at ON interaction_events(at_utc);";
            q.ExecuteNonQuery();
        }
    }
    public void RecordInteraction(string kind, DateTimeOffset at, string scene = "")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        lock (_gate)
        {
            Initialize(); using var c = Open(); using var q = c.CreateCommand();
            q.CommandText = "INSERT INTO interaction_events VALUES($id,$kind,$scene,$at)";
            q.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("D")); q.Parameters.AddWithValue("$kind", kind); q.Parameters.AddWithValue("$scene", scene); q.Parameters.AddWithValue("$at", Timestamp(at)); q.ExecuteNonQuery();
        }
    }
    /// <summary>A date needs observed usage and no disabled portion before its fixed cutoff.</summary>
    public Task<bool> IsStatisticsCompleteAsync(DateOnly date, TimeOnly cutoff, bool hasKnownUsage = false) => Task.Run(() =>
    {
        lock (_gate)
        {
            Initialize();
            var start = LocalDayBoundaryPolicy.StartOfDateUtc(date, timeZone);
            DateTimeOffset end;
            if (cutoff == TimeOnly.MaxValue) end = LocalDayBoundaryPolicy.StartOfDateUtc(date.AddDays(1), timeZone);
            else
            {
                var local = date.ToDateTime(cutoff, DateTimeKind.Unspecified);
                while (timeZone.IsInvalidTime(local)) local = local.AddMinutes(1);
                end = timeZone.IsAmbiguousTime(local)
                    ? new DateTimeOffset(local, timeZone.GetAmbiguousTimeOffsets(local).Max()).ToUniversalTime()
                    : new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local, timeZone));
            }
            using var store = new UsageSqliteStore(databasePath);
            if (!hasKnownUsage && !store.ReadSessions().Any(session => session.EndedAt is { } ended && ended > start && session.StartedAt < end && ended > session.StartedAt)) return false;
            using var connection = Open();
            using var query = connection.CreateCommand();
            query.CommandText = "SELECT kind,at_utc FROM interaction_events WHERE kind IN ('StatisticsDisabled','StatisticsEnabled') ORDER BY at_utc,rowid";
            using var reader = query.ExecuteReader();
            bool disabledAtStart = false;
            while (reader.Read())
            {
                var kind = reader.GetString(0);
                var at = DateTimeOffset.Parse(reader.GetString(1), CultureInfo.InvariantCulture);
                if (at >= end) break;
                if (at < start) disabledAtStart = kind == "StatisticsDisabled";
                else if (kind == "StatisticsDisabled") return false;
                else if (at == start) disabledAtStart = false;
            }
            return !disabledAtStart;
        }
    });
    public Task ClearAsync() => Task.Run(() =>
    {
        lock (_gate)
        {
            Initialize(); using var c = Open(); using var t = c.BeginTransaction();
            foreach (var table in OperationalTables.Where(table => Exists(c,t,table))) Execute(c,t,$"DELETE FROM {table}");
            t.Commit();
        }
    });
    public Task PruneAsync(DateTimeOffset now) => Task.Run(() =>
    {
        lock (_gate)
        {
            Initialize(); var localDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now,timeZone).DateTime).AddDays(-14);
            var cutoff = LocalDayBoundaryPolicy.StartOfDateUtc(localDate,timeZone);
            using var c = Open(); using var t = c.BeginTransaction();
            // Carry effective state forward while removing history older than the retained dates.
            Execute(c,t,"""
                UPDATE interaction_events SET at_utc=$value
                WHERE id=(SELECT id FROM interaction_events
                    WHERE kind IN ('StatisticsDisabled','StatisticsEnabled') AND at_utc<$value
                    ORDER BY at_utc DESC,rowid DESC LIMIT 1)
                  AND kind='StatisticsDisabled'
                  AND NOT EXISTS (SELECT 1 FROM interaction_events
                    WHERE kind IN ('StatisticsDisabled','StatisticsEnabled') AND at_utc=$value);
                """,Timestamp(cutoff));
            foreach (var table in OperationalTables.Where(table => Exists(c,t,table)))
            {
                if (table is "usage_sessions" or "window_title_intervals")
                {
                    Execute(c,t,$"DELETE FROM {table} WHERE ended_at_utc IS NOT NULL AND ended_at_utc <= $value", Timestamp(cutoff));
                    Execute(c,t,$"UPDATE {table} SET started_at_utc=$value WHERE started_at_utc < $value AND (ended_at_utc IS NULL OR ended_at_utc > $value)", Timestamp(cutoff));
                }
                else if (table is "greeting_state" or "daily_reports") Execute(c,t,$"DELETE FROM {table} WHERE local_date < $value",localDate.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture));
                else Execute(c,t,$"DELETE FROM {table} WHERE at_utc < $value",Timestamp(cutoff));
            }
            t.Commit();
        }
    });
    public async Task ExportAsync(DateOnly start, DateOnly end, bool titles, string directory, IReadOnlyDictionary<string,string>? categories = null)
    {
        if (end < start || end.DayNumber-start.DayNumber > 366) throw new ArgumentOutOfRangeException(nameof(end));
        Directory.CreateDirectory(directory);
        var applicationsCsv = new StringBuilder("Date,Application,Seconds\r\n");
        var titlesCsv = new StringBuilder("Date,Application,WindowTitle,Seconds\r\n");
        var factsService = new UsageFactsService(databasePath,timeZone,snapshot:snapshot);
        var days = new List<UsageFacts>();
        for (var date=start; date<=end; date=date.AddDays(1))
        {
            var facts=await factsService.BuildAsync(date,TimeOnly.MaxValue,categories);
            days.Add(facts);
            foreach(var app in facts.Applications)
            {
                applicationsCsv.Append(Csv(date.ToString("yyyy-MM-dd"))).Append(',').Append(Csv(app.ProcessName)).Append(',').Append(Seconds(app.Duration)).Append("\r\n");
                if(titles) foreach(var title in app.WindowTitles) titlesCsv.Append(Csv(date.ToString("yyyy-MM-dd"))).Append(',').Append(Csv(app.ProcessName)).Append(',').Append(Csv(title.WindowTitle)).Append(',').Append(Seconds(title.Duration)).Append("\r\n");
            }
        }
        var interactionKinds = new[] { "SingleClick", "Hit", "RightClick", "Activity", "Greeting" }
            .Concat(days.SelectMany(day => day.Interactions.Keys)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(kind => kind, StringComparer.OrdinalIgnoreCase).ToArray();
        var headings = new[] { "Date", "TotalSeconds", "LongestContinuousSeconds", "ApplicationSwitchCount", "DeepNightSeconds", "WorkSeconds", "EntertainmentSeconds", "UnclassifiedSeconds", "AiSuccessCount", "AiFailureCount", "AiRetryCount", "AiDisplayedCount" }
            .Concat(interactionKinds.Select(kind => "Interaction_" + kind)).Concat(Enumerable.Range(0,24).Select(hour => $"Hour{hour:00}Seconds"));
        var summaryCsv = new StringBuilder(string.Join(',', headings.Select(Csv))).Append("\r\n");
        foreach (var day in days)
        {
            var values = new List<string> { Csv(day.LocalDate.ToString("yyyy-MM-dd")), Seconds(day.TotalDuration), Seconds(day.LongestContinuousDuration), Count(day.ApplicationSwitchCount), Seconds(day.DeepNightDuration), Seconds(day.CategoryDurations.GetValueOrDefault("Work")), Seconds(day.CategoryDurations.GetValueOrDefault("Entertainment")), Seconds(day.CategoryDurations.GetValueOrDefault("Unclassified")), Count(day.AiSuccessCount), Count(day.AiFailureCount), Count(day.AiRetryCount), Count(day.AiDisplayedCount) };
            values.AddRange(interactionKinds.Select(kind => Count(day.Interactions.GetValueOrDefault(kind))));
            values.AddRange(Enumerable.Range(0,24).Select(hour => Seconds(day.HourlyDurations.GetValueOrDefault(hour))));
            summaryCsv.AppendJoin(',',values).Append("\r\n");
        }
        var suffix = $"{start:yyyy-MM-dd}-{end:yyyy-MM-dd}.csv";
        await File.WriteAllTextAsync(Path.Combine(directory,"applications-"+suffix),applicationsCsv.ToString(),new UTF8Encoding(true));
        await File.WriteAllTextAsync(Path.Combine(directory,"summary-"+suffix),summaryCsv.ToString(),new UTF8Encoding(true));
        if(titles) await File.WriteAllTextAsync(Path.Combine(directory,"titles-"+suffix),titlesCsv.ToString(),new UTF8Encoding(true));
    }
    private static string Seconds(TimeSpan duration) => duration.TotalSeconds.ToString("0.#######",CultureInfo.InvariantCulture);
    private static string Count(int count) => count.ToString(CultureInfo.InvariantCulture);
    public static string Csv(string text)
    {
        var trimmed=text.TrimStart();
        if(trimmed.Length>0 && "=+-@".Contains(trimmed[0]) || text.StartsWith('\t') || text.StartsWith('\r')) text="'"+text;
        return "\""+text.Replace("\"","\"\"")+"\"";
    }
    private SqliteConnection Open() { var c=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=databasePath,Pooling=false}.ToString()); c.Open(); return c; }
    private static bool Exists(SqliteConnection c,SqliteTransaction t,string table) { using var q=c.CreateCommand(); q.Transaction=t;q.CommandText="SELECT count(*) FROM sqlite_master WHERE type='table' AND name=$table";q.Parameters.AddWithValue("$table",table);return (long)q.ExecuteScalar()!>0; }
    private static void Execute(SqliteConnection c,SqliteTransaction t,string sql,string? value=null) { using var q=c.CreateCommand();q.Transaction=t;q.CommandText=sql;if(value!=null)q.Parameters.AddWithValue("$value",value);q.ExecuteNonQuery(); }
    internal static string Timestamp(DateTimeOffset at)=>at.ToUniversalTime().ToString("O",CultureInfo.InvariantCulture);
}




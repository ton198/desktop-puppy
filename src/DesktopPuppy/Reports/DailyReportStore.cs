using System.Globalization;
using System.IO;
using System.Text.Json;
using Microsoft.Data.Sqlite;
namespace DesktopPuppy.Reports;
public sealed class DailyReportStore
{
    private readonly string path;
    public DailyReportStore(string databasePath) {
        path=databasePath; SQLitePCL.Batteries_V2.Init(); Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var c=Open();using var transaction=c.BeginTransaction();using var q=c.CreateCommand();q.Transaction=transaction;
        q.CommandText="SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='report_schema_version'";
        if(Convert.ToInt64(q.ExecuteScalar(),CultureInfo.InvariantCulture)>0)
        {
            q.CommandText="SELECT version FROM report_schema_version WHERE id=1";
            var version=q.ExecuteScalar();
            if(version is not null && version is not DBNull && Convert.ToInt64(version,CultureInfo.InvariantCulture)>1)
                throw new InvalidOperationException("Report database version is unsupported; the original database was preserved.");
        }
        q.CommandText="""
            CREATE TABLE IF NOT EXISTS daily_reports(local_date TEXT PRIMARY KEY,cutoff_utc TEXT NOT NULL,state TEXT NOT NULL,plan_json TEXT NOT NULL,text TEXT NOT NULL,error_kind TEXT NOT NULL,at_utc TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS report_schema_version(id INTEGER PRIMARY KEY CHECK(id=1),version INTEGER NOT NULL);
            INSERT INTO report_schema_version VALUES(1,1) ON CONFLICT(id) DO NOTHING;
            UPDATE daily_reports SET state='Failed',error_kind='Interrupted' WHERE state='Generating';
            """; q.ExecuteNonQuery();transaction.Commit();
    }
    private SqliteConnection Open() {var c=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=path,Pooling=false,DefaultTimeout=5}.ToString());c.Open();return c;}
    public void Save(DailyReport report) {using var c=Open();using var q=c.CreateCommand();q.CommandText="INSERT INTO daily_reports VALUES($date,$cutoff,$state,$plan,$text,$error,$at) ON CONFLICT(local_date) DO UPDATE SET cutoff_utc=$cutoff,state=$state,plan_json=$plan,text=$text,error_kind=$error,at_utc=$at";
        q.Parameters.AddWithValue("$date",report.Date.ToString("yyyy-MM-dd"));q.Parameters.AddWithValue("$cutoff",report.Plan?.CutoffUtc.ToString("O")??"");q.Parameters.AddWithValue("$state",report.State.ToString());q.Parameters.AddWithValue("$plan",JsonSerializer.Serialize(report.Plan));q.Parameters.AddWithValue("$text",report.Text);q.Parameters.AddWithValue("$error",report.ErrorKind);q.Parameters.AddWithValue("$at",report.AtUtc.ToUniversalTime().ToString("O"));q.ExecuteNonQuery();}
    public DailyReport? Read(DateOnly date)=>ReadAll().FirstOrDefault(r=>r.Date==date);
    public IReadOnlyList<DailyReport> ReadAll() {using var c=Open();using var q=c.CreateCommand();q.CommandText="SELECT local_date,state,plan_json,text,error_kind,at_utc FROM daily_reports ORDER BY local_date DESC";using var r=q.ExecuteReader();var reports=new List<DailyReport>();while(r.Read())reports.Add(new(DateOnly.ParseExact(r.GetString(0),"yyyy-MM-dd",CultureInfo.InvariantCulture),Enum.Parse<ReportState>(r.GetString(1)),JsonSerializer.Deserialize<ReportPlan>(r.GetString(2)),r.GetString(3),r.GetString(4),DateTimeOffset.Parse(r.GetString(5),CultureInfo.InvariantCulture)));return reports;}
}

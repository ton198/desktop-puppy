using System.Globalization;
using System.IO;
using Microsoft.Data.Sqlite;

namespace DesktopPuppy.Ai;

// Shares the product DB, but owns only AI tables. Every operation opens its own connection.
public sealed class AiSqliteStore(string databasePath)
{
    public void Initialize()
    {
        SQLitePCL.Batteries_V2.Init();
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(databasePath))!);
        using var connection = Open(); using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand(); command.Transaction = transaction;
        // Version admission must precede CREATE/ALTER or recovery writes.
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='ai_schema_version'";
        if (Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture) > 0)
        {
            command.CommandText = "SELECT version FROM ai_schema_version WHERE id=1";
            var version = command.ExecuteScalar();
            if (version is not null && version is not DBNull && Convert.ToInt64(version, CultureInfo.InvariantCulture) > 2)
                throw new InvalidOperationException("AI database version is unsupported; the original database was preserved.");
        }
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS ai_settings (id INTEGER PRIMARY KEY CHECK(id=1), enabled INTEGER NOT NULL, right_click_enabled INTEGER NOT NULL);
            CREATE TABLE IF NOT EXISTS api_key_blobs (provider TEXT PRIMARY KEY, ciphertext BLOB NOT NULL);
            CREATE TABLE IF NOT EXISTS ai_requests (id TEXT PRIMARY KEY, at_utc TEXT NOT NULL, status TEXT NOT NULL, error_kind TEXT NOT NULL, generation_attempts INTEGER NOT NULL, selection_attempts INTEGER NOT NULL);
            CREATE TABLE IF NOT EXISTS content_candidates (request_id TEXT NOT NULL, id TEXT NOT NULL, at_utc TEXT NOT NULL, text TEXT NOT NULL, style TEXT NOT NULL, PRIMARY KEY(request_id,id));
            CREATE TABLE IF NOT EXISTS content_history (request_id TEXT PRIMARY KEY, at_utc TEXT NOT NULL, candidate_id TEXT NOT NULL, text TEXT NOT NULL, style TEXT NOT NULL, application TEXT NOT NULL, title TEXT NOT NULL);
            CREATE INDEX IF NOT EXISTS ix_ai_requests_at ON ai_requests(at_utc);
            CREATE INDEX IF NOT EXISTS ix_ai_candidates_at ON content_candidates(at_utc);
            CREATE INDEX IF NOT EXISTS ix_ai_history_at ON content_history(at_utc);
            """;
        command.ExecuteNonQuery();
        foreach (var spec in new[] { ("ai_settings", "greeting_enabled", "INTEGER NOT NULL DEFAULT 1"), ("ai_settings", "activity_enabled", "INTEGER NOT NULL DEFAULT 1"), ("ai_settings", "daily_report_enabled", "INTEGER NOT NULL DEFAULT 1"), ("ai_requests", "scene_kind", "TEXT NOT NULL DEFAULT 'RightClick'"), ("content_history", "scene_kind", "TEXT NOT NULL DEFAULT 'RightClick'") })
        {
            using var inspect=connection.CreateCommand(); inspect.Transaction=transaction; inspect.CommandText=$"PRAGMA table_info({spec.Item1})";
            using var reader=inspect.ExecuteReader(); var found=false; while(reader.Read()) if(reader.GetString(1)==spec.Item2) found=true; reader.Close();
            if(!found) { using var alter=connection.CreateCommand(); alter.Transaction=transaction; alter.CommandText=$"ALTER TABLE {spec.Item1} ADD COLUMN {spec.Item2} {spec.Item3}"; alter.ExecuteNonQuery(); }
        }
        using var migration=connection.CreateCommand(); migration.Transaction=transaction;
        migration.CommandText="CREATE TABLE IF NOT EXISTS ai_schema_version(id INTEGER PRIMARY KEY CHECK(id=1),version INTEGER NOT NULL); INSERT INTO ai_schema_version VALUES(1,2) ON CONFLICT(id) DO UPDATE SET version=MAX(version,2);"; migration.ExecuteNonQuery();
        transaction.Commit();
    }
    private SqliteConnection Open() { var c = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = databasePath, DefaultTimeout = 5, Pooling = false }.ToString()); c.Open(); return c; }
    private static string Timestamp(DateTimeOffset at) => at.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
    private void Execute(string sql, params (string Name, object Value)[] parameters)
    {
        using var c = Open(); using var q = c.CreateCommand(); q.CommandText = sql;
        foreach (var p in parameters) q.Parameters.AddWithValue(p.Name, p.Value);
        q.ExecuteNonQuery();
    }
    public AiConfiguration LoadSettings()
    {
        using var c = Open(); using var q = c.CreateCommand(); q.CommandText = "SELECT enabled,right_click_enabled,greeting_enabled,activity_enabled,daily_report_enabled FROM ai_settings WHERE id=1";
        using var r = q.ExecuteReader(); return r.Read() ? new(r.GetBoolean(0), r.GetBoolean(1), r.GetBoolean(2), r.GetBoolean(3), r.GetBoolean(4)) : new();
    }
    public void SaveSettings(AiConfiguration settings) => Execute("INSERT INTO ai_settings(id,enabled,right_click_enabled,greeting_enabled,activity_enabled,daily_report_enabled) VALUES(1,$e,$r,$g,$a,$d) ON CONFLICT(id) DO UPDATE SET enabled=$e,right_click_enabled=$r,greeting_enabled=$g,activity_enabled=$a,daily_report_enabled=$d", ("$e", settings.Enabled), ("$r", settings.RightClickEnabled), ("$g",settings.GreetingEnabled), ("$a",settings.ActivityEnabled), ("$d",settings.DailyReportEnabled));
    public byte[]? ReadKey(AiProvider provider)
    {
        using var c = Open(); using var q = c.CreateCommand(); q.CommandText = "SELECT ciphertext FROM api_key_blobs WHERE provider=$p"; q.Parameters.AddWithValue("$p", provider.ToString());
        return q.ExecuteScalar() as byte[];
    }
    public void WriteKey(AiProvider provider, byte[] ciphertext) => Execute("INSERT INTO api_key_blobs VALUES($p,$v) ON CONFLICT(provider) DO UPDATE SET ciphertext=$v", ("$p", provider.ToString()), ("$v", ciphertext));
    public void DeleteKey(AiProvider provider) => Execute("DELETE FROM api_key_blobs WHERE provider=$p", ("$p", provider.ToString()));
    public void RecordRequest(Guid id, DateTimeOffset at, string status, string error, int generationAttempts, int selectionAttempts, AiSceneKind scene = AiSceneKind.RightClick) => Execute("INSERT INTO ai_requests(id,at_utc,status,error_kind,generation_attempts,selection_attempts,scene_kind) VALUES($id,$at,$s,$e,$g,$j,$scene) ON CONFLICT(id) DO UPDATE SET status=$s,error_kind=$e,generation_attempts=$g,selection_attempts=$j,scene_kind=$scene", ("$id", id.ToString()), ("$at", Timestamp(at)), ("$s", status), ("$e", error), ("$g", generationAttempts), ("$j", selectionAttempts), ("$scene",scene.ToString()));
    public void AddCandidates(Guid requestId, DateTimeOffset at, IReadOnlyList<ContentCandidate> candidates)
    {
        using var c = Open(); using var t = c.BeginTransaction();
        foreach (var candidate in candidates)
        {
            using var q = c.CreateCommand(); q.Transaction = t; q.CommandText = "INSERT OR IGNORE INTO content_candidates VALUES($r,$id,$at,$text,$style)";
            q.Parameters.AddWithValue("$r", requestId.ToString()); q.Parameters.AddWithValue("$id", candidate.Id); q.Parameters.AddWithValue("$at", Timestamp(at)); q.Parameters.AddWithValue("$text", candidate.Text); q.Parameters.AddWithValue("$style", candidate.Style); q.ExecuteNonQuery();
        }
        t.Commit();
    }
    public void RecordDisplayed(Guid requestId, DateTimeOffset at, ContentCandidate candidate, string application, string title, AiSceneKind scene = AiSceneKind.RightClick) => Execute("INSERT OR IGNORE INTO content_history(request_id,at_utc,candidate_id,text,style,application,title,scene_kind) VALUES($r,$at,$id,$text,$style,$app,$title,$scene)", ("$r", requestId.ToString()), ("$at", Timestamp(at)), ("$id", candidate.Id), ("$text", candidate.Text), ("$style", candidate.Style), ("$app", application), ("$title", title), ("$scene",scene.ToString()));
    public IReadOnlyList<DisplayedContent> ReadDisplayed()
    {
        using var c = Open(); using var q = c.CreateCommand(); q.CommandText = "SELECT request_id,at_utc,candidate_id,text,style,application,title FROM content_history ORDER BY at_utc DESC";
        using var r = q.ExecuteReader(); var rows = new List<DisplayedContent>();
        while (r.Read()) rows.Add(new(Guid.Parse(r.GetString(0)), DateTimeOffset.Parse(r.GetString(1), CultureInfo.InvariantCulture), new(r.GetString(2), r.GetString(3), r.GetString(4)), r.GetString(5), r.GetString(6)));
        return rows;
    }
    public void PruneBefore(DateTimeOffset cutoff)
    {
        using var c = Open(); using var t = c.BeginTransaction(); using var q = c.CreateCommand(); q.Transaction = t;
        q.CommandText = "DELETE FROM content_history WHERE at_utc<$at; DELETE FROM content_candidates WHERE at_utc<$at; DELETE FROM ai_requests WHERE at_utc<$at;";
        q.Parameters.AddWithValue("$at", Timestamp(cutoff)); q.ExecuteNonQuery(); t.Commit();
    }
    public long HistoryCount()
    {
        using var c = Open(); using var q = c.CreateCommand(); q.CommandText = "SELECT (SELECT COUNT(*) FROM ai_requests)+(SELECT COUNT(*) FROM content_candidates)+(SELECT COUNT(*) FROM content_history)"; return (long)q.ExecuteScalar()!;
    }
}

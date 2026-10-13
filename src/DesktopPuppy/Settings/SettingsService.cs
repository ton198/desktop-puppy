using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using DesktopPuppy.Core.Settings;
using DesktopPuppy.Ai;
using Microsoft.Data.Sqlite;
namespace DesktopPuppy.Settings;
public sealed class SettingsService(string databasePath)
{
    private readonly SemaphoreSlim gate=new(1,1);
    public AppSettings Current { get; private set; }=new();
    public string? LastError { get; private set; }
    public event EventHandler? Changed;
    private SqliteConnection Open()
    {
        SQLitePCL.Batteries_V2.Init(); Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(databasePath))!);
        var c=new SqliteConnection(new SqliteConnectionStringBuilder {DataSource=databasePath,Pooling=false,DefaultTimeout=5}.ToString()); c.Open();
        try
        {
            // Reject unsupported settings before the separately owned AI migration can write.
            using(var preflight=c.CreateCommand())
            {
                preflight.CommandText="SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='settings_schema'";
                if(Convert.ToInt64(preflight.ExecuteScalar(),System.Globalization.CultureInfo.InvariantCulture)>0)
                {
                    preflight.CommandText="SELECT version FROM settings_schema WHERE id=1";
                    var version=preflight.ExecuteScalar();
                    if(version is not null && version is not DBNull && Convert.ToInt64(version,System.Globalization.CultureInfo.InvariantCulture)!=1)
                        throw new InvalidOperationException("设置数据库版本不兼容，无法安全修改。");
                }
            }
            new AiSqliteStore(databasePath).Initialize();
        using var t=c.BeginTransaction(); using var q=c.CreateCommand(); q.Transaction=t;
        q.CommandText="CREATE TABLE IF NOT EXISTS settings_schema (id INTEGER PRIMARY KEY CHECK(id=1),version INTEGER NOT NULL); CREATE TABLE IF NOT EXISTS app_settings(id INTEGER PRIMARY KEY CHECK(id=1),json TEXT NOT NULL); INSERT OR IGNORE INTO settings_schema VALUES(1,1); INSERT OR IGNORE INTO app_settings VALUES(1,'{}');";
        q.ExecuteNonQuery(); q.CommandText="SELECT version FROM settings_schema WHERE id=1"; if(Convert.ToInt64(q.ExecuteScalar(),System.Globalization.CultureInfo.InvariantCulture)!=1){c.Dispose();throw new InvalidOperationException("设置数据库版本较新，无法安全修改。");} t.Commit(); return c;
        }
        catch { c.Dispose(); throw; }
    }
    public async Task LoadAsync()
    {
        await gate.WaitAsync();
        try
        {
            var loaded=await Task.Run(()=>
            {
                using var c=Open(); using var q=c.CreateCommand(); q.CommandText="SELECT json FROM app_settings WHERE id=1";
                AppSettings value; LastError=null;
                try {value=JsonSerializer.Deserialize<AppSettings>((string)q.ExecuteScalar()!)??new(); Validate(value); value=Freeze(value);}
                catch(JsonException){value=new();LastError="设置损坏，已使用默认值。";}
                catch(ArgumentException){value=new();LastError="设置超出有效范围，已使用默认值。";}
                var ai=new AiSqliteStore(databasePath).LoadSettings();
                return Freeze(value with {Ai=value.Ai with {Enabled=ai.Enabled,RightClickEnabled=ai.RightClickEnabled,GreetingEnabled=ai.GreetingEnabled,ActivityEnabled=ai.ActivityEnabled,ReportEnabled=ai.DailyReportEnabled}});
            });
            Current=loaded;
        }
        catch { LastError="无法加载设置；原数据库已保留。"; throw; }
        finally {gate.Release();}
        Changed?.Invoke(this,EventArgs.Empty);
    }
    public async Task SaveAsync(AppSettings settings)
    {
        Validate(settings); var snapshot=Freeze(settings); await gate.WaitAsync();
        try
        {
            await Task.Run(()=>
            {
                using var c=Open(); using var t=c.BeginTransaction(); using var q=c.CreateCommand(); q.Transaction=t;
                var node=JsonSerializer.SerializeToNode(snapshot)!.AsObject(); node.Remove("Ai");
                q.CommandText="UPDATE app_settings SET json=$json WHERE id=1; INSERT INTO ai_settings(id,enabled,right_click_enabled,greeting_enabled,activity_enabled,daily_report_enabled) VALUES(1,$e,$r,$g,$a,$d) ON CONFLICT(id) DO UPDATE SET enabled=$e,right_click_enabled=$r,greeting_enabled=$g,activity_enabled=$a,daily_report_enabled=$d;";
                q.Parameters.AddWithValue("$json",node.ToJsonString()); q.Parameters.AddWithValue("$e",snapshot.Ai.Enabled); q.Parameters.AddWithValue("$r",snapshot.Ai.RightClickEnabled); q.Parameters.AddWithValue("$g",snapshot.Ai.GreetingEnabled); q.Parameters.AddWithValue("$a",snapshot.Ai.ActivityEnabled); q.Parameters.AddWithValue("$d",snapshot.Ai.ReportEnabled);
                q.ExecuteNonQuery(); t.Commit();
            });
            Current=snapshot;LastError=null;
        }
        catch {LastError="保存失败，仍保留上一份有效设置。";throw;}
        finally {gate.Release();}
        Changed?.Invoke(this,EventArgs.Empty);
    }
    public Task ResetAsync()=>SaveAsync(new());
    private static AppSettings Freeze(AppSettings value)=>value with {Activity=value.Activity with {Blacklist=Array.AsReadOnly(value.Activity.Blacklist.Select(s=>s.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray())},Statistics=value.Statistics with {Categories=new System.Collections.ObjectModel.ReadOnlyDictionary<string,ApplicationCategory>(new Dictionary<string,ApplicationCategory>(value.Statistics.Categories,StringComparer.OrdinalIgnoreCase))}};
    public static void Validate(AppSettings value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if(value.Window is null||value.Activity is null||value.Statistics is null||value.Interaction is null||value.Ai is null)throw new ArgumentException("设置分组不能为空。");
        if(!double.IsFinite(value.Window.Scale)||value.Window.Scale<.5||value.Window.Scale>2)throw new ArgumentOutOfRangeException(nameof(value),"大小必须为50%–200%。");
        if(!double.IsFinite(value.Window.Opacity)||value.Window.Opacity<.3||value.Window.Opacity>1)throw new ArgumentOutOfRangeException(nameof(value),"透明度必须为30%–100%。");
        if(value.Activity.GlobalCooldown<TimeSpan.FromMinutes(1)||value.Activity.GlobalCooldown>TimeSpan.FromMinutes(180)||!Enum.IsDefined(value.Activity.Frequency))throw new ArgumentOutOfRangeException(nameof(value));
        if(value.Activity.Blacklist is null||value.Statistics.Categories is null||value.Activity.Blacklist.Any(string.IsNullOrWhiteSpace)||value.Statistics.Categories.Any(x=>string.IsNullOrWhiteSpace(x.Key)||!Enum.IsDefined(x.Value)))throw new ArgumentException("应用列表无效。");
    }
}



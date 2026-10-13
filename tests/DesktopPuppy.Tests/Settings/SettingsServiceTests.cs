using DesktopPuppy.Ai;
using DesktopPuppy.Core.Settings;
using DesktopPuppy.Settings;
using Microsoft.Data.Sqlite;
using Xunit;
namespace DesktopPuppy.Tests.Settings;
public sealed class SettingsServiceTests : IDisposable
{
    private readonly string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "puppy-settings-" + Guid.NewGuid() + ".db");
    [Fact] public async Task Migrates_legacy_AI_and_preserves_encrypted_keys()
    {
        var legacy = new AiSqliteStore(path); legacy.Initialize(); legacy.SaveSettings(new(false,false)); legacy.WriteKey(AiProvider.DeepSeek,[1,2,3]);
        var service = new SettingsService(path); await service.LoadAsync();
        Assert.False(service.Current.Ai.Enabled); Assert.False(service.Current.Ai.RightClickEnabled);
        await service.SaveAsync(service.Current with { Ai = service.Current.Ai with { Enabled=true }, Window=new() { Scale=1.5 }, StartupEnabled=true });
        Assert.True(legacy.LoadSettings().Enabled); Assert.Equal(new byte[]{1,2,3},legacy.ReadKey(AiProvider.DeepSeek));
        var reopened=new SettingsService(path); await reopened.LoadAsync(); Assert.Equal(1.5,reopened.Current.Window.Scale); Assert.True(reopened.Current.StartupEnabled);
    }
    [Fact] public async Task Invalid_save_does_not_publish_or_replace_current_settings()
    {
        var service=new SettingsService(path); await service.LoadAsync(); int changes=0; service.Changed+=(_,_)=>changes++;
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(()=>service.SaveAsync(service.Current with { Window=new() { Opacity=0.1 } }));
        Assert.Equal(1,service.Current.Window.Opacity); Assert.Equal(0,changes);
    }
    [Fact] public async Task Corrupt_json_uses_defaults_and_preserves_legacy_AI()
    {
        var service=new SettingsService(path); await service.LoadAsync();
        using(var c=new SqliteConnection($"Data Source={path}")){c.Open();using var q=c.CreateCommand();q.CommandText="UPDATE app_settings SET json='broken'; INSERT OR REPLACE INTO ai_settings(id,enabled,right_click_enabled) VALUES(1,0,0)";q.ExecuteNonQuery();}
        await service.LoadAsync(); Assert.NotNull(service.LastError); Assert.Equal(1,service.Current.Window.Scale); Assert.False(service.Current.Ai.Enabled);
    }
    [Fact] public async Task Reset_preserves_keys_and_clears_startup_and_classification()
    {
        var legacy=new AiSqliteStore(path); legacy.Initialize(); legacy.WriteKey(AiProvider.Jev,[9]);
        var service=new SettingsService(path); await service.SaveAsync(new(){StartupEnabled=true,Statistics=new(){Categories=new Dictionary<string,ApplicationCategory>{{"editor",ApplicationCategory.Work}}}});
        await service.ResetAsync(); Assert.False(service.Current.StartupEnabled); Assert.Empty(service.Current.Statistics.Categories); Assert.Equal(new byte[]{9},legacy.ReadKey(AiProvider.Jev));
    }
    [Fact] public async Task Placement_persists_and_rejects_invalid_coordinates()
    {
        var store=new PetPlacementStore(path); await store.SaveAsync(new("DISPLAY1",25,90,"Left"));
        Assert.Equal(new SavedPetPlacement("DISPLAY1",25,90,"Left"),await new PetPlacementStore(path).LoadAsync());
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(()=>store.SaveAsync(new("DISPLAY1",double.NaN,0,"None")));
    }
    [Fact] public async Task Future_schema_is_not_overwritten()
    {
        var service=new SettingsService(path);await service.LoadAsync();
        using(var c=new SqliteConnection($"Data Source={path}")){c.Open();using var q=c.CreateCommand();q.CommandText="UPDATE settings_schema SET version=99";q.ExecuteNonQuery();}
        await Assert.ThrowsAsync<InvalidOperationException>(()=>service.SaveAsync(new()));
        using var read=new SqliteConnection($"Data Source={path}");read.Open();using var query=read.CreateCommand();query.CommandText="SELECT version FROM settings_schema";Assert.Equal(99L,query.ExecuteScalar());
    }
    [Fact] public async Task Future_settings_schema_rejects_load_before_creating_or_migrating_AI_tables()
    {
        SQLitePCL.Batteries_V2.Init();
        using(var c=new SqliteConnection($"Data Source={path}")){c.Open();using var q=c.CreateCommand();q.CommandText="CREATE TABLE settings_schema(id INTEGER PRIMARY KEY,version INTEGER NOT NULL); INSERT INTO settings_schema VALUES(1,99); CREATE TABLE ai_settings(id INTEGER PRIMARY KEY,enabled INTEGER NOT NULL,right_click_enabled INTEGER NOT NULL); INSERT INTO ai_settings VALUES(1,0,0);";q.ExecuteNonQuery();}
        await Assert.ThrowsAsync<InvalidOperationException>(()=>new SettingsService(path).LoadAsync());
        using var read=new SqliteConnection($"Data Source={path}");read.Open();using var query=read.CreateCommand();
        query.CommandText="SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name IN ('app_settings','ai_schema_version','api_key_blobs','ai_requests')";Assert.Equal(0L,query.ExecuteScalar());
        query.CommandText="SELECT COUNT(*) FROM pragma_table_info('ai_settings')";Assert.Equal(3L,query.ExecuteScalar());
    }
    public void Dispose(){SqliteConnection.ClearAllPools();System.IO.File.Delete(path);}
}



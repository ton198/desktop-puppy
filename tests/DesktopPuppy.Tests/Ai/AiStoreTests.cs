using DesktopPuppy.Ai;
using DesktopPuppy.Security;
using DesktopPuppy.Usage;
using DesktopPuppy.Native;
using Microsoft.Data.Sqlite;
using Xunit;

namespace DesktopPuppy.Tests.Ai;

public sealed class AiStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "puppy-ai-" + Guid.NewGuid());
    private string Database => Path.Combine(_directory, "test.db");

    [Fact]
    public void Migration_is_idempotent_and_preserves_configuration()
    {
        using var usage = new UsageSqliteStore(Database);
        usage.Initialize();
        var at = DateTimeOffset.UtcNow;
        var session = new ForegroundSession(Guid.NewGuid(), new ForegroundWindowSnapshot(1, 2, "fixture", "fixture"), at.AddMinutes(-1), at);
        usage.AppendSession(session);
        var store = new AiSqliteStore(Database); store.Initialize();
        store.SaveSettings(new AiConfiguration(false, true)); store.Initialize();
        Assert.Equal(new AiConfiguration(false, true), store.LoadSettings());
        Assert.Equal(session.Id, Assert.Single(usage.ReadSessions()).Id);
    }

    [Fact]
    public void Keys_round_trip_are_ciphertext_and_delete_independently()
    {
        var store = new AiSqliteStore(Database); store.Initialize();
        var service = new ApiKeyService(store, new DpapiApiKeyProtector());
        var value = Guid.NewGuid().ToString("N");
        service.Save(AiProvider.DeepSeek, value); service.Save(AiProvider.Jev, value + "x");
        Assert.Equal(value, service.Get(AiProvider.DeepSeek));
        Assert.DoesNotContain(value, System.Text.Encoding.UTF8.GetString(store.ReadKey(AiProvider.DeepSeek)!));
        service.Delete(AiProvider.DeepSeek);
        Assert.Null(service.Get(AiProvider.DeepSeek)); Assert.True(service.IsConfigured(AiProvider.Jev));
    }

    [Fact]
    public void Corrupt_key_has_fixed_error()
    {
        var store = new AiSqliteStore(Database); store.Initialize(); store.WriteKey(AiProvider.Jev, [1, 2, 3]);
        var service = new ApiKeyService(store, new DpapiApiKeyProtector());
        Assert.Equal("API key is unavailable.", Assert.Throws<ApiKeyUnavailableException>(() => service.Get(AiProvider.Jev)).Message);
    }

    [Fact]
    public void Pruning_removes_history_but_preserves_settings_and_keys()
    {
        var store = new AiSqliteStore(Database); store.Initialize();
        store.SaveSettings(new AiConfiguration(true, false)); store.WriteKey(AiProvider.Jev, [4, 5, 6]);
        var now = DateTimeOffset.UtcNow; var old = now.AddDays(-20);
        store.RecordRequest(Guid.NewGuid(), old, "Failed", "Http", 1, 0);
        store.AddCandidates(Guid.NewGuid(), old, [new("c1", "这是一条测试用的调皮台词。", "tease")]);
        store.RecordDisplayed(Guid.NewGuid(), old, new("c2", "这是一条已经展示的测试台词。", "tease"), "editor", "test");
        store.PruneBefore(now.AddDays(-14));
        Assert.Empty(store.ReadDisplayed()); Assert.Equal(0, store.HistoryCount());
        Assert.Equal(new AiConfiguration(true, false), store.LoadSettings()); Assert.Equal(new byte[] { 4, 5, 6 }, store.ReadKey(AiProvider.Jev));
    }

    [Fact]
    public void Future_AI_schema_rejects_migration_without_mutating_existing_tables()
    {
        SQLitePCL.Batteries_V2.Init(); Directory.CreateDirectory(_directory);
        using(var c=new SqliteConnection($"Data Source={Database};Pooling=False"))
        {
            c.Open();using var q=c.CreateCommand();
            q.CommandText="CREATE TABLE ai_schema_version(id INTEGER PRIMARY KEY,version INTEGER NOT NULL); INSERT INTO ai_schema_version VALUES(1,99); CREATE TABLE ai_settings(id INTEGER PRIMARY KEY,enabled INTEGER NOT NULL,right_click_enabled INTEGER NOT NULL); INSERT INTO ai_settings VALUES(1,0,1);";q.ExecuteNonQuery();
        }
        Assert.Throws<InvalidOperationException>(()=>new AiSqliteStore(Database).Initialize());
        using var read=new SqliteConnection($"Data Source={Database};Pooling=False");read.Open();using var query=read.CreateCommand();
        query.CommandText="SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name IN ('api_key_blobs','ai_requests','content_candidates','content_history')";Assert.Equal(0L,query.ExecuteScalar());
        query.CommandText="SELECT COUNT(*) FROM pragma_table_info('ai_settings')";Assert.Equal(3L,query.ExecuteScalar());
        query.CommandText="SELECT version FROM ai_schema_version";Assert.Equal(99L,query.ExecuteScalar());
    }
    public void Dispose() { if (Directory.Exists(_directory)) Directory.Delete(_directory, true); }
}

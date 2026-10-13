using DesktopPuppy.Ai;
using Xunit;

namespace DesktopPuppy.Tests.Ai;
public sealed class AiSceneProfileTests
{
    [Fact] public void Context_bounds_untrusted_fact_shapes_and_keeps_comparison_evidence()
    {
        var excessive=Enumerable.Range(0,16).ToDictionary(i=>new string((char)('甲'+i),32),i=>new string('狗',64));
        var source=System.Text.Json.JsonSerializer.Serialize(new{totalMinutes=99,categoryMinutes=excessive,Interactions=excessive,recentComparison=new{date="2026-10-12",totalMinutes=80,differenceMinutes=19}});
        var wire=AiContextSerializer.Serialize(new("editor","title","day",[],FactsJson:source));
        Assert.True(wire.Length<=8000);
        using var context=System.Text.Json.JsonDocument.Parse(wire);
        using var facts=System.Text.Json.JsonDocument.Parse(context.RootElement.GetProperty("facts").GetString()!);
        Assert.Equal(99,facts.RootElement.GetProperty("totalMinutes").GetInt32());
        Assert.Equal(19,facts.RootElement.GetProperty("recentComparison").GetProperty("differenceMinutes").GetInt32());
    }
    [Fact] public void Context_compacts_large_facts_without_losing_totals_or_theme_evidence()
    {
        var source=System.Text.Json.JsonSerializer.Serialize(new{totalMinutes=321.5,ApplicationSwitchCount=70,deepNightMinutes=41,categoryMinutes=new {Work=121,Entertainment=200},applications=Enumerable.Range(0,100).Select(i=>new {process=new string('狗',900)+i,minutes=10}),optionalNotes=new string('狗',10000)});
        var scene=new AiScene("editor",new string('狗',9000),"day",[],AiSceneKind.DailyReport,source);
        var wire=AiContextSerializer.Serialize(scene);Assert.True(wire.Length<=8000);
        using var context=System.Text.Json.JsonDocument.Parse(wire);
        using var facts=System.Text.Json.JsonDocument.Parse(context.RootElement.GetProperty("facts").GetString()!);
        Assert.Equal(321.5,facts.RootElement.GetProperty("totalMinutes").GetDouble());
        Assert.Equal(70,facts.RootElement.GetProperty("ApplicationSwitchCount").GetInt32());
        Assert.Equal(41,facts.RootElement.GetProperty("deepNightMinutes").GetInt32());
        Assert.Equal(121,facts.RootElement.GetProperty("categoryMinutes").GetProperty("Work").GetInt32());
        Assert.NotEmpty(facts.RootElement.GetProperty("applications").EnumerateArray());
    }
    [Fact] public void Context_includes_bounded_recent_titles_and_ignores_invalid_facts()
    {
        var scene=new AiScene("editor","current","day",[],FactsJson:"invalid",RecentTitles:Enumerable.Range(0,100).Select(i=>new string('狗',900)+i).ToArray());
        using var context=System.Text.Json.JsonDocument.Parse(AiContextSerializer.Serialize(scene));
        var titles=context.RootElement.GetProperty("recentTitles");Assert.InRange(titles.GetArrayLength(),1,6);
        Assert.All(titles.EnumerateArray(),t=>Assert.True(t.GetString()!.Length<=160));
        using var facts=System.Text.Json.JsonDocument.Parse(context.RootElement.GetProperty("facts").GetString()!);Assert.Equal(System.Text.Json.JsonValueKind.Object,facts.RootElement.ValueKind);
    }
    [Fact] public async Task Report_rejects_candidates_outside_saved_plan_style()
    {
        var scene=new AiScene("","","day",[],AiSceneKind.DailyReport,RequiredStyle:"wordplay");
        var result=await new AiPipeline(new WrongStyle(),new NeverSelect(),new ImmediateDelay()).RunAsync(scene,_=>{},CancellationToken.None);
        Assert.Null(result.Candidate);Assert.Equal(5,result.GenerationAttempts);Assert.Equal(0,result.SelectionAttempts);
    }
    private sealed class WrongStyle:IAiContentProvider { public Task<IReadOnlyList<ContentCandidate>> GenerateAsync(AiScene s,IReadOnlyList<ContentCandidate> p,int v,CancellationToken ct)=>Task.FromResult<IReadOnlyList<ContentCandidate>>(Enumerable.Range(0,6).Select(i=>new ContentCandidate(i.ToString(),new string((char)('甲'+i),200),"tease")).ToArray()); }
    private sealed class NeverSelect:IJevDecisionProvider{public Task<JevDecision>SelectAsync(AiScene s,IReadOnlyList<ContentCandidate> p,CancellationToken ct)=>throw new InvalidOperationException("Selection must not accept wrong-style report candidates.");}
    private sealed class ImmediateDelay:IAiDelay{public Task WaitAsync(TimeSpan duration,CancellationToken ct)=>Task.CompletedTask;}
    [Fact] public void Greeting_rejects_long_short_dialogue()
    {
        var candidate = new ContentCandidate("a", new string('狗', 51), "tease");
        Assert.False(new ContentValidator().IsValid(candidate, [], [], AiSceneProfile.For(AiSceneKind.Greeting)));
        Assert.True(new ContentValidator().IsValid(candidate, [], []));
    }
    [Fact] public void Report_accepts_long_plain_text_and_rejects_short_dialogue()
    {
        var validator = new ContentValidator(); var profile = AiSceneProfile.For(AiSceneKind.DailyReport);
        Assert.True(validator.IsValid(new("a", new string('狗', 200), "tease"), [], [], profile));
        Assert.False(validator.IsValid(new("b", new string('狗', 199), "tease"), [], [], profile));
        Assert.Equal(TimeSpan.FromSeconds(30), profile.GenerationTimeout);
    }
    [Fact] public void Context_preserves_scene_and_remains_bounded_even_with_escape_characters()
    {
        var scene = new AiScene("editor", new string('"', 9000), "day", [], AiSceneKind.DailyReport, new string('"', 9000));
        var json = AiContextSerializer.Serialize(scene);
        Assert.True(json.Length <= 8000);
        using var parsed = System.Text.Json.JsonDocument.Parse(json);
        Assert.Equal("DailyReport", parsed.RootElement.GetProperty("scene").GetString());
    }
    [Fact] public void Legacy_ai_tables_migrate_without_losing_settings_and_preserve_new_flags()
    {
        var path=Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".db");
        try {
            SQLitePCL.Batteries_V2.Init();
            using(var c=new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={path};Pooling=False")){c.Open();using var q=c.CreateCommand();q.CommandText="CREATE TABLE ai_settings(id INTEGER PRIMARY KEY,enabled INTEGER,right_click_enabled INTEGER);INSERT INTO ai_settings VALUES(1,0,1);";q.ExecuteNonQuery();}
            var store=new AiSqliteStore(path);store.Initialize();Assert.False(store.LoadSettings().Enabled);Assert.True(store.LoadSettings().GreetingEnabled);
            store.SaveSettings(new(true,true,false,false,false));store.Initialize();Assert.False(store.LoadSettings().DailyReportEnabled);
            var id=Guid.NewGuid();store.RecordRequest(id,DateTimeOffset.UtcNow,"Success","",1,1,AiSceneKind.DailyReport);Assert.Equal(1,store.HistoryCount());
        } finally {File.Delete(path);}
    }
}

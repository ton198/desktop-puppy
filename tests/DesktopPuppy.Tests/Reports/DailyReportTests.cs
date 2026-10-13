using DesktopPuppy.Reports;
using DesktopPuppy.Ai;
using Xunit;
namespace DesktopPuppy.Tests.Reports;
public sealed class DailyReportTests
{
    [Fact] public void Style_avoids_latest_success_even_after_more_than_three_days()
    {
        var date=new DateOnly(2026,10,6);var priorDate=new DateOnly(2026,10,1);var at=DateTimeOffset.UtcNow;
        var priorPlan=new ReportPlan(priorDate,at,"常用应用","战报","tease","{}");
        var latestSuccess=new DailyReport(priorDate,ReportState.Success,priorPlan,new string('狗',200),"",at);
        var plan=new DailyReportPlanner().Create(date,at,new ReportFacts(31,true,"{}",["常用应用"]),[latestSuccess]);
        Assert.NotEqual("tease",plan.Style);
    }
    [Fact] public async Task Notification_failure_does_not_change_saved_success()
    {
        var path=Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".db");
        try {var store=new DailyReportStore(path);var date=new DateOnly(2026,10,9);
            var service=new DailyReportService(store,TimeZoneInfo.Utc,(d,c,ct)=>Task.FromResult(new ReportFacts(d==date?31:0,true,"{}",["常用应用"])),()=>true,(p,ct)=>Task.FromResult(new AiOutcome(new("c",new string('狗',200),p.Style),"",1,1)));
            service.Completed+=_=>throw new InvalidOperationException("Notification host is unavailable.");
            await service.CheckAsync(new(2026,10,9,18,20,0,TimeSpan.Zero),CancellationToken.None);
            Assert.Equal(ReportState.Success,store.Read(date)!.State);
        }finally{File.Delete(path);}
    }
    [Fact] public async Task Exact_thirty_minutes_skip_and_failed_retry_preserves_plan()
    {
        var path=Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".db");
        try {
            var store=new DailyReportStore(path); var attempts=0;
            var service=new DailyReportService(store,TimeZoneInfo.Utc, (date,cutoff,ct)=>Task.FromResult(new ReportFacts(date.Day==8?30:date.Day==9?31:0,true,"{\"minutes\":31}",["常用应用"])),()=>true,(plan,ct)=>{attempts++;return Task.FromResult(new AiOutcome(attempts==1?null:new("x",new string('狗',200),plan.Style),attempts==1?"Http":"",1,1));});
            await service.CheckAsync(new(2026,10,9,18,20,0,TimeSpan.Zero),CancellationToken.None);
            Assert.Equal(ReportState.InsufficientActivity,store.Read(new(2026,10,8))!.State);
            var failed=store.Read(new(2026,10,9))!; Assert.Equal(ReportState.Failed,failed.State);
            await service.CheckAsync(new(2026,10,9,20,0,0,TimeSpan.Zero),CancellationToken.None); Assert.Equal(1,attempts);
            await service.RetryAsync(failed.Date,CancellationToken.None);
            var result=store.Read(failed.Date)!; Assert.Equal(ReportState.Success,result.State); Assert.Equal(failed.Plan,result.Plan);
        } finally { File.Delete(path); }
    }
    [Fact] public void Interrupted_generation_recovers_failed_and_rotation_avoids_recent_formats()
    {
        var path=Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".db");
        try { var store=new DailyReportStore(path); var date=new DateOnly(2026,10,9); var facts=new ReportFacts(31,true,"{}",["常用应用"]); var planner=new DailyReportPlanner();
            var first=planner.Create(date,DateTimeOffset.UtcNow,facts,[]);
            store.Save(new(date,ReportState.Generating,first,"","",DateTimeOffset.UtcNow));
            Assert.Equal(ReportState.Failed,new DailyReportStore(path).Read(date)!.State);
            var second=planner.Create(date.AddDays(1),DateTimeOffset.UtcNow,facts,[store.Read(date)!]);
            Assert.NotEqual(first.Format,second.Format);
        } finally { File.Delete(path); }
    }
    [Fact] public async Task Waiting_configuration_keeps_cutoff_snapshot_and_generates_when_ready()
    {
        var path=Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".db");
        try {var store=new DailyReportStore(path);var enabled=false;var factsCalls=0;ReportPlan? sent=null;
            var date=new DateOnly(2026,10,9);
            var service=new DailyReportService(store,TimeZoneInfo.Utc,(d,c,ct)=>{factsCalls++;return Task.FromResult(new ReportFacts(d==date?31:0,true,"{\"minutes\":31}",["常用应用"]));},()=>enabled,(p,ct)=>{sent=p;return Task.FromResult(new AiOutcome(new("c",new string('狗',200),p.Style),"",1,1));});
            await service.CheckAsync(new(2026,10,9,18,19,0,TimeSpan.Zero),CancellationToken.None);Assert.Null(store.Read(date));
            await service.CheckAsync(new(2026,10,9,18,20,0,TimeSpan.Zero),CancellationToken.None);var waiting=store.Read(date)!;Assert.Equal(ReportState.WaitingForConfiguration,waiting.State);var calls=factsCalls;
            enabled=true;await service.CheckAsync(new(2026,10,9,22,0,0,TimeSpan.Zero),CancellationToken.None);
            Assert.Equal(calls,factsCalls);Assert.Equal(waiting.Plan,sent);Assert.Equal(new DateTimeOffset(2026,10,9,18,20,0,TimeSpan.Zero),sent!.CutoffUtc);
        }finally{File.Delete(path);}
    }
}


using DesktopPuppy.Ai;
namespace DesktopPuppy.Reports;
public sealed class DailyReportService(DailyReportStore store,TimeZoneInfo timeZone,
    Func<DateOnly,DateTimeOffset,CancellationToken,Task<ReportFacts>> facts,Func<bool> ready,
    Func<ReportPlan,CancellationToken,Task<AiOutcome>> generate)
{
    private readonly SemaphoreSlim gate=new(1,1);
    private readonly DailyReportPlanner planner=new();
    public event Action<DailyReport>? Completed;
    public static TimeOnly CutoffTime=>new(18,20);
    public async Task CheckAsync(DateTimeOffset now,CancellationToken ct) {
        if(!await gate.WaitAsync(0,ct).ConfigureAwait(false))return;
        try {
            var local=TimeZoneInfo.ConvertTime(now,timeZone);var today=DateOnly.FromDateTime(local.DateTime);
            for(var date=today.AddDays(-14);date<=today;date=date.AddDays(1)) {
                ct.ThrowIfCancellationRequested(); var cutoff=new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(date.ToDateTime(CutoffTime),timeZone));
                if(cutoff>now)continue;
                var report=store.Read(date);
                if(report is null) {
                    var data=await facts(date,cutoff,ct).ConfigureAwait(false);
                    var state=!data.StatisticsAvailable?ReportState.StatisticsUnavailable:data.TotalMinutes<=30?ReportState.InsufficientActivity:ReportState.Pending;
                    report=new(date,state,state==ReportState.Pending?planner.Create(date,cutoff,data,store.ReadAll()):null,"","",now);
                    store.Save(report);
                }
                if(report.State is ReportState.Pending or ReportState.WaitingForConfiguration) {
                    if(!ready()){store.Save(report with{State=ReportState.WaitingForConfiguration});continue;}
                    await GenerateAsync(report,ct).ConfigureAwait(false);
                }
            }
        } finally {gate.Release();}
    }
    public async Task RetryAsync(DateOnly date,CancellationToken ct) {
        await gate.WaitAsync(ct).ConfigureAwait(false);
        try {var report=store.Read(date);if(report?.Plan is null||report.State!=ReportState.Failed||!ready())return;await GenerateAsync(report,ct).ConfigureAwait(false);}finally{gate.Release();}
    }
    private async Task GenerateAsync(DailyReport report,CancellationToken ct) {
        store.Save(report with{State=ReportState.Generating});
        try {
            var outcome=await generate(report.Plan!,ct).ConfigureAwait(false);ct.ThrowIfCancellationRequested();
            var result=report with{State=outcome.Candidate is null?ReportState.Failed:ReportState.Success,Text=outcome.Candidate?.Text??"",ErrorKind=outcome.ErrorKind,AtUtc=DateTimeOffset.UtcNow};
            store.Save(result);Notify(result);
        } catch(OperationCanceledException){store.Save(report with{State=ReportState.Failed,ErrorKind="Interrupted"});throw;}
        catch {var result=report with{State=ReportState.Failed,ErrorKind="Generation"};store.Save(result);Notify(result);}
    }
    private void Notify(DailyReport report){try{Completed?.Invoke(report);}catch{/* A notification host cannot change persisted generation outcome. */}}
    public async Task WaitForIdleAsync(CancellationToken ct=default){await gate.WaitAsync(ct).ConfigureAwait(false);gate.Release();}
}

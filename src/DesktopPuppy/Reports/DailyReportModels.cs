using DesktopPuppy.Ai;
namespace DesktopPuppy.Reports;
public enum ReportState { InsufficientActivity, StatisticsUnavailable, WaitingForConfiguration, Pending, Generating, Success, Failed }
public sealed record ReportFacts(double TotalMinutes,bool StatisticsAvailable,string Json,IReadOnlyList<string> SupportedThemes);
public sealed record ReportPlan(DateOnly Date,DateTimeOffset CutoffUtc,string Theme,string Format,string Style,string FactsJson,int Version=1);
public sealed record DailyReport(DateOnly Date,ReportState State,ReportPlan? Plan,string Text,string ErrorKind,DateTimeOffset AtUtc);
public sealed class DailyReportPlanner
{
    public static readonly string[] Themes=["工作时间","娱乐时间","窗口切换","常用应用","深夜使用","最长连续使用","桌宠互动","工作/娱乐比例","与近期变化"];
    public static readonly string[] Formats=["战报","事故调查","颁奖典礼","法庭判决","体育解说","天气预报","侦探记录","绩效评估","新闻快讯","桌宠投诉信"];
    public ReportPlan Create(DateOnly date,DateTimeOffset cutoff,ReportFacts facts,IReadOnlyList<DailyReport> recent)
    {
        var prior=recent.Where(r=>r.Date<date&&r.Plan is not null).OrderByDescending(r=>r.Date).ToArray();
        var supported=Themes.Where(facts.SupportedThemes.Contains).ToArray(); if(supported.Length==0) supported=["常用应用"];
        string Select(string[] choices,Func<ReportPlan,string> selector,int days) {
            var last=prior.Where(r=>r.Date>=date.AddDays(-days)).Select(r=>selector(r.Plan!)).ToHashSet();
            var unused=choices.FirstOrDefault(c=>!last.Contains(c));
            return unused??choices.OrderBy(c=>prior.Where(r=>selector(r.Plan!)==c).Select(r=>r.Date).DefaultIfEmpty(DateOnly.MinValue).Max()).ThenBy(c=>Array.IndexOf(choices,c)).First();
        }
        // Validation rejects the most recent successful style regardless of calendar gaps.
        // Keep the fixed plan compatible with that rule even when no reports succeeded recently.
        var latestSuccessStyle=prior.FirstOrDefault(r=>r.State==ReportState.Success)?.Plan?.Style;
        var styles=ContentValidator.Styles.Where(style=>style!=latestSuccessStyle).ToArray();
        return new(date,cutoff,Select(supported,p=>p.Theme,5),Select(Formats,p=>p.Format,5),Select(styles,p=>p.Style,3),facts.Json);
    }
}

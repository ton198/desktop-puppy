using System.Windows;
using System.Text.Json;

namespace DesktopPuppy.Reports;

public sealed partial class DailyReportWindow : Window
{
    public event Action<DailyReport>? ReportDisplayed;
    private int reloadVersion;
    private readonly DailyReportStore store;
    private readonly Func<DateOnly, Task> retry;
    public DailyReportWindow(DailyReportStore store, Func<DateOnly, Task> retry, DateOnly? selected = null)
    {
        InitializeComponent();
        this.store = store;
        this.retry = retry;
        Themes.WindowSizing.FitToWorkArea(this);
        dates.SelectionChanged += (_, _) => ShowSelection();
        Loaded += (_, _) => ShowSelection();
        retryButton.Click += async (_, _) =>
        {
            if (dates.SelectedItem is not ReportItem item) return;
            retryButton.IsEnabled = false;
            try
            {
                await retry(item.Report.Date);
                await ReloadAsync(item.Report.Date);
            }
            catch { status.Text = "重试未完成，请稍后再试。"; }
            finally { retryButton.IsEnabled = true; }
        };
        Reload(selected);
    }
    public void Reload(DateOnly? selected = null) => _ = ReloadAsync(selected);
    public async Task ReloadAsync(DateOnly? selected = null)
    {
        var version = ++reloadVersion;
        try
        {
            var today = DateOnly.FromDateTime(DateTime.Now);
            var rows = await Task.Run(store.ReadAll);
            if (version != reloadVersion) return;
            var reports = rows.Where(r => r.Date >= today.AddDays(-14) && r.Date <= today).Select(r => new ReportItem(r)).ToArray();
            dates.ItemsSource = reports;
            dates.SelectedItem = reports.FirstOrDefault(r => r.Report.Date == selected) ?? reports.FirstOrDefault();
            if (reports.Length == 0)
            {
                status.Text = "还没有日报，18:20 后会检查当天使用情况。";
                body.Text = "启用 AI 和日报 AI，配置好 Key；当天截至 18:20 使用时间超过 30 分钟后，即可生成日报。";
                facts.Text = "";
                factsSection.Visibility = Visibility.Collapsed;
                retryButton.Visibility = Visibility.Collapsed;
                reportDate.Text = "暂无日报";
            }
        }
        catch { if (version == reloadVersion) status.Text = "日报读取失败，请稍后重新打开。"; }
    }
    private void ShowSelection()
    {
        if (dates.SelectedItem is not ReportItem item) return;
        var r = item.Report;
        reportDate.Text = r.Date.ToString("yyyy 年 MM 月 dd 日");
        factsSection.Visibility = Visibility.Visible;
        status.Text = $"{r.Date:yyyy-MM-dd} · {Label(r.State)}";
        body.Text = string.IsNullOrWhiteSpace(r.Text) ? StateMessage(r.State) : r.Text;
        retryButton.Visibility = r.State == ReportState.Failed ? Visibility.Visible : Visibility.Collapsed;
        try
        {
            using var json = JsonDocument.Parse(r.Plan?.FactsJson ?? "{}");
            facts.Text = string.Join(Environment.NewLine, json.RootElement.EnumerateObject()
                .Select(p => $"{FactLabel(p.Name)}：{FactValue(p.Value)}"));
        }
        catch { facts.Text = "没有可显示的统计。"; }
        if (IsLoaded && IsVisible && r.State == ReportState.Success) ReportDisplayed?.Invoke(r);
    }
    private static string Label(ReportState state) => state switch { ReportState.InsufficientActivity => "截至 18:20 使用时间未超过 30 分钟", ReportState.StatisticsUnavailable => "统计关闭或数据缺失", ReportState.WaitingForConfiguration => "等待启用 AI 并配置 Key", ReportState.Pending => "等待生成", ReportState.Generating => "正在生成", ReportState.Success => "已完成", _ => "生成失败，可重试" };
    private static string FactLabel(string key) => key.ToLowerInvariant() switch { "totalminutes" or "minutes" => "前台使用（分钟）", "applications" => "常用应用", "applicationswitchcount" => "应用切换次数", "longestcontinuousminutes" => "最长连续使用（分钟）", "deepnightminutes" => "深夜使用（分钟）", "categoryminutes" => "分类使用（分钟）", "interactions" => "桌宠互动", "aisuccesscount" => "AI 成功次数", "aifailurecount" => "AI 失败次数", "airetrycount" => "AI 重试次数", "aidisplayedcount" => "AI 展示次数", _ => key };
    private static string FactValue(JsonElement value) => value.ValueKind switch { JsonValueKind.Object => string.Join("，", value.EnumerateObject().Select(p => $"{FactLabel(p.Name)} {FactValue(p.Value)}")), JsonValueKind.Array => string.Join("；", value.EnumerateArray().Select(FactValue)), JsonValueKind.String => value.GetString() ?? "", _ => value.ToString() };
    private static string StateMessage(ReportState state) => state switch
    {
        ReportState.InsufficientActivity => "截至 18:20，使用时间未超过 30 分钟，今天不生成日报。",
        ReportState.StatisticsUnavailable => "暂无统计数据，请先开启使用统计。",
        ReportState.WaitingForConfiguration => "请启用 AI 和日报 AI，并配置 DeepSeek 与 Jev Key。",
        ReportState.Pending or ReportState.Generating => "日报正在生成，稍后查看。",
        ReportState.Failed => "日报生成失败，点击“重新生成”再试一次。",
        _ => "当天暂无日报内容。"
    };
    private sealed record ReportItem(DailyReport Report)
    {
        public string DateLabel => Report.Date.ToString("MM-dd");
        public string StateLabel => Report.State switch
        {
            ReportState.Success => "已完成",
            ReportState.Pending or ReportState.Generating => "准备中",
            ReportState.InsufficientActivity => "记录较少",
            ReportState.StatisticsUnavailable => "暂无统计",
            ReportState.WaitingForConfiguration => "等待配置",
            _ => "可重试"
        };
        public override string ToString() => $"{DateLabel}  {StateLabel}";
    }
}


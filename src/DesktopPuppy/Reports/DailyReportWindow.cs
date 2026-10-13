using System.Windows;
using System.Windows.Controls;
using System.Text.Json;
using ListBox = System.Windows.Controls.ListBox;
using TextBox = System.Windows.Controls.TextBox;
using Button = System.Windows.Controls.Button;
namespace DesktopPuppy.Reports;
public sealed class DailyReportWindow : Window
{
    public event Action<DailyReport>? ReportDisplayed;
    private int reloadVersion;
    private readonly DailyReportStore store;
    private readonly Func<DateOnly,Task> retry;
    private readonly ListBox dates=new(){MinWidth=175};
    private readonly TextBlock status=new(){Margin=new(0,0,0,12),TextWrapping=TextWrapping.Wrap};
    private readonly TextBlock body=new(){TextWrapping=TextWrapping.Wrap,FontSize=16};
    private readonly TextBox facts=new(){IsReadOnly=true,TextWrapping=TextWrapping.Wrap,AcceptsReturn=true,BorderThickness=new(0)};
    private readonly Button retryButton=new(){Content="重试",Margin=new(0,12,0,12),HorizontalAlignment=System.Windows.HorizontalAlignment.Left};
    public DailyReportWindow(DailyReportStore store,Func<DateOnly,Task> retry,DateOnly? selected=null)
    {
        this.store=store;this.retry=retry;Title="桌宠日报";Width=850;Height=600;MinWidth=600;MinHeight=400;WindowStartupLocation=WindowStartupLocation.CenterScreen;
        System.Windows.Automation.AutomationProperties.SetAutomationId(this,"DailyReportWindow");
        System.Windows.Automation.AutomationProperties.SetAutomationId(dates,"ReportDateList");
        System.Windows.Automation.AutomationProperties.SetAutomationId(status,"ReportStatus");
        System.Windows.Automation.AutomationProperties.SetAutomationId(body,"ReportBody");
        System.Windows.Automation.AutomationProperties.SetAutomationId(facts,"ReportFacts");
        System.Windows.Automation.AutomationProperties.SetAutomationId(retryButton,"ReportRetry");
        var grid=new Grid{Margin=new(18)};grid.ColumnDefinitions.Add(new(){Width=new GridLength(190)});grid.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});
        grid.Children.Add(dates);var panel=new StackPanel{Margin=new(18,0,0,0)};panel.Children.Add(status);panel.Children.Add(body);panel.Children.Add(retryButton);panel.Children.Add(new Expander{Header="准确统计（本地计算）",Content=facts,Margin=new(0,12,0,0),IsExpanded=false});
        var scroll=new ScrollViewer{Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};Grid.SetColumn(scroll,1);grid.Children.Add(scroll);Content=grid;
        dates.SelectionChanged+=(_,_)=>ShowSelection();Loaded+=(_,_)=>ShowSelection();retryButton.Click+=async(_,_)=>{if(dates.SelectedItem is not ReportItem item)return;retryButton.IsEnabled=false;try{await retry(item.Report.Date);await ReloadAsync(item.Report.Date);}catch{status.Text="重试未完成，请稍后再试。";}finally{retryButton.IsEnabled=true;}};
        Reload(selected);
    }
    public void Reload(DateOnly? selected=null)=>_ = ReloadAsync(selected);
    public async Task ReloadAsync(DateOnly? selected=null) {
        var version=++reloadVersion;
        try {
            var today=DateOnly.FromDateTime(DateTime.Now);var rows=await Task.Run(store.ReadAll);if(version!=reloadVersion)return;
            var reports=rows.Where(r=>r.Date>=today.AddDays(-14)&&r.Date<=today).Select(r=>new ReportItem(r)).ToArray();
            dates.ItemsSource=reports;dates.SelectedItem=reports.FirstOrDefault(r=>r.Report.Date==selected)??reports.FirstOrDefault();if(reports.Length==0){status.Text="还没有日报，18:20 后会检查当天使用情况。";body.Text="";retryButton.Visibility=Visibility.Collapsed;}
        }catch{if(version==reloadVersion)status.Text="日报读取失败，请稍后重新打开。";}
    }
    private void ShowSelection(){if(dates.SelectedItem is not ReportItem item)return;var r=item.Report;status.Text=$"{r.Date:yyyy-MM-dd} · {Label(r.State)}";body.Text=r.Text;retryButton.Visibility=r.State==ReportState.Failed?Visibility.Visible:Visibility.Collapsed;
        try{using var json=JsonDocument.Parse(r.Plan?.FactsJson??"{}");facts.Text=string.Join(Environment.NewLine,json.RootElement.EnumerateObject().Select(p=>$"{FactLabel(p.Name)}：{FactValue(p.Value)}"));}catch{facts.Text="没有可显示的统计。";}
        if(IsLoaded && IsVisible && r.State==ReportState.Success)ReportDisplayed?.Invoke(r);
    }
    private static string Label(ReportState state)=>state switch{ReportState.InsufficientActivity=>"截至 18:20 使用时间未超过 30 分钟",ReportState.StatisticsUnavailable=>"统计关闭或数据缺失",ReportState.WaitingForConfiguration=>"等待启用 AI 并配置 Key",ReportState.Pending=>"等待生成",ReportState.Generating=>"正在生成",ReportState.Success=>"已完成",_=>"生成失败，可重试"};
    private static string FactLabel(string key)=>key.ToLowerInvariant() switch{"totalminutes" or "minutes"=>"前台使用（分钟）","applications"=>"常用应用","applicationswitchcount"=>"应用切换次数","longestcontinuousminutes"=>"最长连续使用（分钟）","deepnightminutes"=>"深夜使用（分钟）","categoryminutes"=>"分类使用（分钟）","interactions"=>"桌宠互动","aisuccesscount"=>"AI 成功次数","aifailurecount"=>"AI 失败次数","airetrycount"=>"AI 重试次数","aidisplayedcount"=>"AI 展示次数",_=>key};
    private static string FactValue(JsonElement value)=>value.ValueKind switch{JsonValueKind.Object=>string.Join("，",value.EnumerateObject().Select(p=>$"{FactLabel(p.Name)} {FactValue(p.Value)}")),JsonValueKind.Array=>string.Join("；",value.EnumerateArray().Select(FactValue)),JsonValueKind.String=>value.GetString()??"",_=>value.ToString()};
    private sealed record ReportItem(DailyReport Report){public override string ToString()=>$"{Report.Date:MM-dd}  {Label(Report.State)}";}
}


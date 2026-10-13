using System.Globalization;
using System.Windows;
using DesktopPuppy.Ai;
using DesktopPuppy.Core.Settings;
using DesktopPuppy.Security;
using Controls=System.Windows.Controls;
namespace DesktopPuppy.Settings;
public sealed class SettingsWindow:Window
{
    private readonly SettingsService service;
    private readonly Controls.StackPanel root=new(){Margin=new Thickness(16)};
    private readonly Controls.TextBlock status=new(){TextWrapping=TextWrapping.Wrap};
    private readonly Dictionary<string,Controls.CheckBox> checks=new();
    private readonly Dictionary<string,Controls.TextBox> texts=new();
    private readonly Controls.ComboBox frequency=new(){ItemsSource=Enum.GetValues<ActivityFrequency>()};
    private readonly Controls.TabControl tabs=new();
    private readonly ApiKeyService? keys;
    public SettingsWindow(SettingsService service,ApiKeyService? keys,Func<Task> clearHistory,Func<DateOnly,DateOnly,bool,Task> export,Func<Task> resetPosition,Action<bool> startupChanged)
    {
        this.service=service;this.keys=keys;Title="Desktop Puppy 设置";Width=580;Height=690;MinWidth=480;MinHeight=500;Content=new Controls.ScrollViewer{Content=root,VerticalScrollBarVisibility=Controls.ScrollBarVisibility.Auto};root.Children.Add(tabs);
        var appearance=Page("外观与互动");Text(appearance,"Scale","大小（50–200%）");Text(appearance,"Opacity","透明度（30–100%）");Check(appearance,"Topmost","置顶");Check(appearance,"AllowHit","允许受击");Check(appearance,"Sound","声音");Check(appearance,"Fullscreen","全屏时隐藏");
        var activity=Page("活动");Check(activity,"Activity","主动活动总开关");activity.Children.Add(new Controls.TextBlock{Text="活动频率"});activity.Children.Add(frequency);Text(activity,"Cooldown","全局冷却（1–180 分钟）");Check(activity,"Quiet","安静时段");Text(activity,"QuietStart","开始（HH:mm）");Text(activity,"QuietEnd","结束（HH:mm）");Check(activity,"Peek","偷瞄");Check(activity,"Proud","得意");Check(activity,"Funny","做鬼脸");
        var privacy=Page("统计与隐私");Check(privacy,"Statistics","记录使用统计");Text(privacy,"Categories","应用分类：每行 进程名=工作 / 娱乐 / 未分类",true);Text(privacy,"Blacklist","主动活动黑名单：每行一个进程名",true);Text(privacy,"From","导出开始日期（yyyy-MM-dd）");Text(privacy,"To","导出结束日期（yyyy-MM-dd）");Check(privacy,"Titles","导出标题明细（包含窗口标题）");
        ActionButton(privacy,"导出统计",async()=>{var from=DateOnly.ParseExact(texts["From"].Text,"yyyy-MM-dd",CultureInfo.InvariantCulture);var to=DateOnly.ParseExact(texts["To"].Text,"yyyy-MM-dd",CultureInfo.InvariantCulture);if(from>to)throw new ArgumentException("开始日期不能晚于结束日期。");await export(from,to,checks["Titles"].IsChecked==true);status.Text="统计已导出。";});
        ActionButton(privacy,"清除历史",async()=>{if(System.Windows.MessageBox.Show(this,"清除最近使用、互动、AI 和日报历史？设置、位置和 Key 将保留。","清除历史",MessageBoxButton.YesNo,MessageBoxImage.Warning)==MessageBoxResult.Yes){await clearHistory();status.Text="历史已清除。";}});
        var ai=Page("AI");ai.Children.Add(new Controls.TextBlock{Text="AI 请求会发送当前应用、窗口标题、使用统计和有限历史摘要。Key 仅以当前 Windows 用户的加密密文保存，不会回显。",TextWrapping=TextWrapping.Wrap});Check(ai,"Ai","AI 总开关");Check(ai,"RightClick","右键 AI");Check(ai,"Greeting","每日问候 AI");Check(ai,"AiActivity","随机活动 AI");Check(ai,"Report","日报 AI");KeyControls(ai,AiProvider.DeepSeek);KeyControls(ai,AiProvider.Jev);
        var startup=Page("启动");Check(startup,"Startup","开机启动");startup.Children.Add(new Controls.TextBlock{Text="开机启动时立即显示托盘，10 秒后显示桌宠；托盘“显示”可提前结束等待。",TextWrapping=TextWrapping.Wrap});
        ActionButton(root,"保存设置",async()=>
        {
            var next=Read();var previous=service.Current.StartupEnabled;
            if(next.StartupEnabled!=previous)startupChanged(next.StartupEnabled);
            try{await service.SaveAsync(next);}catch{if(next.StartupEnabled!=previous)startupChanged(previous);throw;}
            status.Text="设置已保存。";
        });
        ActionButton(root,"恢复默认设置",async()=>
        {
            if(System.Windows.MessageBox.Show(this,"恢复默认设置和位置，并关闭开机启动？历史和 Key 将保留。","恢复默认",MessageBoxButton.YesNo,MessageBoxImage.Question)!=MessageBoxResult.Yes)return;
            var previous=service.Current.StartupEnabled;startupChanged(false);
            try{await service.ResetAsync();}catch{startupChanged(previous);throw;}
            await resetPosition();Populate();status.Text="已恢复默认设置。";
        });
        System.Windows.Automation.AutomationProperties.SetAutomationId(this,"AiSettingsWindow"); System.Windows.Automation.AutomationProperties.SetAutomationId(status,"AiSettingsStatus"); root.Children.Add(status);Populate();
    }
    public void SelectAiPage()=>tabs.SelectedIndex=3;
    private Controls.StackPanel Page(string name){var panel=new Controls.StackPanel{Margin=new Thickness(8)};tabs.Items.Add(new Controls.TabItem{Header=name,Content=panel});return panel;}
    private void Check(Controls.Panel panel,string key,string label){var c=new Controls.CheckBox{Content=label,Margin=new Thickness(0,6,0,6),Name=key};checks[key]=c;System.Windows.Automation.AutomationProperties.SetAutomationId(c,key=="Ai"?"AiEnabled":key=="RightClick"?"RightClickAiEnabled":key);panel.Children.Add(c);}
    private void Text(Controls.Panel panel,string key,string label,bool multiline=false){panel.Children.Add(new Controls.TextBlock{Text=label,Margin=new Thickness(0,8,0,3)});var t=new Controls.TextBox{Name=key,AcceptsReturn=multiline,Height=multiline?80:26,VerticalScrollBarVisibility=multiline?Controls.ScrollBarVisibility.Auto:Controls.ScrollBarVisibility.Disabled};System.Windows.Automation.AutomationProperties.SetAutomationId(t,key);texts[key]=t;panel.Children.Add(t);}
    private void ActionButton(Controls.Panel panel,string label,Func<Task> action)
    {
        var button=new Controls.Button{Content=label,Margin=new Thickness(0,8,0,0),Padding=new Thickness(8,4,8,4)};System.Windows.Automation.AutomationProperties.SetAutomationId(button,label=="保存设置"?"SaveAiSettings":label);panel.Children.Add(button);
        button.Click+=async(_,_)=>{root.IsEnabled=false;status.Text="";try{await action();}catch(Exception e){status.Text=e is ArgumentException or FormatException?e.Message:"操作失败，请检查配置或稍后再试。";}finally{root.IsEnabled=true;}};
    }
    private void KeyControls(Controls.Panel panel,AiProvider provider)
    {
        var label=new Controls.TextBlock{Text=provider+" Key：检查中",Margin=new Thickness(0,10,0,3)};System.Windows.Automation.AutomationProperties.SetAutomationId(label,provider+"KeyStatus"); panel.Children.Add(label); Loaded+=async(_,_)=>{try{var configured=keys is not null && await Task.Run(()=>keys.IsConfigured(provider));label.Text=configured?"已保存（不回显）":"未配置";}catch{label.Text=provider+" Key：读取失败";}};
        var password=new Controls.PasswordBox{IsEnabled=keys is not null};System.Windows.Automation.AutomationProperties.SetAutomationId(password,provider+"KeyInput");panel.Children.Add(password);Closed+=(_,_)=>password.Clear();
        ActionButton(panel,"保存 "+provider+" Key",async()=>{if(keys is null)throw new InvalidOperationException();var secret=password.Password;password.Clear();await Task.Run(()=>keys.Save(provider,secret));label.Text="已保存（不回显）";});
        ActionButton(panel,"删除 "+provider+" Key",async()=>{if(keys is null)return;password.Clear();await Task.Run(()=>keys.Delete(provider));label.Text="未配置";});
    }
    private void Populate()
    {
        var s=service.Current;
        Set("Topmost",s.Window.Topmost);Set("AllowHit",s.Interaction.AllowHit);Set("Sound",s.Interaction.SoundEnabled);Set("Fullscreen",s.Interaction.SuppressOnFullscreen);Set("Activity",s.Activity.Enabled);Set("Quiet",s.Activity.QuietHoursEnabled);Set("Peek",s.Activity.PeekEnabled);Set("Proud",s.Activity.ProudEnabled);Set("Funny",s.Activity.FunnyFaceEnabled);Set("Statistics",s.Statistics.Enabled);Set("Ai",s.Ai.Enabled);Set("RightClick",s.Ai.RightClickEnabled);Set("Greeting",s.Ai.GreetingEnabled);Set("AiActivity",s.Ai.ActivityEnabled);Set("Report",s.Ai.ReportEnabled);Set("Startup",s.StartupEnabled);
        texts["Scale"].Text=(s.Window.Scale*100).ToString(CultureInfo.InvariantCulture);texts["Opacity"].Text=(s.Window.Opacity*100).ToString(CultureInfo.InvariantCulture);texts["Cooldown"].Text=s.Activity.GlobalCooldown.TotalMinutes.ToString(CultureInfo.InvariantCulture);texts["QuietStart"].Text=s.Activity.QuietHoursStart.ToString("HH:mm");texts["QuietEnd"].Text=s.Activity.QuietHoursEnd.ToString("HH:mm");frequency.SelectedItem=s.Activity.Frequency;texts["Categories"].Text=string.Join(Environment.NewLine,s.Statistics.Categories.Select(x=>$"{x.Key}={CategoryName(x.Value)}"));texts["Blacklist"].Text=string.Join(Environment.NewLine,s.Activity.Blacklist);texts["From"].Text=DateOnly.FromDateTime(DateTime.Now).AddDays(-14).ToString("yyyy-MM-dd");texts["To"].Text=DateOnly.FromDateTime(DateTime.Now).ToString("yyyy-MM-dd");
    }
    private void Set(string key,bool value)=>checks[key].IsChecked=value;
    private bool On(string key)=>checks[key].IsChecked==true;
    private string[] Lines(string key)=>texts[key].Text.Split(['\r','\n'],StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries);
    private static string CategoryName(ApplicationCategory category)=>category switch{ApplicationCategory.Work=>"工作",ApplicationCategory.Entertainment=>"娱乐",_=>"未分类"};
    private AppSettings Read()
    {
        var categories=new Dictionary<string,ApplicationCategory>(StringComparer.OrdinalIgnoreCase);
        foreach(var line in Lines("Categories")){var pair=line.Split('=',2,StringSplitOptions.TrimEntries);if(pair.Length!=2)throw new ArgumentException("分类格式：进程名=工作 / 娱乐 / 未分类");var name=pair[1] switch{"工作"=>"Work","娱乐"=>"Entertainment","未分类"=>"Unclassified",_=>pair[1]};if(!Enum.TryParse<ApplicationCategory>(name,true,out var category)||!Enum.IsDefined(category))throw new ArgumentException("分类格式：进程名=工作 / 娱乐 / 未分类");categories.Add(pair[0],category);}
        var current=service.Current;
        var next=current with
        {
            Window=new(){Scale=double.Parse(texts["Scale"].Text,CultureInfo.InvariantCulture)/100,Opacity=double.Parse(texts["Opacity"].Text,CultureInfo.InvariantCulture)/100,Topmost=On("Topmost")},
            Interaction=current.Interaction with{AllowHit=On("AllowHit"),SoundEnabled=On("Sound"),SuppressOnFullscreen=On("Fullscreen")},
            Activity=current.Activity with{Enabled=On("Activity"),Frequency=(ActivityFrequency)frequency.SelectedItem,GlobalCooldown=TimeSpan.FromMinutes(double.Parse(texts["Cooldown"].Text,CultureInfo.InvariantCulture)),QuietHoursEnabled=On("Quiet"),QuietHoursStart=TimeOnly.ParseExact(texts["QuietStart"].Text,"HH:mm",CultureInfo.InvariantCulture),QuietHoursEnd=TimeOnly.ParseExact(texts["QuietEnd"].Text,"HH:mm",CultureInfo.InvariantCulture),PeekEnabled=On("Peek"),ProudEnabled=On("Proud"),FunnyFaceEnabled=On("Funny"),Blacklist=Lines("Blacklist")},
            Statistics=current.Statistics with{Enabled=On("Statistics"),Categories=categories},
            Ai=current.Ai with{Enabled=On("Ai"),RightClickEnabled=On("RightClick"),GreetingEnabled=On("Greeting"),ActivityEnabled=On("AiActivity"),ReportEnabled=On("Report")},StartupEnabled=On("Startup")
        };
        SettingsService.Validate(next);return next;
    }
}






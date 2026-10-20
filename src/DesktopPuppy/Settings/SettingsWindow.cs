using System.Globalization;
using System.Windows;
using DesktopPuppy.Ai;
using DesktopPuppy.Core.Settings;
using DesktopPuppy.Security;
using Controls = System.Windows.Controls;
using MessageBox = System.Windows.MessageBox;

namespace DesktopPuppy.Settings;

public sealed partial class SettingsWindow : Window
{
    private readonly SettingsService service;
    private readonly ApiKeyService? keys;
    private readonly Dictionary<string, Controls.CheckBox> checks = new();
    private readonly Dictionary<string, Controls.TextBox> texts = new();

    public SettingsWindow(SettingsService service, ApiKeyService? keys, Func<Task> clearHistory,
        Func<DateOnly, DateOnly, bool, Task> export, Func<Task> resetPosition, Action<bool> startupChanged)
    {
        InitializeComponent();
        this.service = service;
        this.keys = keys;
        Themes.WindowSizing.FitToWorkArea(this);
        foreach (var key in new[] { "Topmost", "AllowHit", "Sound", "Fullscreen", "Activity", "Quiet", "Peek", "Proud", "Funny", "Statistics", "Titles", "Ai", "RightClick", "Greeting", "AiActivity", "Report", "Startup" })
            checks[key] = (Controls.CheckBox)FindName(key == "Topmost" ? "TopmostInput" : key);
        foreach (var key in new[] { "Scale", "Opacity", "Cooldown", "QuietStart", "QuietEnd", "Categories", "Blacklist", "From", "To" })
            texts[key] = (Controls.TextBox)FindName(key == "Opacity" ? "OpacityInput" : key);
        BindAction(SaveSettingsButton, async () =>
        {
            var next = Read();
            var previous = service.Current.StartupEnabled;
            if (next.StartupEnabled != previous) startupChanged(next.StartupEnabled);
            try { await service.SaveAsync(next); }
            catch { if (next.StartupEnabled != previous) startupChanged(previous); throw; }
            status.Text = "设置已保存。";
        });
        BindAction(ResetButton, async () =>
        {
            if (MessageBox.Show(this, "恢复默认设置和位置，并关闭开机启动？历史和 Key 将保留。", "恢复默认", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            var previous = service.Current.StartupEnabled;
            startupChanged(false);
            try { await service.ResetAsync(); }
            catch { startupChanged(previous); throw; }
            await resetPosition();
            Populate();
            status.Text = "已恢复默认设置。";
        });
        BindAction(ExportButton, async () =>
        {
            var from = DateOnly.ParseExact(texts["From"].Text, "yyyy-MM-dd", CultureInfo.InvariantCulture);
            var to = DateOnly.ParseExact(texts["To"].Text, "yyyy-MM-dd", CultureInfo.InvariantCulture);
            if (from > to) throw new ArgumentException("开始日期不能晚于结束日期。");
            await export(from, to, checks["Titles"].IsChecked == true);
            status.Text = "统计已导出。";
        });
        BindAction(ClearButton, async () =>
        {
            if (MessageBox.Show(this, "清除最近使用、互动、AI 和日报历史？设置、位置和 Key 将保留。", "清除历史", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            await clearHistory();
            status.Text = "历史已清除。";
        });
        KeyControls(AiProvider.DeepSeek, DeepSeekStatus, DeepSeekInput, SaveDeepSeek, DeleteDeepSeek);
        KeyControls(AiProvider.Jev, JevStatus, JevInput, SaveJev, DeleteJev);
        Populate();
    }

    public void SelectAiPage() => tabs.SelectedIndex = 3;

    private void BindAction(Controls.Button button, Func<Task> action)
    {
        button.Click += async (_, _) =>
        {
            root.IsEnabled = false;
            status.Text = "";
            status.SetResourceReference(Controls.TextBlock.ForegroundProperty, "AccentBrush");
            try { await action(); }
            catch (Exception e)
            {
                status.SetResourceReference(Controls.TextBlock.ForegroundProperty, "ErrorBrush");
                status.Text = e is ArgumentException or FormatException ? e.Message : "操作失败，请检查配置或稍后再试。";
            }
            finally { root.IsEnabled = true; }
        };
    }

    private void KeyControls(AiProvider provider, Controls.TextBlock label, Controls.PasswordBox password,
        Controls.Button save, Controls.Button delete)
    {
        password.IsEnabled = save.IsEnabled = delete.IsEnabled = keys is not null;
        Loaded += async (_, _) =>
        {
            try { label.Text = keys is not null && await Task.Run(() => keys.IsConfigured(provider)) ? "已保存（不回显）" : "未配置"; }
            catch { label.Text = "读取失败"; }
        };
        Closed += (_, _) => password.Clear();
        BindAction(save, async () =>
        {
            if (keys is null) throw new InvalidOperationException();
            var secret = password.Password;
            password.Clear();
            await Task.Run(() => keys.Save(provider, secret));
            label.Text = "已保存（不回显）";
            status.Text = $"{provider} Key 已保存。";
        });
        BindAction(delete, async () =>
        {
            if (keys is null) return;
            password.Clear();
            await Task.Run(() => keys.Delete(provider));
            label.Text = "未配置";
            status.Text = $"{provider} Key 已删除。";
        });
    }
    private void Populate()
    {
        var s = service.Current;
        Set("Topmost", s.Window.Topmost); Set("AllowHit", s.Interaction.AllowHit); Set("Sound", s.Interaction.SoundEnabled); Set("Fullscreen", s.Interaction.SuppressOnFullscreen); Set("Activity", s.Activity.Enabled); Set("Quiet", s.Activity.QuietHoursEnabled); Set("Peek", s.Activity.PeekEnabled); Set("Proud", s.Activity.ProudEnabled); Set("Funny", s.Activity.FunnyFaceEnabled); Set("Statistics", s.Statistics.Enabled); Set("Ai", s.Ai.Enabled); Set("RightClick", s.Ai.RightClickEnabled); Set("Greeting", s.Ai.GreetingEnabled); Set("AiActivity", s.Ai.ActivityEnabled); Set("Report", s.Ai.ReportEnabled); Set("Startup", s.StartupEnabled);
        texts["Scale"].Text = (s.Window.Scale * 100).ToString(CultureInfo.InvariantCulture); texts["Opacity"].Text = (s.Window.Opacity * 100).ToString(CultureInfo.InvariantCulture); texts["Cooldown"].Text = s.Activity.GlobalCooldown.TotalMinutes.ToString(CultureInfo.InvariantCulture); texts["QuietStart"].Text = s.Activity.QuietHoursStart.ToString("HH:mm"); texts["QuietEnd"].Text = s.Activity.QuietHoursEnd.ToString("HH:mm"); frequency.SelectedValue = s.Activity.Frequency; texts["Categories"].Text = string.Join(Environment.NewLine, s.Statistics.Categories.Select(x => $"{x.Key}={CategoryName(x.Value)}")); texts["Blacklist"].Text = string.Join(Environment.NewLine, s.Activity.Blacklist); texts["From"].Text = DateOnly.FromDateTime(DateTime.Now).AddDays(-14).ToString("yyyy-MM-dd"); texts["To"].Text = DateOnly.FromDateTime(DateTime.Now).ToString("yyyy-MM-dd");
    }
    private void Set(string key, bool value) => checks[key].IsChecked = value;
    private bool On(string key) => checks[key].IsChecked == true;
    private string[] Lines(string key) => texts[key].Text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    private static string CategoryName(ApplicationCategory category) => category switch { ApplicationCategory.Work => "工作", ApplicationCategory.Entertainment => "娱乐", _ => "未分类" };
    private AppSettings Read()
    {
        var categories = new Dictionary<string, ApplicationCategory>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in Lines("Categories")) { var pair = line.Split('=', 2, StringSplitOptions.TrimEntries); if (pair.Length != 2) throw new ArgumentException("分类格式：进程名=工作 / 娱乐 / 未分类"); var name = pair[1] switch { "工作" => "Work", "娱乐" => "Entertainment", "未分类" => "Unclassified", _ => pair[1] }; if (!Enum.TryParse<ApplicationCategory>(name, true, out var category) || !Enum.IsDefined(category)) throw new ArgumentException("分类格式：进程名=工作 / 娱乐 / 未分类"); categories.Add(pair[0], category); }
        var current = service.Current;
        var next = current with
        {
            Window = new() { Scale = double.Parse(texts["Scale"].Text, CultureInfo.InvariantCulture) / 100, Opacity = double.Parse(texts["Opacity"].Text, CultureInfo.InvariantCulture) / 100, Topmost = On("Topmost") },
            Interaction = current.Interaction with { AllowHit = On("AllowHit"), SoundEnabled = On("Sound"), SuppressOnFullscreen = On("Fullscreen") },
            Activity = current.Activity with { Enabled = On("Activity"), Frequency = (ActivityFrequency)frequency.SelectedValue, GlobalCooldown = TimeSpan.FromMinutes(double.Parse(texts["Cooldown"].Text, CultureInfo.InvariantCulture)), QuietHoursEnabled = On("Quiet"), QuietHoursStart = TimeOnly.ParseExact(texts["QuietStart"].Text, "HH:mm", CultureInfo.InvariantCulture), QuietHoursEnd = TimeOnly.ParseExact(texts["QuietEnd"].Text, "HH:mm", CultureInfo.InvariantCulture), PeekEnabled = On("Peek"), ProudEnabled = On("Proud"), FunnyFaceEnabled = On("Funny"), Blacklist = Lines("Blacklist") },
            Statistics = current.Statistics with { Enabled = On("Statistics"), Categories = categories },
            Ai = current.Ai with { Enabled = On("Ai"), RightClickEnabled = On("RightClick"), GreetingEnabled = On("Greeting"), ActivityEnabled = On("AiActivity"), ReportEnabled = On("Report") },
            StartupEnabled = On("Startup")
        };
        SettingsService.Validate(next); return next;
    }
}






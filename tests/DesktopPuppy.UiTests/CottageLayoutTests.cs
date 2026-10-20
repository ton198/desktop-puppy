using System.Globalization;
using System.IO;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DesktopPuppy.Pet;
using DesktopPuppy.Pet.Statistics;
using DesktopPuppy.Reports;
using DesktopPuppy.Settings;
using DesktopPuppy.Usage;
using DesktopPuppy.Ai;
using DesktopPuppy.Security;
using Xunit;

namespace DesktopPuppy.UiTests;

public sealed class CottageLayoutTests
{
    [Fact, Trait("Category", "Layout")]
    public void Real_windows_keep_actions_reachable_and_long_content_scrollable()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { VerifyWindows(); }
            catch (Exception e) { failure = e; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(90)), "Layout verification timed out.");
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private static void VerifyWindows()
    {
        var app = new System.Windows.Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("/DesktopPuppy;component/Themes/Cottage.xaml", UriKind.Relative)
        });
        using var seed = new UsageStatisticsSeed();
        var settings = new SettingsService(seed.DatabasePath);
        settings.LoadAsync().GetAwaiter().GetResult();
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        var keyService = new ApiKeyService(new AiSqliteStore(seed.DatabasePath), new DpapiApiKeyProtector());
        var window = new SettingsWindow(settings, keyService, () => Task.CompletedTask,
            (_, _, _) => Task.CompletedTask, () => Task.CompletedTask, _ => { });
        try
        {
            window.Show();
            Drain();
            var tabs = (TabControl)window.FindName("tabs");
            var save = (System.Windows.Controls.Button)window.FindName("SaveSettingsButton");
            var scale = (System.Windows.Controls.TextBox)window.FindName("Scale");
            scale.Text = "125";
            for (var page = 0; page < tabs.Items.Count; page++)
            {
                tabs.SelectedIndex = page;
                Drain();
                AssertInside(save, window);
                Capture(window, $"settings-{page + 1}");
            }
            tabs.SelectedIndex = 0;
            Drain();
            Assert.Equal("125", scale.Text);
            Assert.True(scale.Focus());
            window.SelectAiPage();
            Drain();
            var password = (PasswordBox)window.FindName("DeepSeekInput");
            password.Password = "isolated-ui-test-key";
            ((System.Windows.Controls.Button)window.FindName("SaveDeepSeek")).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            PumpUntil(() => ((TextBlock)window.FindName("DeepSeekStatus")).Text == "已保存（不回显）");
            Assert.Empty(password.Password);
            Assert.True(keyService.IsConfigured(AiProvider.DeepSeek));
            ((System.Windows.Controls.Button)window.FindName("DeleteDeepSeek")).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            PumpUntil(() => ((TextBlock)window.FindName("DeepSeekStatus")).Text == "未配置");
            Assert.False(keyService.IsConfigured(AiProvider.DeepSeek));
            tabs.SelectedIndex = 0;
            Drain();
            foreach (var factor in new[] { 1d, 1.5, 2d }) Capture(window, $"settings-{factor * 100:0}pct", factor);
            window.Width = window.MinWidth;
            window.Height = window.MinHeight;
            foreach (TabItem tab in tabs.Items)
            {
                tabs.SelectedItem = tab;
                Drain();
                AssertInside(save, window);
            }
            Capture(window, "settings-minimum");
            window.Close();

            var today = DateOnly.FromDateTime(DateTime.Now);
            var store = new DailyReportStore(seed.DatabasePath);
            var reportText = string.Join("\n\n", Enumerable.Repeat("今天认真做事，也留了一点时间给自己。小狗在桌面上巡逻，替你记住这些平凡又珍贵的小片段。", 24));
            store.Save(new(today, ReportState.Success, new(today, DateTimeOffset.UtcNow, "日常", "信", "warm", "{\"totalMinutes\":135}"), reportText, "", DateTimeOffset.UtcNow));
            var report = new DailyReportWindow(store, _ =>
            {
                store.Save(new(today, ReportState.Success, null, reportText, "", DateTimeOffset.UtcNow));
                return Task.CompletedTask;
            }, today);
            report.Show();
            PumpUntil(() => ((TextBlock)report.FindName("body")).Text == reportText);
            var reportScroll = FindVisual<ScrollViewer>(report, c => System.Windows.Automation.AutomationProperties.GetAutomationId(c) == "ReportScroll");
            Assert.True(reportScroll.ScrollableHeight > 0);
            foreach (var factor in new[] { 1d, 1.5, 2d }) Capture(report, $"report-{factor * 100:0}pct", factor);
            reportScroll.ScrollToEnd();
            var factsExpander = FindVisual<Expander>(report, _ => true);
            factsExpander.IsExpanded = true;
            Drain();
            Capture(report, "report-facts");
            store.Save(new(today, ReportState.Failed, null, "", "test", DateTimeOffset.UtcNow));
            report.Reload(today);
            var retry = (System.Windows.Controls.Button)report.FindName("retryButton");
            PumpUntil(() => retry.Visibility == Visibility.Visible);
            Capture(report, "report-failed");
            retry.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            PumpUntil(() => ((TextBlock)report.FindName("body")).Text == reportText && retry.IsEnabled);
            Assert.Equal(Visibility.Collapsed, retry.Visibility);
            report.Close();

            var query = new PreviewQuery();
            var model = new UsageStatisticsViewModel(query, TimeZoneInfo.Local, () => DateTimeOffset.UtcNow,
                _ => Task.FromResult(new UsageFacts(today, DateTimeOffset.UtcNow, TimeSpan.FromMinutes(135), 12,
                    TimeSpan.FromMinutes(45), TimeSpan.Zero, new Dictionary<int, TimeSpan> { [9] = TimeSpan.FromMinutes(60), [10] = TimeSpan.FromMinutes(75) },
                    new Dictionary<string, TimeSpan> { ["Work"] = TimeSpan.FromMinutes(90), ["Entertainment"] = TimeSpan.FromMinutes(45) },
                    [], new Dictionary<string, int> { ["Click"] = 3 }, 1, 0, 0, 1)));
            var statistics = new UsageStatisticsWindow(model);
            statistics.Show();
            PumpUntil(() => model.Statistics is not null);
            model.SelectApplication("CodeApp");
            Drain();
            var titleList = FindVisual<System.Windows.Controls.ListBox>(statistics,
                c => System.Windows.Automation.AutomationProperties.GetAutomationId(c) == "StatisticsTitleList");
            var titleItem = (ListBoxItem)titleList.ItemContainerGenerator.ContainerFromIndex(0);
            Assert.True(titleItem.Focusable);
            Assert.True(titleItem.Focus());
            foreach (var factor in new[] { 1d, 1.5, 2d }) Capture(statistics, $"statistics-{factor * 100:0}pct", factor);
            statistics.Width = statistics.MinWidth;
            statistics.Height = statistics.MinHeight;
            FindVisual<Expander>(statistics, _ => true).IsExpanded = true;
            Drain();
            var list = (System.Windows.Controls.ListBox)statistics.FindName("ApplicationList");
            Assert.True(list.ActualHeight >= 60, $"Ranking collapsed to {list.ActualHeight} DIP.");
            Capture(statistics, "statistics-minimum-expanded");
            statistics.Close();

            var stateQuery = new StateQuery();
            var stateModel = new UsageStatisticsViewModel(stateQuery, TimeZoneInfo.Local, () => DateTimeOffset.UtcNow);
            var stateWindow = new UsageStatisticsWindow(stateModel);
            stateWindow.Show();
            Drain();
            Assert.True(stateModel.IsLoading);
            Capture(stateWindow, "statistics-loading");
            stateQuery.Result.SetResult(new(today, TimeSpan.Zero, []));
            PumpUntil(() => !stateModel.IsLoading);
            Assert.Empty(stateModel.Statistics!.Applications);
            Assert.Equal(Visibility.Visible, FindVisual<TextBlock>(stateWindow, c => System.Windows.Automation.AutomationProperties.GetAutomationId(c) == "StatisticsEmptyState").Visibility);
            Capture(stateWindow, "statistics-empty");
            stateQuery.Fail = true;
            var failedRefresh = stateModel.RefreshAsync();
            PumpUntil(() => failedRefresh.IsCompleted);
            Assert.False(string.IsNullOrWhiteSpace(stateModel.ErrorMessage));
            Assert.Equal(Visibility.Visible, FindVisual<TextBlock>(stateWindow, c => System.Windows.Automation.AutomationProperties.GetAutomationId(c) == "StatisticsErrorState").Visibility);
            Capture(stateWindow, "statistics-error");
            stateWindow.Close();

            var speech = new PetSpeechWindow();
            ((Border)speech.FindName("BubbleBorder")).Visibility = Visibility.Visible;
            ((TextBlock)speech.FindName("BubbleText")).Text = "忙了一天，也记得给自己留一点休息的时间。";
            speech.Show();
            Drain();
            Assert.InRange(speech.ActualWidth, 1, 225);
            Capture(speech, "speech");
            speech.Close();

            // Opt-in pause supports a native screenshot review without touching the user's database.
            if (int.TryParse(Environment.GetEnvironmentVariable("DESKTOP_PUPPY_PREVIEW_SECONDS"), out var seconds))
            {
                var preview = new SettingsWindow(settings, null, () => Task.CompletedTask, (_, _, _) => Task.CompletedTask, () => Task.CompletedTask, _ => { });
                preview.Show();
                var until = DateTime.UtcNow.AddSeconds(Math.Clamp(seconds, 1, 60));
                PumpUntil(() => DateTime.UtcNow >= until, 65);
                preview.Close();
            }
        }
        finally
        {
            foreach (Window open in app.Windows.Cast<Window>().ToArray()) open.Close();
            app.Shutdown();
        }
    }

    private static void AssertInside(FrameworkElement control, Window window)
    {
        var content = (FrameworkElement)window.Content;
        var bounds = control.TransformToAncestor(content).TransformBounds(new Rect(control.RenderSize));
        Assert.True(bounds.Width > 0 && bounds.Height > 0);
        Assert.InRange(bounds.Top, 0, content.ActualHeight);
        Assert.InRange(bounds.Bottom, 0, content.ActualHeight + 1);
    }

    private static T FindVisual<T>(DependencyObject root, Func<T, bool> match) where T : DependencyObject
    {
        if (root is T candidate && match(candidate)) return candidate;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var found = FindVisualOrNull(VisualTreeHelper.GetChild(root, i), match);
            if (found is not null) return found;
        }
        throw new InvalidOperationException($"No {typeof(T).Name} in visual tree.");
    }

    private static T? FindVisualOrNull<T>(DependencyObject root, Func<T, bool> match) where T : DependencyObject
    {
        if (root is T candidate && match(candidate)) return candidate;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var found = FindVisualOrNull(VisualTreeHelper.GetChild(root, i), match);
            if (found is not null) return found;
        }
        return null;
    }

    private static void Drain() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);

    private static void PumpUntil(Func<bool> condition, int seconds = 5)
    {
        var deadline = DateTime.UtcNow.AddSeconds(seconds);
        while (!condition() && DateTime.UtcNow < deadline) { Drain(); Thread.Sleep(10); }
        Assert.True(condition(), "Asynchronous window state did not settle.");
        Drain();
    }

    private static void Capture(Window window, string name, double scale = 1)
    {
        Drain();
        var directory = Environment.GetEnvironmentVariable("DESKTOP_PUPPY_RENDER_DIR");
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory);
        var content = (FrameworkElement)window.Content;
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(content.ActualWidth * scale), (int)Math.Ceiling(content.ActualHeight * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        bitmap.Render(content);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = File.Create(Path.Combine(directory, name + ".png"));
        encoder.Save(output);
    }

    private sealed class PreviewQuery : IUsageStatisticsQuery
    {
        public Task<DailyUsageStatistics> QueryAsync(DateOnly date, CancellationToken cancellationToken = default) => Task.FromResult(new DailyUsageStatistics(date, TimeSpan.FromMinutes(135),
            [new("CodeApp", TimeSpan.FromMinutes(90), [new("桌宠小屋设计 — 一份较长的窗口标题，用于验证省略显示和完整提示", TimeSpan.FromMinutes(90))]), new("BrowserApp", TimeSpan.FromMinutes(45), [new("参考资料", TimeSpan.FromMinutes(45))])]));
    }

    private sealed class StateQuery : IUsageStatisticsQuery
    {
        public TaskCompletionSource<DailyUsageStatistics> Result { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool Fail { get; set; }
        public Task<DailyUsageStatistics> QueryAsync(DateOnly date, CancellationToken cancellationToken = default) =>
            Fail ? Task.FromException<DailyUsageStatistics>(new InvalidOperationException("测试数据暂时不可用")) : Result.Task;
    }
}

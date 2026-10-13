using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using DesktopPuppy.Activities;
using DesktopPuppy.Ai;
using DesktopPuppy.Core.Settings;
using DesktopPuppy.Data;
using DesktopPuppy.Pet.Interaction;
using DesktopPuppy.Pet.Tray;
using DesktopPuppy.Reports;
using DesktopPuppy.Runtime;
using DesktopPuppy.Settings;
using DesktopPuppy.Usage;

namespace DesktopPuppy;

public partial class App
{
    private SettingsService? _settings;
    private PetPlacementStore? _placementStore;
    private HistoryMaintenanceService? _history;
    private ActivityScheduler? _activities;
    private GreetingService? _greetings;
    private DailyReportStore? _reportsStore;
    private DailyReportService? _reports;
    private DailyReportWindow? _reportWindow;
    private AiPipeline? _aiPipeline;
    private WinFormsTrayIconHost? _trayHost;
    private DispatcherTimer? _featureTimer;
    private readonly RuntimeState _runtime = new();
    private StartupDelayCoordinator? _startupDelay;
    private readonly StartupRegistrationService _startupRegistration = new();
    private CancellationTokenSource _featureCancellation = new();
    private Task _featureCheck = Task.CompletedTask, _reportCheck = Task.CompletedTask, _statisticsTransition = Task.CompletedTask,
        _eventWrites = Task.CompletedTask, _placementSave = Task.CompletedTask;
    private readonly object _recordGate = new();
    private bool _maintenance, _featureClosing, _featureInitializationFailed;
    private TaskCompletionSource? _maintenanceFinished;
    private string DatabasePath => _usageStore?.DatabasePath ?? Environment.GetEnvironmentVariable("DESKTOP_PUPPY_USAGE_DB_PATH") ??
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DesktopPuppy", "desktop-puppy.db");

    private async Task InitializeFeaturesAsync(bool startup)
    {
        _settings ??= new SettingsService(DatabasePath);
        _placementStore = new(DatabasePath);
        _history = new(DatabasePath, TimeZoneInfo.Local, () => _usageCollector?.CaptureStatisticsSnapshot() ?? new(DateTimeOffset.UtcNow, null));
        await Task.Run(_history.Initialize);
        await Task.Run(() => { _activities = new(DatabasePath); _greetings = new(DatabasePath); _reportsStore = new(DatabasePath); });
        _reports = new(_reportsStore!, TimeZoneInfo.Local, BuildReportFactsAsync,
            () => !_featureClosing && !_maintenance && !(_petWindow?.Paused ?? true) && (_settings?.Current.Ai.Enabled ?? false) && (_settings?.Current.Ai.ReportEnabled ?? false) && KeysReady(), GenerateReportAsync);
        _reports.Completed += report => Dispatcher.BeginInvoke(() =>
        {
            _reportWindow?.Reload(report.Date);
            if (_maintenance || _featureClosing || !_runtime.CanRequest || _petWindow is null || !_petWindow.IsVisible || CheckFullscreen()) return;
            _trayHost?.Notify(report.State == ReportState.Success ? $"{report.Date:MM-dd} 日报已完成，点击查看。" : "日报生成失败，点击打开后重试。", () => OpenReport(report.Date));
        });
        _petWindow!.ApplySettings(_settings.Current);
        if (!_settings.Current.Statistics.Enabled) { QueueRecord("StatisticsDisabled"); await Task.Run(() => _usageCollector?.Stop()); }
        else QueueRecord("StatisticsEnabled");
        _petWindow.RequestBlock = () => _featureInitializationFailed || _maintenance || _featureClosing || _runtime.Locked || _runtime.Suspended;
        _petWindow.FullscreenNow = () => _settings.Current.Interaction.SuppressOnFullscreen && CheckFullscreen();
        _settings.Changed += (_, _) => Dispatcher.BeginInvoke(new Action(ApplyCurrentSettings));
        _petWindow.PlacementChanged += QueueSavePlacement;
        _petWindow.InteractionTriggered += kind => QueueRecord(kind.ToString());
        _petWindow.BeforeExit = StopFeaturesAsync;
        _petWindow.IsVisibleChanged += (_, _) => QueueFeatureCheck();
        var saved = await _placementStore.LoadAsync();
        _petWindow.Loaded += (_, _) => _petWindow.RestorePlacement(saved);
        if (_petWindow.AiCoordinator is not null)
        {
            _petWindow.AiCoordinator.FactsProvider = async ct => SerializeFacts(await BuildFactsAsync(DateOnly.FromDateTime(DateTime.Now), TimeOnly.MaxValue, ct));
            _petWindow.AiCoordinator.RecentTitlesProvider = ct => Task.Run<IReadOnlyList<string>>(() =>
                _usageStore!.ReadWindowTitleIntervals().Where(t => t.StartedAt >= DateTimeOffset.UtcNow.AddHours(-2))
                .OrderByDescending(t => t.StartedAt).Select(t => t.WindowTitle).Distinct().Take(6)
                .Select(t => t.Length > 160 ? t[..160] : t).ToArray(), ct);
        }
        _trayApplicationActions!.PauseAction = () => { _petWindow.SetPaused(!_petWindow.Paused); _runtime.Paused = _petWindow.Paused; QueueFeatureCheck(); };
        _trayApplicationActions.SettingsAction = () => OpenSettings(false);
        _trayApplicationActions.SpeechAction = () => { if (!_maintenance) _ = _petWindow.AiCoordinator?.GenerateAsync(AiSceneKind.UserRequested); };
        _trayApplicationActions.TodayReportAction = () => OpenReport(DateOnly.FromDateTime(DateTime.Now));
        _trayApplicationActions.ReportHistoryAction = () => OpenReport(null);
        _runtime.StartupWaiting = startup;
        _startupDelay = new(_runtime, () => { _petWindow.SetStartupWaiting(false); QueueFeatureCheck(); });
        _trayApplicationActions.ShowAction = () => { _startupDelay.ShowNow(); _petWindow.SetStartupWaiting(false); _petWindow.ShowFromUser(); };
        _featureTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
        _featureTimer.Tick += (_, _) => QueueFeatureCheck();
        _featureTimer.Start();
        if (startup) _ = _startupDelay.StartAsync();
        else { _petWindow.SetStartupWaiting(false); _petWindow.ShowFromUser(); }
        if (_settings.LastError is not null) _petWindow.NotifyAi(_settings.LastError);
        QueueFeatureCheck();
    }

    private void ApplyCurrentSettings()
    {
        if (_settings is null || _petWindow is null || _featureClosing) return;
        var settings = _settings.Current;
        _petWindow.ApplySettings(settings);
        _petWindow.SetFullscreen(settings.Interaction.SuppressOnFullscreen && CheckFullscreen());
        if (_maintenance) return;
        _statisticsTransition = _statisticsTransition.ContinueWith(_ => SetStatisticsEnabledAsync(), TaskScheduler.Default).Unwrap();
        QueueFeatureCheck();
    }
    private async Task SetStatisticsEnabledAsync()
    {
        try
        {
            if (_usageCollector is null || _maintenance || _featureClosing) return;
            var enabled = _settings!.Current.Statistics.Enabled;
            if (!enabled && _usageCollector.IsStarted) { QueueRecord("StatisticsDisabled"); await Task.Run(_usageCollector.Stop); }
            else if (enabled && !_usageCollector.IsStarted)
            {
                QueueRecord("StatisticsEnabled");
                await Task.Run(() =>
                {
                    _usageCollector.Start();
                    if (_runtime.Locked || _runtime.Suspended) _usageCollector.OnSessionBoundary(_runtime.Locked ? SessionBoundary.Lock : SessionBoundary.Suspend);
                });
            }
        }
        catch { _ = Dispatcher.BeginInvoke(() => _petWindow?.NotifyAi("统计状态切换失败，请稍后再试。")); }
    }
    private void OpenSettings(bool ai)
    {
        if (_featureInitializationFailed || _settings is null) { _petWindow?.NotifyAi("设置尚未安全初始化，请检查数据库或重启程序。"); return; }
        if (_aiSettingsWindow is null)
        {
            _aiSettingsWindow = new(_settings, _apiKeys, ClearHistoryAsync, ExportAsync,
                async () => { await _placementSave; await _placementStore!.ResetAsync(); _petWindow?.ResetPosition(); QueueSavePlacement(); await _placementSave; },
                enabled => { if (!_startupRegistration.TrySetEnabled(enabled)) throw new InvalidOperationException("开机启动设置失败。"); });
            _aiSettingsWindow.Closed += (_, _) => _aiSettingsWindow = null;
        }
        if (ai) _aiSettingsWindow.SelectAiPage();
        if (!_aiSettingsWindow.IsVisible) _aiSettingsWindow.Show();
        if (_aiSettingsWindow.WindowState == WindowState.Minimized) _aiSettingsWindow.WindowState = WindowState.Normal;
        _aiSettingsWindow.Activate();
    }
    private bool KeysReady()
    {
        try { return _apiKeys is not null && _apiKeys.IsConfigured(AiProvider.DeepSeek) && _apiKeys.IsConfigured(AiProvider.Jev); }
        catch { return false; }
    }
    private Task<UsageFacts> BuildFactsAsync(DateOnly date, TimeOnly cutoff, CancellationToken ct = default)
    {
        var categories = _settings?.Current.Statistics.Categories.ToDictionary(p => p.Key, p => p.Value.ToString());
        return new UsageFactsService(DatabasePath, TimeZoneInfo.Local, snapshot: () => _usageCollector?.CaptureStatisticsSnapshot() ?? new(DateTimeOffset.UtcNow, null))
            .BuildAsync(date, cutoff, categories, ct);
    }
    private static string SerializeFacts(UsageFacts f, object? recentComparison = null) => JsonSerializer.Serialize(new
    {
        totalMinutes = Math.Round(f.TotalDuration.TotalMinutes, 2), f.ApplicationSwitchCount,
        longestContinuousMinutes = Math.Round(f.LongestContinuousDuration.TotalMinutes, 2),
        deepNightMinutes = Math.Round(f.DeepNightDuration.TotalMinutes, 2),
        categoryMinutes = f.CategoryDurations.ToDictionary(p => p.Key, p => Math.Round(p.Value.TotalMinutes, 2)),
        applications = f.Applications.OrderByDescending(a => a.Duration).Take(8).Select(a => new { process = a.ProcessName, minutes = Math.Round(a.Duration.TotalMinutes, 2) }),
        f.Interactions, f.AiSuccessCount, f.AiFailureCount, f.AiRetryCount, f.AiDisplayedCount, recentComparison
    });
    private async Task<ReportFacts> BuildReportFactsAsync(DateOnly date, DateTimeOffset cutoff, CancellationToken ct)
    {
        var facts = await BuildFactsAsync(date, DailyReportService.CutoffTime, ct);
        var supported = new List<string>();
        if (facts.Applications.Count > 0) supported.AddRange(["常用应用", "最长连续使用"]);
        if (facts.ApplicationSwitchCount > 0) supported.Add("窗口切换");
        if (facts.DeepNightDuration > TimeSpan.Zero) supported.Add("深夜使用");
        if (facts.Interactions.Values.Sum() > 0) supported.Add("桌宠互动");
        if (facts.CategoryDurations.GetValueOrDefault("Work") > TimeSpan.Zero) supported.Add("工作时间");
        if (facts.CategoryDurations.GetValueOrDefault("Entertainment") > TimeSpan.Zero) supported.Add("娱乐时间");
        if (supported.Contains("工作时间") || supported.Contains("娱乐时间")) supported.Add("工作/娱乐比例");
        var previous = await BuildFactsAsync(date.AddDays(-1), DailyReportService.CutoffTime, ct);
        object? comparison = null;
        if (previous.TotalDuration > TimeSpan.Zero && await _history!.IsStatisticsCompleteAsync(date.AddDays(-1), DailyReportService.CutoffTime, hasKnownUsage: true))
        {
            supported.Add("与近期变化");
            comparison = new { date = date.AddDays(-1).ToString("yyyy-MM-dd"), totalMinutes = Math.Round(previous.TotalDuration.TotalMinutes, 2),
                differenceMinutes = Math.Round((facts.TotalDuration - previous.TotalDuration).TotalMinutes, 2) };
        }
        await _statisticsTransition; await _eventWrites;
        var complete = facts.TotalDuration > TimeSpan.Zero && await _history!.IsStatisticsCompleteAsync(date, DailyReportService.CutoffTime, hasKnownUsage: true);
        return new(facts.TotalDuration.TotalMinutes, complete, SerializeFacts(facts, comparison), supported);
    }
    private async Task<AiOutcome> GenerateReportAsync(ReportPlan plan, CancellationToken ct)
    {
        if (_aiPipeline is null || _aiStore is null) return new(null, "Unavailable", 0, 0);
        var id = Guid.NewGuid(); var at = DateTimeOffset.UtcNow;
        return await Task.Run(async () =>
        {
            _aiStore.RecordRequest(id, at, "Pending", "", 0, 0, AiSceneKind.DailyReport);
            var recent = _reportsStore!.ReadAll().Where(r => r.Date < plan.Date && r.State == ReportState.Success && r.Plan is not null)
                .Select(r => new DisplayedContent(Guid.Empty, r.AtUtc, new ContentCandidate(r.Date.ToString(), r.Text, r.Plan!.Style), "", "")).ToArray();
            var scene = new AiScene("", "", "report", recent, AiSceneKind.DailyReport, plan.FactsJson,
                $"日期 {plan.Date:yyyy-MM-dd}，主题 {plan.Theme}，形式 {plan.Format}，风格 {plan.Style}。报告必须遵循此固定计划。", RequiredStyle: plan.Style);
            var result = await _aiPipeline.RunAsync(scene, candidates => _aiStore.AddCandidates(id, at, candidates), ct);
            ct.ThrowIfCancellationRequested();
            _aiStore.RecordRequest(id, at, result.Candidate is null ? "Failed" : "NotDisplayed", result.ErrorKind, result.GenerationAttempts, result.SelectionAttempts, AiSceneKind.DailyReport);
            return result;
        }, ct);
    }
    private void QueueFeatureCheck()
    {
        if (_featureInitializationFailed || _featureClosing || _maintenance || _settings is null || _reports is null) return;
        if (_featureCheck.IsCompleted) _featureCheck = CheckFeaturesAsync(_featureCancellation.Token);
        if (_reportCheck.IsCompleted) _reportCheck = CheckReportsAsync(_featureCancellation.Token);
    }
    private async Task CheckReportsAsync(CancellationToken ct)
    {
        try { await Task.Run(() => _reports!.CheckAsync(DateTimeOffset.UtcNow, ct), ct); await _history!.PruneAsync(DateTimeOffset.UtcNow); }
        catch (OperationCanceledException) { }
        catch { }
    }
    private async Task CheckFeaturesAsync(CancellationToken ct)
    {
        try
        {
            var pet = _petWindow!; var s = _settings!.Current; var now = DateTimeOffset.UtcNow;
            var process = _contextWatcher?.LastExternal?.ProcessName ?? "";
            var category = s.Statistics.Categories.GetValueOrDefault(process).ToString().ToLowerInvariant();
            var active = _usageCollector?.CaptureStatisticsSnapshot().ActiveSession;
            var enabled = new List<string>();
            if (s.Activity.PeekEnabled) enabled.Add("special-glance");
            if (s.Activity.ProudEnabled) enabled.Add("special-smile");
            if (s.Activity.FunnyFaceEnabled) enabled.Add("special-tongue");
            var context = new ActivityContext(now, TimeZoneInfo.Local, s.Activity.Enabled, pet.IsVisible,
                pet.Paused || _maintenance || _runtime.Locked || _runtime.Suspended, CheckFullscreen(), pet.HasBubble, pet.AiCoordinator?.IsBusy ?? false,
                s.Activity.QuietHoursEnabled, s.Activity.QuietHoursStart, s.Activity.QuietHoursEnd,
                process, s.Activity.Blacklist, enabled, category,
                active is null ? 0 : Math.Max(0, (now - active.StartedAt).TotalMinutes), s.Activity.Frequency.ToString(), (int)s.Activity.GlobalCooldown.TotalMinutes);
            var date = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, TimeZoneInfo.Local).DateTime);
            var action = await Task.Run(() =>
            {
                ct.ThrowIfCancellationRequested();
                if (_greetings!.CanTrigger(date, _activities!.CanTriggerGlobal(context))) return "greeting";
                var decision = _activities!.Evaluate(context);
                if (decision is null) return "";
                return decision.Animation;
            }, ct);
            ct.ThrowIfCancellationRequested();
            if (action.Length > 0 && pet.CanShowAi && !pet.HasBubble && !(pet.AiCoordinator?.IsBusy ?? false) &&
                ReferenceEquals(_settings.Current,s) && (_contextWatcher?.LastExternal?.ProcessName ?? "") == process &&
                !_maintenance && !_runtime.Locked && !_runtime.Suspended && !CheckFullscreen())
            {
                pet.PlayProactive(action); QueueRecord(action == "greeting" ? "Greeting" : "Activity", action);
                await Task.Run(() =>
                {
                    if (action == "greeting") _greetings!.MarkTriggered(date, now);
                    else _activities!.MarkTriggered(new ActivityDecision(action, ""), now);
                }, ct);
                var useAi = s.Ai.Enabled && (action == "greeting" ? s.Ai.GreetingEnabled : s.Ai.ActivityEnabled);
                if (useAi && await Task.Run(KeysReady, ct) && pet.CanShowAi && !pet.HasBubble)
                    _ = pet.AiCoordinator?.GenerateAsync(action == "greeting" ? AiSceneKind.Greeting : AiSceneKind.Activity, action);
            }
        }
        catch (OperationCanceledException) { }
        catch { /* Optional proactive features retry on the next check without exposing content. */ }
    }
    private void QueueRecord(string kind, string scene = "", DateTimeOffset? occurredAt = null)
    {
        var at = occurredAt ?? DateTimeOffset.UtcNow;
        lock (_recordGate)
        {
            if (_history is null || _featureInitializationFailed || _maintenance || _featureClosing) return;
            _eventWrites = _eventWrites.ContinueWith(_ => { try { _history.RecordInteraction(kind, at, scene); } catch { } }, TaskScheduler.Default);
        }
    }
    private void RecordBoundary(SessionPowerEvent kind, DateTimeOffset at)
    {
        switch (kind)
        {
            case SessionPowerEvent.Locked: _runtime.Locked = true; break;
            case SessionPowerEvent.Unlocked: _runtime.Locked = false; break;
            case SessionPowerEvent.Suspend: case SessionPowerEvent.Hibernate: _runtime.Suspended = true; break;
            case SessionPowerEvent.Resume: _runtime.Suspended = false; break;
        }
        if (_history is null || _maintenance || _featureClosing) return;
        if (kind is SessionPowerEvent.Locked or SessionPowerEvent.Suspend or SessionPowerEvent.Hibernate or SessionPowerEvent.Shutdown)
            QueueRecord(kind == SessionPowerEvent.Locked ? "Lock" : kind.ToString(), occurredAt: at);
    }
    private void QueueSavePlacement()
    {
        if (_placementStore is null || _featureClosing) return;
        var value = _petWindow?.CapturePlacement(); if (value is null) return;
        _placementSave = _placementSave.ContinueWith(async _ => { try { await _placementStore.SaveAsync(value); } catch { } }, TaskScheduler.Default).Unwrap();
    }
    private async Task ClearHistoryAsync()
    {
        if (_history is null || _maintenance || _featureClosing || _featureInitializationFailed) return;
        _maintenanceFinished = new(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_recordGate) _maintenance = true;
        _featureTimer?.Stop(); _featureCancellation.Cancel();
        try
        {
            if (_petWindow?.AiCoordinator is not null) await _petWindow.AiCoordinator.CancelAndDrainAsync();
            await _featureCheck; await _reportCheck; await _statisticsTransition;
            if (_reports is not null) await _reports.WaitForIdleAsync();
            await Task.Run(() => _usageCollector?.Stop());
            lock (_recordGate) { /* Freeze event chain after marking maintenance. */ }
            await _eventWrites;
            await _history.ClearAsync();
            _reportWindow?.Reload();
        }
        finally
        {
            _featureCancellation.Dispose(); _featureCancellation = new(); _maintenance = false;
            try
            {
                if (!_featureClosing && _settings?.Current.Statistics.Enabled == true) await Task.Run(() =>
                {
                    _usageCollector?.Start();
                    if (_runtime.Locked || _runtime.Suspended) _usageCollector?.OnSessionBoundary(_runtime.Locked ? SessionBoundary.Lock : SessionBoundary.Suspend);
                });
                if (!_featureClosing) _featureTimer?.Start();
            }
            finally { _maintenanceFinished.TrySetResult(); }
        }
    }
    private async Task ExportAsync(DateOnly from, DateOnly to, bool titles)
    {
        using var picker = new System.Windows.Forms.FolderBrowserDialog { Description = "选择统计 CSV 保存文件夹" };
        if (picker.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
        await _history!.ExportAsync(from, to, titles, picker.SelectedPath,
            _settings?.Current.Statistics.Categories.ToDictionary(p => p.Key,p => p.Value.ToString()));
    }
    private void OpenReport(DateOnly? date)
    {
        if (_reportsStore is null || _reports is null) return;
        if (_reportWindow is null)
        {
            _reportWindow = new(_reportsStore, async d =>
            {
                if (_maintenance || _featureClosing || _petWindow?.Paused == true) return;
                var ct = _featureCancellation.Token;
                await Task.Run(() => _reports.RetryAsync(d, ct), ct);
            }, date);
            _reportWindow.ReportDisplayed += report =>
            {
                if (_maintenance || _featureClosing || report.Plan is null || _aiStore is null) return;
                var id = new Guid(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes("report:" + report.Date.ToString("yyyy-MM-dd")))[..16]);
                lock (_recordGate)
                {
                    if (_maintenance || _featureClosing || _featureInitializationFailed) return;
                    _eventWrites = _eventWrites.ContinueWith(_ =>
                    {
                        try { _aiStore.RecordDisplayed(id, DateTimeOffset.UtcNow, new ContentCandidate(report.Date.ToString(), report.Text, report.Plan.Style), "", "", AiSceneKind.DailyReport); } catch { }
                    }, TaskScheduler.Default);
                }
            };
            _reportWindow.Closed += (_, _) => _reportWindow = null;
            _reportWindow.Show();
        }
        else { _reportWindow.Reload(date); if (_reportWindow.WindowState == WindowState.Minimized) _reportWindow.WindowState = WindowState.Normal; _reportWindow.Activate(); }
    }
    private async Task StopFeaturesAsync()
    {
        QueueSavePlacement();
        _featureClosing = true; _featureTimer?.Stop(); _fullscreenTimer?.Stop(); _featureCancellation.Cancel();
        if (_maintenanceFinished is not null) await _maintenanceFinished.Task;
        _sessionPowerEventSource?.Dispose();
        await Task.Run(() => _usageCollector?.Stop());
        await Task.Run(() => _contextWatcher?.Dispose());
        if (_petWindow?.AiCoordinator is not null) await _petWindow.AiCoordinator.CancelAndDrainAsync();
        await _featureCheck; await _reportCheck; await _statisticsTransition;
        if (_reports is not null) await _reports.WaitForIdleAsync();
        await _eventWrites; await _placementSave;
        _reportWindow?.Close(); _aiSettingsWindow?.Close();
    }
}

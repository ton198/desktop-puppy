using DesktopPuppy.Native;
using DesktopPuppy.Security;
using DesktopPuppy.Usage;

namespace DesktopPuppy.Ai;

public interface IRightClickAiView
{
    bool CanShowAi { get; }
    void ShowPlaceholder();
    void ShowAiText(string text);
    void RemovePlaceholder();
    void NotifyAi(string text);
}

// UI-thread confined gate; HTTP and SQLite work run off the Dispatcher.
public sealed class RightClickAiCoordinator : IDisposable
{
    private readonly AiSqliteStore _store;
    private readonly ApiKeyService _keys;
    private readonly AiPipeline _pipeline;
    private readonly IRightClickAiView _view;
    private readonly Func<ForegroundWindowSnapshot?> _context;
    private readonly Func<DateTimeOffset> _utc;
    private readonly Func<TimeSpan> _elapsed;
    private readonly SingleBubbleRequestGate _gate = new();
    private CancellationTokenSource _shutdown = new();
    private Task _pending = Task.CompletedTask;
    public bool IsBusy => !_pending.IsCompleted;
    public Func<CancellationToken, Task<string>>? FactsProvider { get; set; }
    public Func<CancellationToken, Task<IReadOnlyList<string>>>? RecentTitlesProvider { get; set; }
    public Task RightClickAsync() => GenerateAsync(AiSceneKind.RightClick);
    public Task GenerateAsync(AiSceneKind kind, string detail = "")
    {
        if (IsBusy || _disposed) return Task.CompletedTask;
        _pending = RunSceneAsync(kind, detail);
        return _pending;
    }
    public async Task CancelAndDrainAsync()
    {
        _shutdown.Cancel();
        InvalidateBubble();
        try { await _pending; } catch (OperationCanceledException) { }
        _shutdown.Dispose();
        if (!_disposed) _shutdown = new CancellationTokenSource();
    }
    private DateTimeOffset? _lastRequestAt;
    private string? _lastTitle;
    private string? _lastApplication;
    private bool _disposed;
    private bool _placeholderShown;

    public RightClickAiCoordinator(AiSqliteStore store, ApiKeyService keys, AiPipeline pipeline, IRightClickAiView view, Func<ForegroundWindowSnapshot?> context, Func<DateTimeOffset> utc, Func<TimeSpan> elapsed)
    { _store = store; _keys = keys; _pipeline = pipeline; _view = view; _context = context; _utc = utc; _elapsed = elapsed; }
    public void InvalidateBubble()
    {
        if (_gate.Invalidate() && _placeholderShown) _view.RemovePlaceholder();
        _placeholderShown = false;
    }
    public void Advance()
    {
        if (_gate.Expire(_elapsed()))
        {
            if (_placeholderShown) _view.RemovePlaceholder();
            _placeholderShown = false;
        }
    }
    private bool CanNotify(Guid id) => !_disposed && _gate.CanDisplay(id, _elapsed(), _view.CanShowAi);
    private async Task RunSceneAsync(AiSceneKind kind, string detail)
    {
        if (_disposed || !_view.CanShowAi) return;
        var id = _gate.TryBegin(_elapsed()); if (id is null) return;
        var at = _utc(); var snapshot = _context();
        var application = Limit(snapshot?.ProcessName ?? "", 128); var title = Limit(snapshot?.WindowTitle ?? "", 512);
        var local = TimeZoneInfo.ConvertTime(at, TimeZoneInfo.Local); var night = local.Hour >= 23 || local.Hour < 8;
        var generation = 0; var selection = 0;
        try
        {
            var loaded = await Task.Run(() =>
            {
                _store.PruneBefore(UsageRetentionPolicy.GetCutoffUtc(at, TimeZoneInfo.Local));
                var settings = _store.LoadSettings();
                // Readability is checked without retaining the plaintext in coordinator state.
                var configured = !settings.Enabled || (!string.IsNullOrEmpty(_keys.Get(AiProvider.DeepSeek)) && !string.IsNullOrEmpty(_keys.Get(AiProvider.Jev)));
                return (settings, configured, recent: _store.ReadDisplayed());
            }, _shutdown.Token);
            var sceneEnabled = kind switch { AiSceneKind.RightClick => loaded.settings.RightClickEnabled,
                AiSceneKind.Greeting => loaded.settings.GreetingEnabled, AiSceneKind.Activity => loaded.settings.ActivityEnabled, _ => true };
            if (!loaded.settings.Enabled || !sceneEnabled) return;
            if (!loaded.configured) { if (CanNotify(id.Value)) _view.NotifyAi("请在 AI 设置中配置 DeepSeek 和 Jev Key。"); return; }
            var changed = application != _lastApplication || ContentValidator.Normalize(title) != _lastTitle;
            var similar = loaded.recent.Any(h => h.Application == application && ContentValidator.Similarity(h.Title, title) >= .75);
            var eligible = changed || !similar || !_lastRequestAt.HasValue || at - _lastRequestAt.Value >= TimeSpan.FromMinutes(5) || night;
            if ((kind == AiSceneKind.RightClick && !eligible) || !_gate.CanDisplay(id.Value, _elapsed(), _view.CanShowAi)) return;
            _lastRequestAt = at; _lastTitle = ContentValidator.Normalize(title); _lastApplication = application;
            var facts = FactsProvider is null ? "" : await FactsProvider(_shutdown.Token);
            var recentTitles = RecentTitlesProvider is null ? null : await RecentTitlesProvider(_shutdown.Token);
            if (!_gate.CanDisplay(id.Value, _elapsed(), _view.CanShowAi)) return;
            var scene = new AiScene(application, title, night ? "night" : "day", loaded.recent, kind, facts, detail, RecentTitles: recentTitles);
            _placeholderShown = true;
            _view.ShowPlaceholder();
            await Task.Run(() => _store.RecordRequest(id.Value, at, "Pending", "", 0, 0, kind), _shutdown.Token);
            var outcome = await Task.Run(() => _pipeline.RunAsync(scene, candidates => _store.AddCandidates(id.Value, at, candidates), _shutdown.Token), _shutdown.Token);
            generation = outcome.GenerationAttempts; selection = outcome.SelectionAttempts;
            if (outcome.Candidate is null)
            {
                var notify = CanNotify(id.Value);
                InvalidateBubble();
                if (notify) _view.NotifyAi("AI 台词生成失败，请稍后再试。");
                await Task.Run(() => _store.RecordRequest(id.Value, at, "Failed", outcome.ErrorKind, generation, selection, kind), _shutdown.Token);
                return;
            }
            bool displayed = _gate.CanDisplay(id.Value, _elapsed(), _view.CanShowAi);
            if (displayed) { _view.ShowAiText(outcome.Candidate.Text); _gate.Invalidate(); _placeholderShown = false; }
            else InvalidateBubble();
            await Task.Run(() =>
            {
                _store.RecordRequest(id.Value, at, displayed ? "Displayed" : "NotDisplayed", "", generation, selection, kind);
                if (displayed) _store.RecordDisplayed(id.Value, _utc(), outcome.Candidate, application, title, kind);
            }, _shutdown.Token);
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested) { /* Shutdown has no notification. */ }
        catch (ApiKeyUnavailableException)
        {
            if (CanNotify(id.Value)) _view.NotifyAi("API Key 无法解密，请在 AI 设置中重新保存。");
        }
        catch
        {
            if (CanNotify(id.Value)) _view.NotifyAi("AI 服务或存储暂时不可用。");
            try { await Task.Run(() => _store.RecordRequest(id.Value, at, "Failed", "Unavailable", generation, selection, kind)); } catch { }
        }
        finally
        {
            InvalidateBubble();
            _gate.Complete(id.Value);
        }
    }
    private static string Limit(string value, int length) => value.Length <= length ? value : value[..length];
    public void Dispose() { if (_disposed) return; _disposed = true; InvalidateBubble(); _shutdown.Cancel(); /* Pending operations still observe this token. */ }
}

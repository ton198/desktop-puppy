using System.Text;
using DesktopPuppy.Ai;
using DesktopPuppy.Native;
using DesktopPuppy.Security;
using Xunit;

namespace DesktopPuppy.Tests.Ai;

public sealed class RightClickAiCoordinatorTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "puppy-coordinator-" + Guid.NewGuid());
    private TimeSpan _now;
    private readonly View _view = new();
    private readonly Content _content = new();
    private readonly Decision _decision = new();
    private AiSqliteStore? _store;
    private RightClickAiCoordinator Create(bool configured = true)
    {
        _store = new(Path.Combine(_directory, "test.db")); _store.Initialize();
        var keys = new ApiKeyService(_store, new Protector());
        if (configured) { keys.Save(AiProvider.DeepSeek, Guid.NewGuid().ToString()); keys.Save(AiProvider.Jev, Guid.NewGuid().ToString()); }
        return new(_store, keys, new(_content, _decision, new NoDelay()), _view, () => new(1, 2, "editor", "test"), () => DateTimeOffset.UtcNow, () => _now);
    }
    [Fact]
    public async Task Local_replacement_during_fact_loading_keeps_its_bubble()
    {
        using var coordinator=Create();
        var factsStarted=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var factsFinish=new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        coordinator.FactsProvider=async ct=>{factsStarted.TrySetResult(true);return await factsFinish.Task.WaitAsync(ct);};
        var task=coordinator.GenerateAsync(AiSceneKind.UserRequested);
        await factsStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        coordinator.InvalidateBubble();_view.Text="local replacement";
        factsFinish.SetResult("{}");_decision.Finish.TrySetResult(true);await task;
        Assert.Equal("local replacement",_view.Text);
        Assert.False(_view.Placeholder);
        Assert.Equal(0,_content.Calls);
    }
    [Fact]
    public async Task Active_scene_can_be_cancelled_and_drained_before_history_clear()
    {
        using var coordinator = Create();
        var task = coordinator.GenerateAsync(AiSceneKind.UserRequested);
        await _decision.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(coordinator.IsBusy);
        await coordinator.CancelAndDrainAsync();
        await task;
        Assert.False(coordinator.IsBusy);
        Assert.Null(_view.Text);
        Assert.Equal("", _view.Notice);
    }
    [Fact]
    public async Task Disabled_greeting_ai_does_not_request_content()
    {
        using var coordinator = Create();
        _store!.SaveSettings(new AiConfiguration(GreetingEnabled: false));
        await coordinator.GenerateAsync(AiSceneKind.Greeting);
        Assert.Equal(0, _content.Calls);
        Assert.False(_view.Placeholder);
    }
    [Fact]
    public async Task Successful_display_survives_final_cleanup_and_enters_displayed_history()
    {
        using var coordinator = Create(); var task = coordinator.RightClickAsync();
        await _decision.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        _decision.Finish.SetResult(true); await task;
        Assert.NotNull(_view.Text); Assert.False(_view.Placeholder); Assert.Single(_store!.ReadDisplayed());
    }
    [Fact]
    public async Task Greeting_records_its_own_scene_in_request_history()
    {
        using var coordinator=Create();
        var task=coordinator.GenerateAsync(AiSceneKind.Greeting);
        await _decision.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        _decision.Finish.SetResult(true);await task;
        using var db=new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={Path.Combine(_directory,"test.db")};Pooling=false");db.Open();
        using var q=db.CreateCommand();q.CommandText="SELECT scene_kind FROM ai_requests";
        Assert.Equal("Greeting",q.ExecuteScalar());
    }
    [Fact]
    public async Task Expired_result_is_recorded_without_display_and_repeated_click_does_not_queue()
    {
        using var coordinator = Create(); var task = coordinator.RightClickAsync();
        await _decision.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        _now = TimeSpan.FromSeconds(15); coordinator.Advance(); await coordinator.RightClickAsync();
        Assert.False(_view.Placeholder); Assert.Equal(1, _content.Calls);
        _decision.Finish.SetResult(true); await task;
        Assert.Null(_view.Text); Assert.Empty(_store!.ReadDisplayed()); Assert.True(_store.HistoryCount() > 0);
    }
    [Fact]
    public async Task Replacing_or_closing_bubble_prevents_late_result()
    {
        using var coordinator = Create(); var task = coordinator.RightClickAsync();
        await _decision.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        coordinator.InvalidateBubble(); _view.Text = "local interaction";
        _decision.Finish.SetResult(true); await task;
        Assert.Equal("local interaction", _view.Text); Assert.Empty(_store!.ReadDisplayed());
    }
    [Fact]
    public async Task Hidden_result_is_not_displayed_or_added_to_repetition_history()
    {
        using var coordinator = Create(); var task = coordinator.RightClickAsync();
        await _decision.Started.Task.WaitAsync(TimeSpan.FromSeconds(5)); _view.CanShowAi = false;
        _decision.Finish.SetResult(true); await task;
        Assert.Null(_view.Text); Assert.Empty(_store!.ReadDisplayed());
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Late_failure_after_expiry_or_replacement_does_not_notify(bool replaced)
    {
        using var coordinator = Create(); _decision.Fail = true; var task = coordinator.RightClickAsync();
        await _decision.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        if (replaced) coordinator.InvalidateBubble(); else { _now = TimeSpan.FromSeconds(15); coordinator.Advance(); }
        _decision.Finish.SetResult(true); await task;
        Assert.Equal("", _view.Notice); Assert.Empty(_store!.ReadDisplayed());
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Preflight_without_placeholder_preserves_existing_bubble(bool disabled)
    {
        using var coordinator = Create(false); _view.Text = "existing interaction";
        if (disabled) _store!.SaveSettings(new(false, true));
        await coordinator.RightClickAsync(); Assert.Equal("existing interaction", _view.Text);
    }
    [Fact]
    public async Task Missing_keys_only_notify_and_never_call_provider()
    {
        using var coordinator = Create(false); await coordinator.RightClickAsync();
        Assert.Contains("配置", _view.Notice); Assert.Equal(0, _content.Calls); Assert.False(_view.Placeholder);
    }
    [Fact]
    public async Task Disabled_ai_does_not_notify_or_call_provider()
    {
        using var coordinator = Create(); _store!.SaveSettings(new(false, true)); await coordinator.RightClickAsync();
        Assert.Equal("", _view.Notice); Assert.Equal(0, _content.Calls);
    }
    [Fact]
    public async Task Exit_cancels_in_flight_selection_without_failure_notification()
    {
        using var coordinator = Create(); var task = coordinator.RightClickAsync();
        await _decision.Started.Task.WaitAsync(TimeSpan.FromSeconds(5)); coordinator.Dispose(); await task;
        Assert.Equal("", _view.Notice); Assert.Null(_view.Text); Assert.False(_view.Placeholder);
    }
    private sealed class Protector : IApiKeyProtector { public byte[] Protect(string value) => Encoding.UTF8.GetBytes(value); public string Unprotect(byte[] value) => Encoding.UTF8.GetString(value); }
    [Fact]
    public async Task Scene_receives_bounded_recent_title_context_without_replacing_current_title()
    {
        using var coordinator=Create();
        coordinator.RecentTitlesProvider=_=>Task.FromResult<IReadOnlyList<string>>(["recent document"]);
        var pending=coordinator.RightClickAsync();
        await _decision.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("recent document",Assert.Single(_content.LastScene!.RecentTitles!));
        Assert.NotEqual("recent document",_content.LastScene.Title);
        _decision.Finish.TrySetResult(true);await pending;
    }
    private sealed class Content : IAiContentProvider
    {
        public int Calls;
        public AiScene? LastScene;
        public Task<IReadOnlyList<ContentCandidate>> GenerateAsync(AiScene scene, IReadOnlyList<ContentCandidate> existing, int variation, CancellationToken ct)
        { Calls++; LastScene=scene; return Task.FromResult<IReadOnlyList<ContentCandidate>>(AiPipelineTests.Candidates); }
    }
    private sealed class Decision : IJevDecisionProvider
    {
        public bool Fail;
        public TaskCompletionSource<bool> Started = new(TaskCreationOptions.RunContinuationsAsynchronously), Finish = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<JevDecision> SelectAsync(AiScene scene, IReadOnlyList<ContentCandidate> candidates, CancellationToken ct)
        { Started.TrySetResult(true); await Finish.Task.WaitAsync(ct); if (Fail) throw new AiProviderException("Http"); return new(candidates[0].Id, .9, candidates.ToDictionary(c => c.Id, c => c == candidates[0] ? .9 : .02)); }
    }
    private sealed class NoDelay : IAiDelay { public Task WaitAsync(TimeSpan duration, CancellationToken ct) { ct.ThrowIfCancellationRequested(); return Task.CompletedTask; } }
    private sealed class View : IRightClickAiView
    {
        public bool CanShowAi { get; set; } = true; public bool Placeholder; public string? Text; public string Notice = "";
        public void ShowPlaceholder() => Placeholder = true;
        public void ShowAiText(string text) { Text = text; Placeholder = false; }
        public void RemovePlaceholder() { Placeholder = false; Text = null; }
        public void NotifyAi(string text) => Notice = text;
    }
    public void Dispose() { if (Directory.Exists(_directory)) Directory.Delete(_directory, true); }
}

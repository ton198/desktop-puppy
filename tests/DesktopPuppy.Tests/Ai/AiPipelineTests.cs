using DesktopPuppy.Ai;
using Xunit;

namespace DesktopPuppy.Tests.Ai;

public sealed class AiPipelineTests
{
    public static ContentCandidate[] Candidates => [
        new("a","键盘今天很辛苦，我替它申请半分钟休息。","tease"),
        new("b","你的鼠标正在逃跑，快给它发一张通缉令。","absurd"),
        new("c","这页内容如此严肃，我决定先坐下装专家。","deadpan"),
        new("d","屏幕亮得像舞台，而我才是今天的主角。","dramatic"),
        new("e","别急着关窗口，本检查员还没盖狗爪印。","mock-serious"),
        new("f","你负责编辑文字，我负责把气氛搅得乱七八糟。","wordplay")];
    private static AiScene Scene => new("editor", "test", "day", []);

    [Theory]
    [InlineData("")]
    [InlineData("短")]
    [InlineData("您好，我是人工智能助手，很高兴为您服务。")]
    [InlineData("这是一条**带有格式**的长一点的台词。")]
    [InlineData("这是第一段台词。\n这是第二段台词。")]
    [InlineData("今天的键盘真辛苦！！！快让它休息一下。")]
    public void Validator_rejects_invalid_content(string text) => Assert.False(new ContentValidator().IsValid(new("a", text, "tease"), [], []));

    [Fact]
    public void Validator_rejects_duplicate_and_consecutive_style()
    {
        var v = new ContentValidator(); var c = Candidates[0];
        Assert.True(v.IsValid(c, [], []));
        Assert.False(v.IsValid(c, [c], []));
        Assert.False(v.IsValid(Candidates[1], [], [new(Guid.NewGuid(), DateTimeOffset.UtcNow, Candidates[1], "editor", "test")]));
    }

    [Fact]
    public async Task Four_valid_candidates_select_once_without_regenerating()
    {
        var content = new ContentFake(); var decision = new DecisionFake(); var delay = new DelayFake();
        var result = await new AiPipeline(content, decision, delay).RunAsync(Scene, _ => { }, CancellationToken.None);
        Assert.NotNull(result.Candidate); Assert.Equal(1, content.Calls); Assert.Equal(1, decision.Calls); Assert.Empty(delay.Seconds);
    }

    [Fact]
    public async Task Generation_exhaustion_has_exact_backoff_and_no_selector()
    {
        var content = new ContentFake { Fail = true }; var decision = new DecisionFake(); var delay = new DelayFake();
        var result = await new AiPipeline(content, decision, delay).RunAsync(Scene, _ => { }, CancellationToken.None);
        Assert.Null(result.Candidate); Assert.Equal(5, content.Calls); Assert.Equal(0, decision.Calls); Assert.Equal(new[] { 1d, 2d, 4d, 8d }, delay.Seconds);
    }

    [Fact]
    public async Task Jev_failure_retries_only_selection()
    {
        var content = new ContentFake(); var decision = new DecisionFake { Fail = true }; var delay = new DelayFake();
        var result = await new AiPipeline(content, decision, delay).RunAsync(Scene, _ => { }, CancellationToken.None);
        Assert.Null(result.Candidate); Assert.Equal(1, content.Calls); Assert.Equal(5, decision.Calls); Assert.Equal(new[] { 1d, 2d, 4d, 8d }, delay.Seconds);
    }

    [Fact]
    public async Task Low_probability_uses_local_tie_break_without_retry()
    {
        var decision = new DecisionFake { Low = true };
        var result = await new AiPipeline(new ContentFake(), decision, new DelayFake()).RunAsync(Scene, _ => { }, CancellationToken.None);
        Assert.NotNull(result.Candidate); Assert.Equal(1, decision.Calls);
        Assert.NotEqual(Candidates[5].Text, result.Candidate!.Text);
    }

    [Fact]
    public async Task Cancellation_does_not_become_failure_or_retry()
    {
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new AiPipeline(new ContentFake(), new DecisionFake(), new DelayFake()).RunAsync(Scene, _ => { }, cancel.Token));
    }
    [Fact]
    public async Task Partial_candidates_accumulate_and_each_batch_receives_existing_pool()
    {
        var provider = new PartialContent(); var decision = new DecisionFake();
        var result = await new AiPipeline(provider, decision, new DelayFake()).RunAsync(Scene, _ => { }, CancellationToken.None);
        Assert.NotNull(result.Candidate); Assert.Equal(new[] { 0, 2 }, provider.ExistingCounts); Assert.Equal(new[] { 1, 2 }, provider.Variations);
    }
    [Fact]
    public async Task Real_generation_timeout_cancels_attempt_then_next_attempt_can_succeed()
    {
        var provider = new TimeoutContent();
        var result = await new AiPipeline(provider, new DecisionFake(), new DelayFake()).RunAsync(Scene, _ => { }, CancellationToken.None);
        Assert.NotNull(result.Candidate); Assert.True(provider.TimeoutObserved); Assert.Equal(2, result.GenerationAttempts);
    }
    [Fact]
    public async Task Invalid_probability_distribution_retries_selection_without_regeneration()
    {
        var content = new ContentFake(); var decision = new DecisionFake { Invalid = true };
        var result = await new AiPipeline(content, decision, new DelayFake()).RunAsync(Scene, _ => { }, CancellationToken.None);
        Assert.Null(result.Candidate); Assert.Equal(1, content.Calls); Assert.Equal(5, decision.Calls);
    }
    private sealed class PartialContent : IAiContentProvider
    {
        public List<int> ExistingCounts = [], Variations = [];
        public Task<IReadOnlyList<ContentCandidate>> GenerateAsync(AiScene scene, IReadOnlyList<ContentCandidate> existing, int variation, CancellationToken ct)
        {
            ExistingCounts.Add(existing.Count); Variations.Add(variation);
            return Task.FromResult<IReadOnlyList<ContentCandidate>>(variation == 1 ? [Candidates[0], Candidates[1], new("x", "短", "tease"), new("x", "短", "tease"), new("x", "短", "tease"), new("x", "短", "tease")] : Candidates);
        }
    }
    private sealed class TimeoutContent : IAiContentProvider
    {
        public bool TimeoutObserved;
        public async Task<IReadOnlyList<ContentCandidate>> GenerateAsync(AiScene scene, IReadOnlyList<ContentCandidate> existing, int variation, CancellationToken ct)
        {
            if (variation == 1) { try { await Task.Delay(Timeout.InfiniteTimeSpan, ct); } catch (OperationCanceledException) { TimeoutObserved = true; throw; } }
            return Candidates;
        }
    }

    private sealed class ContentFake : IAiContentProvider
    {
        public bool Fail; public int Calls;
        public Task<IReadOnlyList<ContentCandidate>> GenerateAsync(AiScene scene, IReadOnlyList<ContentCandidate> existing, int variation, CancellationToken ct)
        { Calls++; if (Fail) throw new AiProviderException("Json"); return Task.FromResult<IReadOnlyList<ContentCandidate>>(Candidates); }
    }
    private sealed class DecisionFake : IJevDecisionProvider
    {
        public bool Fail, Low, Invalid; public int Calls;
        public Task<JevDecision> SelectAsync(AiScene scene, IReadOnlyList<ContentCandidate> candidates, CancellationToken ct)
        { Calls++; if (Fail) throw new AiProviderException("Http"); return Task.FromResult(new JevDecision(candidates[^1].Id, Low ? .3 : .9, candidates.ToDictionary(c => c.Id, c => Invalid ? double.NaN : c == candidates[^1] ? (Low ? .3 : .9) : (Low ? .14 : .02)))); }
    }
    private sealed class DelayFake : IAiDelay { public List<double> Seconds = []; public Task WaitAsync(TimeSpan duration, CancellationToken ct) { Seconds.Add(duration.TotalSeconds); ct.ThrowIfCancellationRequested(); return Task.CompletedTask; } }
}

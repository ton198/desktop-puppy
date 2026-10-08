namespace DesktopPuppy.Ai;

public enum AiProvider { DeepSeek, Jev }
public sealed record AiConfiguration(bool Enabled = true, bool RightClickEnabled = true);
public sealed record ContentCandidate(string Id, string Text, string Style);
public sealed record DisplayedContent(Guid RequestId, DateTimeOffset AtUtc, ContentCandidate Candidate, string Application, string Title);
public sealed record AiScene(string Application, string Title, string TimeBand, IReadOnlyList<DisplayedContent> Recent);
public sealed record JevDecision(string CandidateId, double Confidence, IReadOnlyDictionary<string, double> Probabilities);
public sealed record AiOutcome(ContentCandidate? Candidate, string ErrorKind, int GenerationAttempts, int SelectionAttempts);
public sealed class AiProviderException(string kind) : Exception("AI provider request failed.") { public string Kind { get; } = kind; }
public interface IAiContentProvider
{
    Task<IReadOnlyList<ContentCandidate>> GenerateAsync(AiScene scene, IReadOnlyList<ContentCandidate> existing, int variation, CancellationToken cancellationToken);
}
public interface IJevDecisionProvider
{
    Task<JevDecision> SelectAsync(AiScene scene, IReadOnlyList<ContentCandidate> candidates, CancellationToken cancellationToken);
}
public interface IAiDelay { Task WaitAsync(TimeSpan duration, CancellationToken cancellationToken); }
public sealed class AiDelay : IAiDelay { public Task WaitAsync(TimeSpan duration, CancellationToken cancellationToken) => Task.Delay(duration, cancellationToken); }

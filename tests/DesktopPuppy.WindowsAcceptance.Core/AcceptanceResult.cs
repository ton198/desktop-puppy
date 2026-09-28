namespace DesktopPuppy.WindowsAcceptance.Core;

public enum AcceptanceStatus { Passed, Failed, Incomplete, Skipped }
public enum ObservedSystemEventKind { Locked, Unlocked, Suspend, Resume }
public sealed record AcceptanceResult(AcceptanceStatus Status, string Summary, IReadOnlyList<ObservedSystemEvent> ObservedEvents, IReadOnlyList<string> SafeDiagnostics);
public sealed record ObservedSystemEvent(ObservedSystemEventKind Kind, DateTimeOffset At);
public sealed record ObservedUsageSession(uint ProcessId, DateTimeOffset StartedAt, DateTimeOffset EndedAt);
public sealed record FixtureProcessIds(uint FixtureAProcessId, uint FixtureBProcessId);

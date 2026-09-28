namespace DesktopPuppy.WindowsAcceptance.Core;

public sealed class SessionBoundaryValidator
{
    public AcceptanceResult ValidateLockUnlock(IReadOnlyList<ObservedSystemEvent> events,
        IReadOnlyList<ObservedUsageSession> sessions, FixtureProcessIds fixtureProcessIds,
        DateTimeOffset operatorCompletedAt, bool operatorConfirmed) =>
        Validate(events, sessions, fixtureProcessIds, operatorCompletedAt, operatorConfirmed,
            ObservedSystemEventKind.Locked, ObservedSystemEventKind.Unlocked);

    public AcceptanceResult ValidateSuspendResume(IReadOnlyList<ObservedSystemEvent> events,
        IReadOnlyList<ObservedUsageSession> sessions, FixtureProcessIds fixtureProcessIds,
        DateTimeOffset operatorCompletedAt, bool operatorConfirmed) =>
        Validate(events, sessions, fixtureProcessIds, operatorCompletedAt, operatorConfirmed,
            ObservedSystemEventKind.Suspend, ObservedSystemEventKind.Resume);

    private static AcceptanceResult Validate(IReadOnlyList<ObservedSystemEvent> events,
        IReadOnlyList<ObservedUsageSession> sessions, FixtureProcessIds ids,
        DateTimeOffset completed, bool confirmed, ObservedSystemEventKind before, ObservedSystemEventKind after)
    {
        AcceptanceResult Result(AcceptanceStatus status, string summary) =>
            new(status, summary, events.ToArray(), Array.Empty<string>());

        if (!confirmed) return Result(AcceptanceStatus.Incomplete, "Operator did not confirm the manual step.");
        var relevant = events.Where(e => e.Kind == before || e.Kind == after).ToArray();
        if (relevant.Length != 2 || relevant[0].Kind != before || relevant[1].Kind != after ||
            relevant[0].At >= relevant[1].At || relevant[1].At >= completed)
            return Result(AcceptanceStatus.Failed, "Required ordered system event pair was not observed before completion.");

        var start = relevant[0].At;
        var end = relevant[1].At;
        if (ids.FixtureAProcessId == 0 || ids.FixtureBProcessId == 0 || ids.FixtureAProcessId == ids.FixtureBProcessId ||
            sessions.Any(s => s.StartedAt >= s.EndedAt || s.EndedAt > completed ||
                (s.StartedAt < end && s.EndedAt > start)))
            return Result(AcceptanceStatus.Failed, "Invalid or overlapping usage session.");

        var pre = sessions.Any(s => s.ProcessId == ids.FixtureAProcessId && s.StartedAt < start && s.EndedAt == start);
        var resumed = sessions.Where(s => s.ProcessId == ids.FixtureAProcessId && s.StartedAt >= end && s.EndedAt < completed).ToArray();
        var closedByB = resumed.Any(a => sessions.Any(b => b.ProcessId == ids.FixtureBProcessId &&
            b.StartedAt >= a.EndedAt && b.StartedAt < b.EndedAt && b.EndedAt <= completed));
        return pre && closedByB
            ? Result(AcceptanceStatus.Passed, "Ordered events and closed fixture sessions verified.")
            : Result(AcceptanceStatus.Failed, "Required fixture A and B session edges were not observed.");
    }
}

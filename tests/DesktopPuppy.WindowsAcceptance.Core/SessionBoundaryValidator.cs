namespace DesktopPuppy.WindowsAcceptance.Core;

public sealed class SessionBoundaryValidator
{
    private static readonly TimeSpan LockBoundaryTolerance = TimeSpan.FromSeconds(5);

    public AcceptanceResult ValidateLockUnlock(IReadOnlyList<ObservedSystemEvent> events,
        IReadOnlyList<ObservedUsageSession> sessions, FixtureProcessIds fixtureProcessIds,
        DateTimeOffset operatorCompletedAt, bool operatorConfirmed)
    {
        AcceptanceResult Result(AcceptanceStatus status, string summary) =>
            new(status, summary, events.ToArray(), Array.Empty<string>());

        if (!operatorConfirmed)
            return Result(AcceptanceStatus.Incomplete, "Operator did not confirm the manual lock/unlock flow.");

        var relevant = events.Where(e => e.Kind is ObservedSystemEventKind.Locked or ObservedSystemEventKind.Unlocked).ToArray();
        if (relevant.Length != 2 || relevant[0].Kind != ObservedSystemEventKind.Locked ||
            relevant[1].Kind != ObservedSystemEventKind.Unlocked || relevant[0].At >= relevant[1].At ||
            relevant[1].At >= operatorCompletedAt)
            return Result(AcceptanceStatus.Failed, "Required ordered lock/unlock notifications were not observed before completion.");

        var lockedAt = relevant[0].At;
        var unlockedAt = relevant[1].At;
        if (fixtureProcessIds.FixtureAProcessId == 0 || fixtureProcessIds.FixtureBProcessId == 0 ||
            fixtureProcessIds.FixtureAProcessId == fixtureProcessIds.FixtureBProcessId ||
            sessions.Any(s => s.StartedAt >= s.EndedAt || s.EndedAt > operatorCompletedAt))
            return Result(AcceptanceStatus.Failed, "Invalid closed fixture usage session.");

        var fixtureSessions = sessions.Where(s => s.ProcessId == fixtureProcessIds.FixtureAProcessId ||
            s.ProcessId == fixtureProcessIds.FixtureBProcessId).ToArray();
        if (fixtureSessions.Any(s => Overlap(s.StartedAt, s.EndedAt, lockedAt, unlockedAt) > LockBoundaryTolerance))
            return Result(AcceptanceStatus.Failed, "A fixture usage session spans too much of the locked interval.");

        var preLock = fixtureSessions.Where(s => s.ProcessId == fixtureProcessIds.FixtureAProcessId &&
            s.StartedAt < lockedAt && WithinTolerance(s.EndedAt, lockedAt)).ToArray();
        var resumedA = fixtureSessions.Where(s => s.ProcessId == fixtureProcessIds.FixtureAProcessId &&
            s.StartedAt >= unlockedAt - LockBoundaryTolerance && s.EndedAt < operatorCompletedAt).ToArray();
        var fixtureB = fixtureSessions.Where(s => s.ProcessId == fixtureProcessIds.FixtureBProcessId &&
            s.StartedAt < s.EndedAt && s.EndedAt >= operatorCompletedAt - LockBoundaryTolerance &&
            s.EndedAt <= operatorCompletedAt).ToArray();
        var closedBySwitch = resumedA.Any(a => fixtureB.Any(b => b.StartedAt >= a.EndedAt &&
            b.StartedAt - a.EndedAt <= LockBoundaryTolerance && b.StartedAt < operatorCompletedAt));

        return preLock.Length > 0 && resumedA.Length > 0 && fixtureB.Length > 0 && closedBySwitch
            ? Result(AcceptanceStatus.Passed, "Real lock/unlock notifications and closed fixture session boundaries verified.")
            : Result(AcceptanceStatus.Failed, "Required pre-lock, post-unlock fixture A, and fixture B session edges were not observed.");
    }

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

    private static bool WithinTolerance(DateTimeOffset left, DateTimeOffset right) =>
        (left - right).Duration() <= LockBoundaryTolerance;

    private static TimeSpan Overlap(DateTimeOffset firstStart, DateTimeOffset firstEnd,
        DateTimeOffset secondStart, DateTimeOffset secondEnd)
    {
        var start = firstStart > secondStart ? firstStart : secondStart;
        var end = firstEnd < secondEnd ? firstEnd : secondEnd;
        return end > start ? end - start : TimeSpan.Zero;
    }
}

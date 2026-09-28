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

    private static readonly TimeSpan SuspendBoundaryTolerance = TimeSpan.FromSeconds(15);

    public AcceptanceResult ValidateSuspendResume(IReadOnlyList<ObservedSystemEvent> events,
        IReadOnlyList<ObservedUsageSession> sessions, FixtureProcessIds fixtureProcessIds,
        DateTimeOffset operatorCompletedAt, bool operatorConfirmed)
    {
        AcceptanceResult Result(AcceptanceStatus status, string summary) =>
            new(status, summary, events.ToArray(), Array.Empty<string>());

        if (!operatorConfirmed)
            return Result(AcceptanceStatus.Incomplete, "Operator did not confirm the manual suspend/resume flow.");

        var relevant = events.Where(e => e.Kind is ObservedSystemEventKind.Suspend or ObservedSystemEventKind.Resume).ToArray();
        if (relevant.Length != 2 || relevant[0].Kind != ObservedSystemEventKind.Suspend ||
            relevant[1].Kind != ObservedSystemEventKind.Resume || relevant[0].At >= relevant[1].At ||
            relevant[1].At >= operatorCompletedAt)
            return Result(AcceptanceStatus.Failed, "Required ordered Suspend then Resume notifications were not observed before completion.");

        var suspendAt = relevant[0].At;
        var resumeAt = relevant[1].At;
        if (fixtureProcessIds.FixtureAProcessId == 0 || fixtureProcessIds.FixtureBProcessId == 0 ||
            fixtureProcessIds.FixtureAProcessId == fixtureProcessIds.FixtureBProcessId ||
            sessions.Any(s => s.StartedAt >= s.EndedAt || s.EndedAt > operatorCompletedAt))
            return Result(AcceptanceStatus.Failed, "Invalid closed fixture usage session.");

        var fixtureSessions = sessions.Where(s => s.ProcessId == fixtureProcessIds.FixtureAProcessId ||
            s.ProcessId == fixtureProcessIds.FixtureBProcessId).ToArray();
        if (fixtureSessions.Any(s => Overlap(s.StartedAt, s.EndedAt, suspendAt, resumeAt) > SuspendBoundaryTolerance))
            return Result(AcceptanceStatus.Failed, "A fixture usage session overlaps the suspended interval by more than 15 seconds.");

        var preSuspendA = fixtureSessions.Any(s => s.ProcessId == fixtureProcessIds.FixtureAProcessId &&
            s.StartedAt < suspendAt && WithinTolerance(s.EndedAt, suspendAt, SuspendBoundaryTolerance));
        var resumedA = fixtureSessions.Where(s => s.ProcessId == fixtureProcessIds.FixtureAProcessId &&
            s.StartedAt >= resumeAt - SuspendBoundaryTolerance && s.EndedAt < operatorCompletedAt).ToArray();
        var fixtureB = fixtureSessions.Where(s => s.ProcessId == fixtureProcessIds.FixtureBProcessId &&
            s.EndedAt >= operatorCompletedAt - SuspendBoundaryTolerance && s.EndedAt <= operatorCompletedAt).ToArray();
        var closedBySwitch = resumedA.Any(a => fixtureB.Any(b => b.StartedAt >= a.EndedAt &&
            b.StartedAt - a.EndedAt <= SuspendBoundaryTolerance && b.StartedAt < operatorCompletedAt));

        return preSuspendA && resumedA.Length > 0 && fixtureB.Length > 0 && closedBySwitch
            ? Result(AcceptanceStatus.Passed, "Real Suspend/Resume notifications and closed fixture session boundaries verified.")
            : Result(AcceptanceStatus.Failed, "Required pre-Suspend, resumed fixture A, A-to-B switch, and fixture B session edges were not observed.");
    }

    private static bool WithinTolerance(DateTimeOffset left, DateTimeOffset right) =>
        WithinTolerance(left, right, LockBoundaryTolerance);

    private static bool WithinTolerance(DateTimeOffset left, DateTimeOffset right, TimeSpan tolerance) =>
        (left - right).Duration() <= tolerance;

    private static TimeSpan Overlap(DateTimeOffset firstStart, DateTimeOffset firstEnd,
        DateTimeOffset secondStart, DateTimeOffset secondEnd)
    {
        var start = firstStart > secondStart ? firstStart : secondStart;
        var end = firstEnd < secondEnd ? firstEnd : secondEnd;
        return end > start ? end - start : TimeSpan.Zero;
    }
}

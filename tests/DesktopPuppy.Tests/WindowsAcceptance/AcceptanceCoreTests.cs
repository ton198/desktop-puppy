using DesktopPuppy.WindowsAcceptance;
using DesktopPuppy.WindowsAcceptance.Core;
using Xunit;

namespace DesktopPuppy.Tests.WindowsAcceptance;

public sealed class AcceptanceCoreTests
{
    private static readonly DateTimeOffset T = new(2026, 9, 28, 12, 0, 0, TimeSpan.Zero);
    private static readonly FixtureProcessIds Fixtures = new(101, 202);

    // Purpose: avoid cross-run DB collisions.
    // Coverage: two generated owners and normalized paths.
    // Target effect: distinct DB files remain under the fixed temp root.
    [Fact]
    public void Owned_temp_directory_is_unique_and_scoped()
    {
        var a = AcceptanceOwnedDirectory.Create();
        var b = AcceptanceOwnedDirectory.Create();
        try
        {
            var root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "DesktopPuppy.WindowsAcceptance")) + Path.DirectorySeparatorChar;
            Assert.NotEqual(a.DatabasePath, b.DatabasePath);
            Assert.StartsWith(root, Path.GetFullPath(a.DatabasePath), StringComparison.OrdinalIgnoreCase);
            Assert.StartsWith(root, Path.GetFullPath(b.DatabasePath), StringComparison.OrdinalIgnoreCase);
            Assert.True(Directory.Exists(Path.GetDirectoryName(a.DatabasePath)));
        }
        finally { a.DeleteOwned(); b.DeleteOwned(); }
    }

    // Purpose: prevent cleanup from deleting user files.
    // Coverage: generated run directory, sibling directory and outside sentinel.
    // Target effect: cleanup removes only the run directory and preserves sibling/outside files.
    [Fact]
    public void Owned_temp_directory_cleanup_preserves_neighbor_files()
    {
        var owner = AcceptanceOwnedDirectory.Create();
        var neighbor = AcceptanceOwnedDirectory.Create();
        var outside = Path.Combine(Path.GetTempPath(), $"acceptance-sentinel-{Guid.NewGuid():N}");
        try
        {
            File.WriteAllText(owner.DatabasePath, "owned");
            File.WriteAllText(neighbor.DatabasePath, "neighbor");
            File.WriteAllText(outside, "outside");
            owner.DeleteOwned();
            Assert.False(Directory.Exists(Path.GetDirectoryName(owner.DatabasePath)));
            Assert.Equal("neighbor", File.ReadAllText(neighbor.DatabasePath));
            Assert.Equal("outside", File.ReadAllText(outside));
        }
        finally { owner.DeleteOwned(); neighbor.DeleteOwned(); File.Delete(outside); }
    }

    // Purpose: prevent simulated/incomplete system flows from passing.
    // Coverage: ordered lock events, pre/post fixture sessions and completion time.
    // Target effect: only correct closed edges return Passed.
    [Fact]
    public void Lock_verdict_requires_real_ordered_events_and_closed_edges()
    {
        var validator = new SessionBoundaryValidator();
        var events = new[] { E(ObservedSystemEventKind.Locked, 10), E(ObservedSystemEventKind.Unlocked, 20) };
        Assert.Equal(AcceptanceStatus.Passed, validator.ValidateLockUnlock(events, ClosedSessions(), Fixtures, T.AddSeconds(40), true).Status);
        Assert.Equal(AcceptanceStatus.Failed, validator.ValidateLockUnlock(events, new[] { S(101, 0, 10), S(202, 25, 45) }, Fixtures, T.AddSeconds(40), true).Status);
        Assert.Equal(AcceptanceStatus.Failed, validator.ValidateLockUnlock(events, ClosedSessions(), Fixtures, T.AddSeconds(29), true).Status);
        var withinTolerance = new[] { S(101, 0, 11), S(101, 15, 30), S(202, 31, 35) };
        Assert.Equal(AcceptanceStatus.Passed, validator.ValidateLockUnlock(events, withinTolerance, Fixtures, T.AddSeconds(40), true).Status);
    }

    // Purpose: enforce lock/unlock tolerance and reject sessions that continue through the locked interval.
    // Coverage: five-second lock/unlock edge tolerance and over-tolerance locked-time overlap.
    // Target effect: only boundaries within the documented tolerance may pass.
    [Fact]
    public void Lock_verdict_applies_five_second_boundary_tolerance()
    {
        var validator = new SessionBoundaryValidator();
        var events = new[] { E(ObservedSystemEventKind.Locked, 10), E(ObservedSystemEventKind.Unlocked, 20) };
        var edgeAtTolerance = new[] { S(101, 0, 15), S(101, 15, 30), S(202, 31, 35) };
        Assert.Equal(AcceptanceStatus.Passed, validator.ValidateLockUnlock(
            events, edgeAtTolerance, Fixtures, T.AddSeconds(40), true).Status);

        var switchAtTolerance = new[] { S(101, 0, 10), S(101, 25, 30), S(202, 35, 40) };
        Assert.Equal(AcceptanceStatus.Passed, validator.ValidateLockUnlock(
            events, switchAtTolerance, Fixtures, T.AddSeconds(45), true).Status);
        var spansLockedTooLong = new[] { S(101, 0, 10), S(101, 25, 30), S(202, 12, 18), S(202, 31, 35) };
        Assert.Equal(AcceptanceStatus.Failed, validator.ValidateLockUnlock(
            events, spansLockedTooLong, Fixtures, T.AddSeconds(40), true).Status);
        var latePreLockClose = new[] { S(101, 0, 16), S(101, 25, 30), S(202, 31, 35) };
        Assert.Equal(AcceptanceStatus.Failed, validator.ValidateLockUnlock(
            events, latePreLockClose, Fixtures, T.AddSeconds(40), true).Status);
        var bClosedTooEarly = new[] { S(101, 0, 10), S(101, 25, 30), S(202, 31, 34) };
        Assert.Equal(AcceptanceStatus.Failed, validator.ValidateLockUnlock(
            events, bClosedTooEarly, Fixtures, T.AddSeconds(40), true).Status);
    }
    // Purpose: ensure a later fixture-B row cannot retroactively prove it closed fixture A.
    // Coverage: resumed A closes ten seconds before B starts; controller confirmation follows B close.
    // Target effect: a gap greater than the lock boundary tolerance returns Failed.
    [Fact]
    public void Lock_verdict_rejects_large_gap_between_fixture_a_and_b()
    {
        var events = new[] { E(ObservedSystemEventKind.Locked, 10), E(ObservedSystemEventKind.Unlocked, 20) };
        var sessions = new[] { S(101, 0, 10), S(101, 25, 30), S(202, 40, 45) };

        Assert.Equal(AcceptanceStatus.Failed, new SessionBoundaryValidator().ValidateLockUnlock(
            events, sessions, Fixtures, T.AddSeconds(50), true).Status);
    }
    // Purpose: validate the bounded manual-step deadline before a run starts.
    // Coverage: default, minimum, maximum, below-range, above-range, and malformed values.
    // Target effect: only a 1–120 minute timeout is accepted; invalid input cannot start a run.
    [Fact]
    public void Step_timeout_defaults_to_thirty_minutes_and_accepts_only_one_to_one_hundred_twenty()
    {
        Assert.True(AcceptanceStepTimeout.TryRead(null, out var defaultTimeout));
        Assert.Equal(TimeSpan.FromMinutes(30), defaultTimeout);
        Assert.True(AcceptanceStepTimeout.TryRead("1", out var minimum));
        Assert.Equal(TimeSpan.FromMinutes(1), minimum);
        Assert.True(AcceptanceStepTimeout.TryRead("120", out var maximum));
        Assert.Equal(TimeSpan.FromMinutes(120), maximum);
        Assert.False(AcceptanceStepTimeout.TryRead("0", out _));
        Assert.False(AcceptanceStepTimeout.TryRead("121", out _));
        Assert.False(AcceptanceStepTimeout.TryRead("thirty", out _));
    }
    // Purpose: prove legitimate suspend/resume only from ordered observations and closed sessions.
    // Coverage: Suspend then Resume, pre/post fixture sessions and operator completion.
    // Target effect: a correct split returns Passed.
    [Fact]
    public void Suspend_verdict_requires_ordered_events_and_closed_edges()
    {
        Assert.Equal(AcceptanceStatus.Passed, new SessionBoundaryValidator().ValidateSuspendResume(
            new[] { E(ObservedSystemEventKind.Suspend, 10), E(ObservedSystemEventKind.Resume, 20) },
            ClosedSessions(), Fixtures, T.AddSeconds(40), true).Status);
    }

    // Purpose: ensure suspended time is excluded.
    // Coverage: Suspend/Resume timestamps and a fixture interval overlapping that range.
    // Target effect: overlapping sessions return Failed.
    [Fact]
    public void Suspend_verdict_rejects_usage_overlap()
    {
        Assert.Equal(AcceptanceStatus.Failed, new SessionBoundaryValidator().ValidateSuspendResume(
            new[] { E(ObservedSystemEventKind.Suspend, 10), E(ObservedSystemEventKind.Resume, 20) },
            new[] { S(101, 0, 15), S(101, 25, 30), S(202, 31, 35) }, Fixtures, T.AddSeconds(40), true).Status);
    }

    // Purpose: do not report acceptance when the manual step is skipped.
    // Coverage: absent confirmation despite partial events.
    // Target effect: Incomplete, never Passed.
    [Fact]
    public void Missing_operator_completion_is_incomplete()
    {
        Assert.Equal(AcceptanceStatus.Incomplete, new SessionBoundaryValidator().ValidateLockUnlock(
            new[] { E(ObservedSystemEventKind.Locked, 10) }, ClosedSessions(), Fixtures, T.AddSeconds(40), false).Status);
        Assert.Equal(AcceptanceStatus.Incomplete, new SessionBoundaryValidator().ValidateSuspendResume(
            new[] { E(ObservedSystemEventKind.Suspend, 10) }, ClosedSessions(), Fixtures, T.AddSeconds(40), false).Status);
    }

    // Purpose: require an actual ordered Windows event chain for both scenarios.
    // Coverage: missing event and reversed lock/power sequences.
    // Target effect: Failed, never Passed.
    [Fact]
    public void Missing_or_reordered_system_event_is_failed()
    {
        var validator = new SessionBoundaryValidator();
        Assert.Equal(AcceptanceStatus.Failed, validator.ValidateLockUnlock(
            new[] { E(ObservedSystemEventKind.Unlocked, 10) }, ClosedSessions(), Fixtures, T.AddSeconds(40), true).Status);
        foreach (var events in new[] {
            new[] { E(ObservedSystemEventKind.Locked, 10) },
            new[] { E(ObservedSystemEventKind.Unlocked, 10), E(ObservedSystemEventKind.Locked, 20) } })
            Assert.Equal(AcceptanceStatus.Failed, validator.ValidateLockUnlock(events, ClosedSessions(), Fixtures, T.AddSeconds(40), true).Status);
        foreach (var events in new[] {
            new[] { E(ObservedSystemEventKind.Suspend, 10) },
            new[] { E(ObservedSystemEventKind.Resume, 10), E(ObservedSystemEventKind.Suspend, 20) } })
            Assert.Equal(AcceptanceStatus.Failed, validator.ValidateSuspendResume(events, ClosedSessions(), Fixtures, T.AddSeconds(40), true).Status);
    }

    private static ObservedSystemEvent E(ObservedSystemEventKind kind, int second) => new(kind, T.AddSeconds(second));
    private static ObservedUsageSession S(uint pid, int start, int end) => new(pid, T.AddSeconds(start), T.AddSeconds(end));
    private static ObservedUsageSession[] ClosedSessions() => new[] { S(101, 0, 10), S(101, 25, 30), S(202, 31, 35) };
}

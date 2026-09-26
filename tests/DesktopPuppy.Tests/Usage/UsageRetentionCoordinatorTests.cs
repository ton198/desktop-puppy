using DesktopPuppy.Usage;
using Xunit;

namespace DesktopPuppy.Tests.Usage;

public sealed class UsageRetentionCoordinatorTests
{
    [Fact]
    public void First_check_prunes_using_oldest_retained_local_midnight()
    {
        // Purpose: ensure runtime retention starts immediately rather than waiting for a date rollover.
        // Coverage: first check, injected time zone, and the retention policy cutoff.
        // Target effect: the prune callback receives the oldest retained local midnight in UTC.
        var nowUtc = new DateTimeOffset(2026, 9, 25, 15, 45, 0, TimeSpan.Zero);
        var cutoffs = new List<DateTimeOffset>();
        var coordinator = new UsageRetentionCoordinator(cutoffs.Add, TimeZoneInfo.Utc);

        var didPrune = coordinator.CheckAndPrune(nowUtc);

        Assert.True(didPrune);
        Assert.Equal(new DateTimeOffset(2026, 9, 11, 0, 0, 0, TimeSpan.Zero), Assert.Single(cutoffs));
    }

    [Fact]
    public void Repeated_checks_on_same_local_date_do_not_prune_again()
    {
        // Purpose: prevent repeated timer checks from performing unnecessary database cleanup.
        // Coverage: multiple checks within the same local date.
        // Target effect: only the first check invokes the prune callback.
        var firstCheck = new DateTimeOffset(2026, 9, 25, 9, 0, 0, TimeSpan.Zero);
        var cutoffs = new List<DateTimeOffset>();
        var coordinator = new UsageRetentionCoordinator(cutoffs.Add, TimeZoneInfo.Utc);

        var firstPruned = coordinator.CheckAndPrune(firstCheck);
        var secondPruned = coordinator.CheckAndPrune(firstCheck.AddHours(8));

        Assert.True(firstPruned);
        Assert.False(secondPruned);
        Assert.Single(cutoffs);
    }

    [Fact]
    public void First_check_after_local_date_change_prunes_new_cutoff_once()
    {
        // Purpose: keep retention rolling while the application remains open across local midnight.
        // Coverage: checks immediately before and after a DST-observing zone's local date rollover.
        // Target effect: the new date prunes once with its new local-midnight cutoff.
        var eastern = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
        var beforeLocalMidnight = new DateTimeOffset(2026, 9, 25, 3, 59, 0, TimeSpan.Zero);
        var afterLocalMidnight = new DateTimeOffset(2026, 9, 25, 4, 1, 0, TimeSpan.Zero);
        var cutoffs = new List<DateTimeOffset>();
        var coordinator = new UsageRetentionCoordinator(cutoffs.Add, eastern);

        Assert.True(coordinator.CheckAndPrune(beforeLocalMidnight));
        Assert.True(coordinator.CheckAndPrune(afterLocalMidnight));

        Assert.Equal(
            [
                new DateTimeOffset(2026, 9, 10, 4, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 9, 11, 4, 0, 0, TimeSpan.Zero)
            ],
            cutoffs);
        Assert.False(coordinator.CheckAndPrune(afterLocalMidnight.AddMinutes(5)));
    }

    [Fact]
    public void Delayed_check_after_resume_catches_up_to_current_local_date()
    {
        // Purpose: ensure sleeping across dates does not preserve stale usage history.
        // Coverage: a resume-era check several local dates after the prior successful check.
        // Target effect: one prune uses the current date cutoff without replaying intermediate dates.
        var beforeSleep = new DateTimeOffset(2026, 9, 25, 15, 0, 0, TimeSpan.Zero);
        var afterResume = new DateTimeOffset(2026, 9, 28, 15, 0, 0, TimeSpan.Zero);
        var cutoffs = new List<DateTimeOffset>();
        var coordinator = new UsageRetentionCoordinator(cutoffs.Add, TimeZoneInfo.Utc);

        Assert.True(coordinator.CheckAndPrune(beforeSleep));
        Assert.True(coordinator.CheckAndPrune(afterResume));

        Assert.Equal(
            [
                new DateTimeOffset(2026, 9, 11, 0, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 9, 14, 0, 0, 0, TimeSpan.Zero)
            ],
            cutoffs);
    }

    [Fact]
    public void Failed_prune_does_not_mark_date_complete_and_can_be_retried()
    {
        // Purpose: avoid silently skipping cleanup after a transient database failure.
        // Coverage: a failed prune followed by another check on the same local date.
        // Target effect: the date remains due until a successful retry marks it complete.
        var nowUtc = new DateTimeOffset(2026, 9, 25, 12, 0, 0, TimeSpan.Zero);
        var calls = 0;
        var coordinator = new UsageRetentionCoordinator(
            _ =>
            {
                calls++;
                if (calls == 1)
                {
                    throw new IOException("temporary database failure");
                }
            },
            TimeZoneInfo.Utc);

        Assert.Throws<IOException>(() => coordinator.CheckAndPrune(nowUtc));
        Assert.True(coordinator.CheckAndPrune(nowUtc));
        Assert.False(coordinator.CheckAndPrune(nowUtc));
        Assert.Equal(2, calls);
    }
}
using DesktopPuppy.Native;
using DesktopPuppy.Usage;
using Xunit;

namespace DesktopPuppy.Tests.Usage;

public sealed class WindowTitleDebouncerTests
{
    private static readonly DateTimeOffset Start = new(2026, 9, 23, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Stable_title_is_upserted_after_two_seconds()
    {
        var updates = new List<WindowTitleInterval>();
        var debouncer = new WindowTitleDebouncer(TimeSpan.FromSeconds(2), updates.Add);

        debouncer.OnTitleChanged(Snapshot("Code", "main.cs"), Start);
        debouncer.Advance(Start.AddSeconds(1.999));
        Assert.Empty(updates);

        debouncer.Advance(Start.AddSeconds(2));

        var interval = Assert.Single(updates);
        Assert.Equal("main.cs", interval.WindowTitle);
        Assert.Equal(Start, interval.StartedAt);
        Assert.Null(interval.EndedAt);
    }

    [Fact]
    public void Title_changed_before_two_seconds_is_discarded()
    {
        var updates = new List<WindowTitleInterval>();
        var debouncer = new WindowTitleDebouncer(TimeSpan.FromSeconds(2), updates.Add);

        debouncer.OnTitleChanged(Snapshot("Code", "main.cs"), Start);
        debouncer.OnTitleChanged(Snapshot("Code", "other.cs"), Start.AddSeconds(1));
        debouncer.Advance(Start.AddSeconds(2.5));

        Assert.Empty(updates);
    }

    [Fact]
    public void New_title_closes_the_previous_stable_interval_and_waits_for_the_new_title()
    {
        var updates = new List<WindowTitleInterval>();
        var debouncer = new WindowTitleDebouncer(TimeSpan.FromSeconds(2), updates.Add);

        debouncer.OnTitleChanged(Snapshot("Code", "main.cs"), Start);
        debouncer.Advance(Start.AddSeconds(2));
        var firstIntervalId = Assert.Single(updates).Id;
        debouncer.OnTitleChanged(Snapshot("Code", "other.cs"), Start.AddSeconds(5));

        Assert.Collection(
            updates,
            opened =>
            {
                Assert.Equal("main.cs", opened.WindowTitle);
                Assert.Null(opened.EndedAt);
            },
            closed =>
            {
                Assert.Equal("main.cs", closed.WindowTitle);
                Assert.Equal(Start.AddSeconds(5), closed.EndedAt);
                Assert.Equal(firstIntervalId, closed.Id);
            });
    }

    [Fact]
    public void Repeating_the_same_title_does_not_duplicate_the_interval()
    {
        var updates = new List<WindowTitleInterval>();
        var debouncer = new WindowTitleDebouncer(TimeSpan.FromSeconds(2), updates.Add);
        var snapshot = Snapshot("Code", "main.cs");

        debouncer.OnTitleChanged(snapshot, Start);
        debouncer.Advance(Start.AddSeconds(2));
        debouncer.OnTitleChanged(snapshot, Start.AddSeconds(3));
        debouncer.Advance(Start.AddSeconds(5));

        Assert.Single(updates);
    }

    [Fact]
    public void Leaving_the_window_before_stability_discards_the_pending_title()
    {
        var updates = new List<WindowTitleInterval>();
        var debouncer = new WindowTitleDebouncer(TimeSpan.FromSeconds(2), updates.Add);

        debouncer.OnTitleChanged(Snapshot("Code", "main.cs"), Start);
        debouncer.OnForegroundWindowLeft(Start.AddSeconds(1));

        Assert.Empty(updates);
    }

    [Fact]
    public void Durable_checkpoints_are_throttled_to_thirty_seconds_and_keep_one_interval_identity()
    {
        var updates = new List<WindowTitleInterval>();
        var debouncer = new WindowTitleDebouncer(TimeSpan.FromSeconds(2), updates.Add);
        debouncer.OnTitleChanged(Snapshot("Code", "main.cs"), Start);
        debouncer.OnTitleChanged(Snapshot("Code", "main.cs"), Start.AddSeconds(2));
        debouncer.Advance(Start.AddSeconds(2));
        var intervalId = Assert.Single(updates).Id;
        debouncer.OnTitleChanged(Snapshot("Code", "main.cs"), Start.AddSeconds(31.999));
        debouncer.Advance(Start.AddSeconds(31.999));
        Assert.Single(updates);
        debouncer.OnTitleChanged(Snapshot("Code", "main.cs"), Start.AddSeconds(32));
        debouncer.Advance(Start.AddSeconds(32));
        Assert.Equal(2, updates.Count);
        Assert.All(updates, update => Assert.Equal(intervalId, update.Id));
        Assert.All(updates, update => Assert.Null(update.EndedAt));
    }

    [Fact]
    public void Failed_title_close_keeps_the_interval_available_for_retry()
    {
        var updates = new List<WindowTitleInterval>();
        var failClose = true;
        var debouncer = new WindowTitleDebouncer(TimeSpan.FromSeconds(2), interval =>
        {
            if (interval.EndedAt is not null && failClose)
            {
                failClose = false;
                throw new InvalidOperationException("Simulated close failure.");
            }
            updates.Add(interval);
        });
        debouncer.OnTitleChanged(Snapshot("Code", "old.cs"), Start);
        debouncer.Advance(Start.AddSeconds(2));
        var originalId = Assert.Single(updates).Id;
        Assert.Throws<InvalidOperationException>(() =>
            debouncer.OnTitleChanged(Snapshot("Code", "new.cs"), Start.AddSeconds(5)));
        debouncer.OnTitleChanged(Snapshot("Code", "new.cs"), Start.AddSeconds(5.25));
        var closed = Assert.Single(updates, interval => interval.EndedAt is not null);
        Assert.Equal(originalId, closed.Id);
        Assert.Equal(Start.AddSeconds(5), closed.EndedAt);
    }

    private static ForegroundWindowSnapshot Snapshot(string processName, string title)
    {
        return new ForegroundWindowSnapshot((nint)42, 42, processName, title);
    }
}


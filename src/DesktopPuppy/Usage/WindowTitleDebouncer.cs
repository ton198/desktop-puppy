using DesktopPuppy.Native;

namespace DesktopPuppy.Usage;

public sealed record WindowTitleInterval(
    Guid Id,
    ForegroundWindowSnapshot Snapshot,
    DateTimeOffset StartedAt,
    DateTimeOffset? EndedAt)
{
    public DateTimeOffset? LastConfirmedAt { get; init; }

    public uint ProcessId => Snapshot.ProcessId;

    public string ProcessName => Snapshot.ProcessName;

    public string WindowTitle => Snapshot.WindowTitle;
}

public sealed class WindowTitleDebouncer
{
    private static readonly TimeSpan CheckpointInterval = TimeSpan.FromSeconds(30);
    private readonly TimeSpan _stabilityWindow;
    private readonly Action<WindowTitleInterval> _onIntervalChanged;
    private ForegroundWindowSnapshot? _pendingSnapshot;
    private DateTimeOffset _pendingSince;
    private DateTimeOffset _lastObservedAt;
    private WindowTitleInterval? _stableInterval;
    private WindowTitleInterval? _pendingClosedInterval;

    public WindowTitleDebouncer(
        TimeSpan stabilityWindow,
        Action<WindowTitleInterval> onIntervalChanged)
    {
        if (stabilityWindow <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(stabilityWindow));
        }

        ArgumentNullException.ThrowIfNull(onIntervalChanged);
        _stabilityWindow = stabilityWindow;
        _onIntervalChanged = onIntervalChanged;
    }

    public void OnTitleChanged(ForegroundWindowSnapshot snapshot, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        FlushPendingClose();
        _lastObservedAt = at;

        if (_stableInterval is not null && SnapshotsMatch(_stableInterval.Snapshot, snapshot))
        {
            _pendingSnapshot = null;
            return;
        }

        if (_stableInterval is not null)
        {
            CloseStableInterval(at);
        }

        if (_pendingSnapshot is not null && SnapshotsMatch(_pendingSnapshot, snapshot))
        {
            return;
        }

        _pendingSnapshot = snapshot;
        _pendingSince = at;
    }

    public void OnForegroundWindowLeft(DateTimeOffset at)
    {
        _pendingSnapshot = null;
        FlushPendingClose();
        CloseStableInterval(at);
    }

    public void Advance(DateTimeOffset at)
    {
        FlushPendingClose();
        if (_stableInterval is { } stable)
        {
            var lastCheckpoint = stable.LastConfirmedAt ?? stable.StartedAt;
            if (_lastObservedAt - lastCheckpoint >= CheckpointInterval)
            {
                var checkpoint = stable with { LastConfirmedAt = _lastObservedAt };
                _onIntervalChanged(checkpoint);
                _stableInterval = checkpoint;
            }
            return;
        }

        if (_pendingSnapshot is null || at - _pendingSince < _stabilityWindow)
        {
            return;
        }

        var interval = new WindowTitleInterval(Guid.NewGuid(), _pendingSnapshot, _pendingSince, EndedAt: null)
        {
            LastConfirmedAt = _lastObservedAt
        };
        _onIntervalChanged(interval);
        _pendingSnapshot = null;
        _stableInterval = interval;
    }

    private void CloseStableInterval(DateTimeOffset at)
    {
        if (_stableInterval is null)
        {
            return;
        }

        // Retire the live interval immediately, but keep the exact observed boundary until
        // persistence succeeds. A later unlock of the same title must open a new interval.
        _pendingClosedInterval = _stableInterval with { EndedAt = at, LastConfirmedAt = at };
        _stableInterval = null;
        FlushPendingClose();
    }

    private void FlushPendingClose()
    {
        if (_pendingClosedInterval is not { } closed) return;
        _onIntervalChanged(closed);
        _pendingClosedInterval = null;
    }

    private static bool SnapshotsMatch(
        ForegroundWindowSnapshot left,
        ForegroundWindowSnapshot right)
    {
        return left.WindowHandle == right.WindowHandle
            && left.ProcessId == right.ProcessId
            && string.Equals(left.ProcessName, right.ProcessName, StringComparison.Ordinal)
            && string.Equals(left.WindowTitle, right.WindowTitle, StringComparison.Ordinal);
    }
}

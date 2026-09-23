using DesktopPuppy.Native;

namespace DesktopPuppy.Usage;

public sealed record WindowTitleInterval(
    Guid Id,
    ForegroundWindowSnapshot Snapshot,
    DateTimeOffset StartedAt,
    DateTimeOffset? EndedAt)
{
    public uint ProcessId => Snapshot.ProcessId;

    public string ProcessName => Snapshot.ProcessName;

    public string WindowTitle => Snapshot.WindowTitle;
}

public sealed class WindowTitleDebouncer
{
    private readonly TimeSpan _stabilityWindow;
    private readonly Action<WindowTitleInterval> _onIntervalChanged;
    private ForegroundWindowSnapshot? _pendingSnapshot;
    private DateTimeOffset _pendingSince;
    private WindowTitleInterval? _stableInterval;

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
        CloseStableInterval(at);
    }

    public void Advance(DateTimeOffset at)
    {
        if (_pendingSnapshot is null || at - _pendingSince < _stabilityWindow)
        {
            return;
        }

        var snapshot = _pendingSnapshot;
        _pendingSnapshot = null;
        _stableInterval = new WindowTitleInterval(Guid.NewGuid(), snapshot, _pendingSince, EndedAt: null);
        _onIntervalChanged(_stableInterval);
    }

    private void CloseStableInterval(DateTimeOffset at)
    {
        if (_stableInterval is null)
        {
            return;
        }

        var closed = _stableInterval with { EndedAt = at };
        _stableInterval = null;
        _onIntervalChanged(closed);
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

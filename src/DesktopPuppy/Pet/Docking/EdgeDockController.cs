namespace DesktopPuppy.Pet.Docking;

public enum DockSide { None, Left, Right }
public enum DockState { Free, Collapsing, Collapsed, Expanding, Expanded }

/// <summary>Monotonic-time docking policy, independent of WPF and physical monitor geometry.</summary>
public sealed class EdgeDockController
{
    public DockSide Side { get; private set; }
    public DockState State { get; private set; }
    public double Progress { get; private set; }
    private double _from, _target;
    private TimeSpan _started;

    public static DockSide Detect(double left, double right, double workLeft, double workRight, double dpiScale)
    {
        var threshold = 24 * dpiScale;
        var nearLeft = left <= workLeft + threshold;
        var nearRight = right >= workRight - threshold;
        if (nearLeft && nearRight)
            return Math.Abs(left - workLeft) <= Math.Abs(right - workRight) ? DockSide.Left : DockSide.Right;
        return nearLeft ? DockSide.Left : nearRight ? DockSide.Right : DockSide.None;
    }

    public void Dock(DockSide side, TimeSpan now)
    {
        Reset();
        if (side == DockSide.None) return;
        Side = side;
        State = DockState.Expanded;
        MoveTo(1, now);
    }

    public void KeepExpanded(TimeSpan now)
    {
        if (Side == DockSide.None) return;
        Progress = _from = _target = 0;
        _started = now;
        State = DockState.Expanded;
    }

    public void Reset()
    {
        Side = DockSide.None;
        State = DockState.Free;
        Progress = _from = _target = 0;
    }

    public void Update(TimeSpan now, bool pointerInside, bool protectedInteraction)
    {
        if (Side == DockSide.None) return;
        Sample(now);
        MoveTo(pointerInside || protectedInteraction ? 0 : 1, now);
    }

    private void MoveTo(double target, TimeSpan now)
    {
        if (_target == target) return;
        _from = Progress;
        _target = target;
        _started = now;
        State = target == 1 ? DockState.Collapsing : DockState.Expanding;
    }

    private void Sample(TimeSpan now)
    {
        var duration = _target == 0 ? 350 : 250;
        var t = Math.Clamp((now - _started).TotalMilliseconds / duration, 0, 1);
        var eased = t * t * (3 - 2 * t);
        Progress = _from + (_target - _from) * eased;
        if (t >= 1) State = _target == 1 ? DockState.Collapsed : DockState.Expanded;
    }
}

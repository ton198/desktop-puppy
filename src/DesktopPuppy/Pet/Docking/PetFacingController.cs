namespace DesktopPuppy.Pet.Docking;

/// <summary>Faces the active monitor's center, with hysteresis and reversible turning.</summary>
public sealed class PetFacingController
{
    private bool _initialized;
    private double _from, _target;
    private TimeSpan _started;
    public double Orientation { get; private set; } // 0 = right, 1 = left
    public bool FacingRight { get; private set; }
    public bool IsTurning => Math.Abs(Orientation - _target) > .000001;
    public void Update(double center, double midpoint, double buffer, TimeSpan now)
    {
        if (!_initialized)
        {
            _initialized = true;
            FacingRight = center < midpoint;
            Orientation = _from = _target = FacingRight ? 0 : 1;
            return;
        }
        var t = Math.Clamp((now - _started).TotalMilliseconds / 240, 0, 1);
        var eased = t * t * (3 - 2 * t);
        Orientation = _from + (_target - _from) * eased;
        if (t >= 1) FacingRight = _target == 0;
        var target = center < midpoint - buffer ? 0 : center > midpoint + buffer ? 1 : _target;
        if (target == _target) return;
        _from = Orientation;
        _target = target;
        _started = now;
    }
}

namespace DesktopPuppy.Ai;

// UI-thread confined. Request ownership survives placeholder expiry, display ownership does not.
public sealed class SingleBubbleRequestGate
{
    private Guid? _active;
    private TimeSpan _deadline;
    private bool _ownsBubble;
    public bool IsBusy => _active.HasValue;
    public Guid? TryBegin(TimeSpan now)
    {
        if (IsBusy) return null;
        _active = Guid.NewGuid(); _deadline = now + TimeSpan.FromSeconds(15); _ownsBubble = true; return _active;
    }
    public bool CanDisplay(Guid id, TimeSpan now, bool visible) => _active == id && _ownsBubble && now < _deadline && visible;
    public bool Invalidate() { var previous = _ownsBubble; _ownsBubble = false; return previous; }
    public bool Expire(TimeSpan now) => _active.HasValue && now >= _deadline && Invalidate();
    public void Complete(Guid id) { if (_active == id) { _active = null; _ownsBubble = false; } }
}

public sealed class PetVisibilityState
{
    public bool UserHidden => _userHidden;
    public bool StartupWaiting { get; set; }
    private bool _userHidden, _fullscreen;
    public bool ShouldShow => !_userHidden && !_fullscreen && !StartupWaiting;
    public void SetUserHidden(bool hidden) => _userHidden = hidden;
    public void SetFullscreen(bool fullscreen) => _fullscreen = fullscreen;
}

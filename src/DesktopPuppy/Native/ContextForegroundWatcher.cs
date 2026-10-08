namespace DesktopPuppy.Native;

// Observe the existing hook, including collector refreshes; do not install a second hook.
public sealed class ContextForegroundWatcher(IForegroundWindowWatcher inner, string ownProcessName) : IForegroundWindowWatcher
{
    private ForegroundWindowSnapshot? _lastExternal;
    public ForegroundWindowSnapshot? LastExternal => Volatile.Read(ref _lastExternal);
    public void Start(Action<ForegroundWindowSnapshot> onForegroundChanged) => inner.Start(snapshot =>
    {
        if (!string.Equals(snapshot.ProcessName, ownProcessName, StringComparison.OrdinalIgnoreCase)) Volatile.Write(ref _lastExternal, snapshot);
        onForegroundChanged(snapshot);
    });
    public void RefreshForegroundWindow() => inner.RefreshForegroundWindow();
    public void Stop() => inner.Stop();
    public void Dispose() => inner.Dispose();
}

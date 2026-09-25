namespace DesktopPuppy.Native;

public interface IForegroundWindowWatcher : IDisposable
{
    void Start(Action<ForegroundWindowSnapshot> onForegroundChanged);

    void RefreshForegroundWindow();

    void Stop();
}

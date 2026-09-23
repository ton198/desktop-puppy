namespace DesktopPuppy.Native;

public interface ISingleInstanceGuard : IDisposable
{
    bool TryAcquire(Action activateExisting);
}

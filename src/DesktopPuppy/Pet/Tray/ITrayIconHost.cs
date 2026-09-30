namespace DesktopPuppy.Pet.Tray;

public interface ITrayIconHost : IDisposable
{
    event EventHandler<TrayIconCommandEventArgs>? CommandRequested;
    void Start();
}

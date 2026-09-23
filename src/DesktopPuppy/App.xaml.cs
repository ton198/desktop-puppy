using System.Windows;
using DesktopPuppy.Pet;

namespace DesktopPuppy;

public partial class App : Application
{
    private PetWindow? _petWindow;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        ShutdownMode = ShutdownMode.OnMainWindowClose;
        _petWindow = new PetWindow();
        MainWindow = _petWindow;
        _petWindow.Show();
    }
}

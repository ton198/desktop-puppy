using System.Windows;

namespace DesktopPuppy.WindowsAcceptance;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (e.Args.Length == 3 && e.Args[0] == "--fixture")
        {
            if (WindowsAcceptancePrerequisite.SessionSkipReason() is not null ||
                !Guid.TryParseExact(e.Args[1], "N", out _) || e.Args[2] is not ("A" or "B"))
            {
                Shutdown();
                return;
            }
            var fixture = new ForegroundFixtureWindow(e.Args[1], e.Args[2]);
            MainWindow = fixture;
            fixture.Show();
            return;
        }

        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        var controller = new WindowsAcceptanceWindow();
        MainWindow = controller;
        controller.Show();
    }
}

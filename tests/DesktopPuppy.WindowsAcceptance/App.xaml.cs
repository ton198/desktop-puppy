using System.Windows;
using System.Windows.Controls;

namespace DesktopPuppy.WindowsAcceptance;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (e.Args.Length == 3 && e.Args[0] == "--fixture")
        {
            // Fixture windows are only a visible shell in Task 1; no product is launched.
            if (Environment.GetEnvironmentVariable("DESKTOP_PUPPY_WINDOWS_ACCEPTANCE") != "1")
            {
                Shutdown();
                return;
            }
            var fixture = new Window
            {
                Title = $"Desktop Puppy acceptance fixture {e.Args[2]}",
                Width = 420, Height = 180,
                Content = new TextBlock { Text = $"Fixture {e.Args[2]} — run {e.Args[1]}", Margin = new Thickness(24) },
                ShowActivated = e.Args[2] != "B"
            };
            MainWindow = fixture;
            fixture.Show();
            return;
        }

        var controller = new WindowsAcceptanceWindow();
        MainWindow = controller;
        controller.Show();
    }
}

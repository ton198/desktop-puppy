using System.Windows;
using System.Windows.Automation;

namespace DesktopPuppy.WindowsAcceptance;

public partial class ForegroundFixtureWindow : Window
{
    public ForegroundFixtureWindow(string runId, string fixtureId)
    {
        InitializeComponent();
        AutomationProperties.SetAutomationId(this, $"DesktopPuppyAcceptance-{runId}-{fixtureId}");
        ShowActivated = fixtureId != "B";
    }
}

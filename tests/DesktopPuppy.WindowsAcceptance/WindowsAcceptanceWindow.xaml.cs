using System.Windows;

namespace DesktopPuppy.WindowsAcceptance;

public partial class WindowsAcceptanceWindow : Window
{
    private string? scenario;

    public WindowsAcceptanceWindow()
    {
        InitializeComponent();
        CheckPrerequisites();
    }

    private bool CheckPrerequisites()
    {
        var reason = WindowsAcceptancePrerequisite.SkipReason();
        if (reason is null) return true;
        ResultState.Text = "Skipped";
        ResultDetail.Text = reason;
        BeginButton.IsEnabled = false;
        CompleteButton.IsEnabled = false;
        LockScenario.IsEnabled = false;
        PowerScenario.IsEnabled = false;
        return false;
    }

    private void ChooseLock(object sender, RoutedEventArgs e)
    {
        scenario = "lock";
        Instructions.Text = "Purpose: check real lock/unlock session boundaries. Coverage: with fixture A foreground, manually lock and unlock Windows; focus fixture A again, switch to fixture B, then return to this controller and confirm. Target effect: A closes at lock, then the resumed A session and B session close. Do not start the lock until A is foreground.";
        BeginButton.IsEnabled = true;
        ResultState.Text = "Not started";
        ResultDetail.Text = "Instructions only; Task 1 has no process or event lifecycle.";
    }

    private void ChoosePower(object sender, RoutedEventArgs e)
    {
        scenario = "power";
        Instructions.Text = "Purpose: check real suspend/resume session boundaries. Coverage: with fixture A foreground, manually sleep and wake Windows, optionally unlock, focus fixture A, switch to fixture B, then return to this controller and confirm. Target effect: A closes at suspend; B closes the resumed session. Do not start sleep until A is foreground.";
        BeginButton.IsEnabled = true;
        ResultState.Text = "Not started";
        ResultDetail.Text = "Instructions only; Task 1 has no process or event lifecycle.";
    }

    private void Begin(object sender, RoutedEventArgs e)
    {
        if (scenario is null || !CheckPrerequisites()) return;
        ResultState.Text = "Waiting for operator";
        ResultDetail.Text = "Shell only: no fixtures/product were launched and no Windows events are being captured. Manual action cannot yield a Passed verdict in Task 1.";
        BeginButton.IsEnabled = false;
        CompleteButton.IsEnabled = true;
        CancelButton.IsEnabled = true;
        LockScenario.IsEnabled = false;
        PowerScenario.IsEnabled = false;
    }

    private void Complete(object sender, RoutedEventArgs e)
    {
        ResultState.Text = "Incomplete";
        ResultDetail.Text = "No product sessions or real system events were observed by this Task 1 shell. Acceptance cannot pass.";
        CompleteButton.IsEnabled = false;
        CancelButton.IsEnabled = false;
        BeginButton.IsEnabled = true;
        LockScenario.IsEnabled = true;
        PowerScenario.IsEnabled = true;
    }

    private void Cancel(object sender, RoutedEventArgs e)
    {
        ResultState.Text = "Incomplete";
        ResultDetail.Text = "Operator cancelled; no acceptance verdict was produced.";
        CompleteButton.IsEnabled = false;
        CancelButton.IsEnabled = false;
        BeginButton.IsEnabled = true;
        LockScenario.IsEnabled = true;
        PowerScenario.IsEnabled = true;
    }
}

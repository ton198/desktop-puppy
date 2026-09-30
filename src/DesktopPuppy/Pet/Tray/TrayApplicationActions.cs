namespace DesktopPuppy.Pet.Tray;

internal sealed class TrayApplicationActions : ITrayApplicationActions
{
    private readonly ITrayPetWindow _petWindow;
    private readonly Action _openStatistics;

    public TrayApplicationActions(ITrayPetWindow petWindow, Action openStatistics)
    {
        _petWindow = petWindow ?? throw new ArgumentNullException(nameof(petWindow));
        _openStatistics = openStatistics ?? throw new ArgumentNullException(nameof(openStatistics));
    }

    public void ShowPet()
    {
        if (!_petWindow.IsVisible) _petWindow.Show();
        if (_petWindow.IsMinimized) _petWindow.Restore();
        _petWindow.Activate();
    }

    public void HidePet() => _petWindow.Hide();

    public void OpenStatistics() => _openStatistics();

    public void ExitApplication() => _petWindow.Close();
}

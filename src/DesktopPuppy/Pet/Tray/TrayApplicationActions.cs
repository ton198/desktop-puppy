namespace DesktopPuppy.Pet.Tray;

internal sealed class TrayApplicationActions : ITrayApplicationActions
{
    private readonly ITrayPetWindow _petWindow;
    private readonly Action _openStatistics;
    private readonly Action _openAiSettings;
    public Action? PauseAction { get; set; }
    public Action? SettingsAction { get; set; }
    public Action? SpeechAction { get; set; }
    public Action? TodayReportAction { get; set; }
    public Action? ReportHistoryAction { get; set; }
    public Action? ShowAction { get; set; }

    public TrayApplicationActions(ITrayPetWindow petWindow, Action openStatistics, Action? openAiSettings = null)
    {
        _petWindow = petWindow ?? throw new ArgumentNullException(nameof(petWindow));
        _openStatistics = openStatistics ?? throw new ArgumentNullException(nameof(openStatistics));
        _openAiSettings = openAiSettings ?? (() => { });
    }

    public void ShowPet()
    {
        if (ShowAction is not null) { ShowAction(); return; }
        if (!_petWindow.IsVisible) _petWindow.Show();
        if (_petWindow.IsMinimized) _petWindow.Restore();
        _petWindow.Activate();
    }

    public void HidePet() => _petWindow.Hide();

    public void OpenStatistics() => _openStatistics();
    public void OpenAiSettings() => _openAiSettings();
    public void TogglePause() => PauseAction?.Invoke();
    public void OpenSettings() => SettingsAction?.Invoke();
    public void RequestSpeech() => SpeechAction?.Invoke();
    public void TodayReport() => TodayReportAction?.Invoke();
    public void ReportHistory() => ReportHistoryAction?.Invoke();

    public void ExitApplication() => _petWindow.Close();
}

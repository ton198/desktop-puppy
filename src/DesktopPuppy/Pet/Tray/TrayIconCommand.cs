namespace DesktopPuppy.Pet.Tray;

public enum TrayIconCommand
{
    ShowPet,
    HidePet,
    OpenStatistics,
    OpenAiSettings,
    TogglePause,
    OpenSettings,
    RequestSpeech,
    TodayReport,
    ReportHistory,
    Exit
}

public sealed class TrayIconCommandEventArgs(TrayIconCommand command) : EventArgs
{
    public TrayIconCommand Command { get; } = command;
}

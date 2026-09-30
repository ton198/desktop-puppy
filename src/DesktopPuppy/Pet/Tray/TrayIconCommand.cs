namespace DesktopPuppy.Pet.Tray;

public enum TrayIconCommand
{
    ShowPet,
    HidePet,
    OpenStatistics,
    Exit
}

public sealed class TrayIconCommandEventArgs(TrayIconCommand command) : EventArgs
{
    public TrayIconCommand Command { get; } = command;
}

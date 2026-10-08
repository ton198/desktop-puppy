namespace DesktopPuppy.Pet.Tray;

public interface ITrayApplicationActions
{
    void ShowPet();
    void HidePet();
    void OpenStatistics();
    void OpenAiSettings() { }
    void ExitApplication();
}

namespace DesktopPuppy.Pet.Tray;

public interface ITrayApplicationActions
{
    void ShowPet();
    void HidePet();
    void OpenStatistics();
    void OpenAiSettings() { }
    void TogglePause() { }
    void OpenSettings() { }
    void RequestSpeech() { }
    void TodayReport() { }
    void ReportHistory() { }
    void ExitApplication();
}

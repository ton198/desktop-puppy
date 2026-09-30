namespace DesktopPuppy.Pet.Tray;

internal interface ITrayPetWindow
{
    bool IsVisible { get; }
    bool IsMinimized { get; }
    void Show();
    void Restore();
    void Hide();
    void Activate();
    void Close();
}

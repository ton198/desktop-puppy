using DesktopPuppy.Pet.Tray;
using Xunit;

namespace DesktopPuppy.Tests.Pet.Tray;

public sealed class TrayApplicationActionsTests
{
    // Purpose: Show a hidden pet using the existing window.
    // Coverage: The fake window starts hidden and normal.
    // Target effect: One Show and one Activate occur; no Close occurs.
    [Fact]
    public void Show_hidden_pet_shows_and_activates_existing_window()
    {
        var window = new FakeTrayPetWindow();
        var actions = new TrayApplicationActions(window, () => { });
        actions.ShowPet();
        Assert.Equal(1, window.ShowCount);
        Assert.Equal(1, window.ActivateCount);
        Assert.Equal(0, window.CloseCount);
    }

    // Purpose: Avoid re-showing an already visible normal pet.
    // Coverage: The fake window starts visible and not minimized.
    // Target effect: No Show or Restore occurs and Activate occurs once.
    [Fact]
    public void Show_visible_normal_pet_does_not_show_again()
    {
        var window = new FakeTrayPetWindow { IsVisible = true };
        var actions = new TrayApplicationActions(window, () => { });
        actions.ShowPet();
        Assert.Equal(0, window.ShowCount);
        Assert.Equal(0, window.RestoreCount);
        Assert.Equal(1, window.ActivateCount);
    }

    // Purpose: Restore a minimized pet before activation.
    // Coverage: The fake window starts visible and minimized.
    // Target effect: One Restore and one Activate occur; no Show or Close occurs.
    [Fact]
    public void Show_minimized_pet_restores_and_activates_existing_window()
    {
        var window = new FakeTrayPetWindow { IsVisible = true, IsMinimized = true };
        var actions = new TrayApplicationActions(window, () => { });
        actions.ShowPet();
        Assert.Equal(1, window.RestoreCount);
        Assert.Equal(1, window.ActivateCount);
        Assert.Equal(0, window.ShowCount);
        Assert.Equal(0, window.CloseCount);
    }

    // Purpose: Hide the pet without exiting the application.
    // Coverage: The fake window starts visible.
    // Target effect: One Hide and zero Close calls occur.
    [Fact]
    public void Hide_pet_never_closes_application()
    {
        var window = new FakeTrayPetWindow { IsVisible = true };
        var actions = new TrayApplicationActions(window, () => { });
        actions.HidePet();
        Assert.Equal(1, window.HideCount);
        Assert.Equal(0, window.CloseCount);
    }

    // Purpose: Open statistics independently of pet visibility.
    // Coverage: The fake window starts hidden.
    // Target effect: Exactly one callback and no pet Show, Restore, Activate, or Close occurs.
    [Fact]
    public void Open_statistics_preserves_hidden_pet()
    {
        var window = new FakeTrayPetWindow();
        var callbackCount = 0;
        var actions = new TrayApplicationActions(window, () => callbackCount++);
        actions.OpenStatistics();
        Assert.Equal(1, callbackCount);
        Assert.Equal(0, window.ShowCount);
        Assert.Equal(0, window.RestoreCount);
        Assert.Equal(0, window.ActivateCount);
        Assert.Equal(0, window.CloseCount);
    }

    // Purpose: Exit through the existing pet window.
    // Coverage: One action is invoked with the fake window.
    // Target effect: Exactly one Close occurs.
    [Fact]
    public void Exit_closes_existing_pet_window()
    {
        var window = new FakeTrayPetWindow();
        var actions = new TrayApplicationActions(window, () => { });
        actions.ExitApplication();
        Assert.Equal(1, window.CloseCount);
    }

    private sealed class FakeTrayPetWindow : ITrayPetWindow
    {
        public bool IsVisible { get; set; }
        public bool IsMinimized { get; set; }
        public int ShowCount { get; private set; }
        public int RestoreCount { get; private set; }
        public int HideCount { get; private set; }
        public int ActivateCount { get; private set; }
        public int CloseCount { get; private set; }
        public void Show() { ShowCount++; IsVisible = true; }
        public void Restore() => RestoreCount++;
        public void Hide() { HideCount++; IsVisible = false; }
        public void Activate() => ActivateCount++;
        public void Close() => CloseCount++;
    }
}

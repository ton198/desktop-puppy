using System.IO;
using Xunit;

namespace DesktopPuppy.UiTests;

public sealed class DesktopPuppyWindowUiTests
{
    // Purpose: Verify the pet exposes a stable and accessible Windows UI Automation entry surface.
    // Coverage: Real pet process, PetWindow AutomationId, top-level window name, tray exit command, and process exit.
    // Target effect: Automation clients can find and close the pet through its tray menu.
    [Fact]
    [Trait("Category", "Ui")]
    public void Desktop_puppy_window_exposes_a_stable_ui_automation_surface()
    {
        var executable = Environment.GetEnvironmentVariable("DESKTOP_PUPPY_EXE");
        Assert.False(
            string.IsNullOrWhiteSpace(executable),
            "Set DESKTOP_PUPPY_EXE to a built DesktopPuppy.exe to run Windows UI tests.");
        Assert.True(File.Exists(executable), $"DesktopPuppy.exe was not found at '{executable}'.");

        using var harness = DesktopPuppyUiHarness.Start(executable!);
        var window = harness.WaitForPetWindow(TimeSpan.FromSeconds(5));

        Assert.Equal("PetWindow", window.Current.AutomationId);
        Assert.Equal("Desktop Puppy", window.Current.Name);
        Assert.False(window.Current.IsOffscreen);

        harness.InvokeTrayMenu("退出桌宠");
        Assert.True(harness.WaitForExit(TimeSpan.FromSeconds(3)));
    }
}

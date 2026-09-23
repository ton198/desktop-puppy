using System.IO;
using Xunit;

namespace DesktopPuppy.UiTests;

public sealed class DesktopPuppyWindowUiTests
{
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

        Assert.Equal("Desktop Puppy", window.Current.Name);
        Assert.False(window.Current.IsOffscreen);

        var closeButton = harness.FindByAutomationId(window, "CloseButton");
        Assert.Equal("退出 Desktop Puppy", closeButton.Current.Name);
        Assert.True(closeButton.TryGetCurrentPattern(
            System.Windows.Automation.InvokePattern.Pattern,
            out _));

        harness.Invoke(closeButton);
        Assert.True(harness.WaitForExit(TimeSpan.FromSeconds(3)));
    }
}

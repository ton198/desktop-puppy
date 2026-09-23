using System.IO;
using Xunit;

namespace DesktopPuppy.UiTests;

public sealed class DesktopPuppyInteractionUiTests
{
    [Fact]
    [Trait("Category", "Ui")]
    public void Right_click_shows_a_closable_speech_bubble()
    {
        using var harness = StartHarness();
        var window = harness.WaitForPetWindow(TimeSpan.FromSeconds(5));

        harness.RightClickCenter();

        var bubble = harness.WaitForAutomationId(window, "SpeechBubbleText", TimeSpan.FromSeconds(2));
        Assert.False(bubble.Current.IsOffscreen);
        var closeBubbleButton = harness.FindByAutomationId(window, "CloseBubbleButton");
        harness.Invoke(closeBubbleButton);
        harness.WaitForAutomationIdToDisappear(window, "SpeechBubbleText", TimeSpan.FromSeconds(2));
    }

    [Fact]
    [Trait("Category", "Ui")]
    public void Double_click_shows_hit_response_bubble()
    {
        using var harness = StartHarness();
        var window = harness.WaitForPetWindow(TimeSpan.FromSeconds(5));

        harness.DoubleClickCenter();

        var bubbleText = harness.WaitForAutomationId(window, "SpeechBubbleText", TimeSpan.FromSeconds(2));
        Assert.Contains("嗷", bubbleText.Current.Name);
    }

    [Fact]
    [Trait("Category", "Ui")]
    public void Drag_moves_the_pet_window_without_closing_it()
    {
        using var harness = StartHarness();
        harness.WaitForPetWindow(TimeSpan.FromSeconds(5));
        var before = harness.GetWindowRect();
        var start = harness.GetPetInteractionPoint();
        var end = (start.X + 100, start.Y + 40);

        harness.Drag(start, end);

        var after = harness.WaitForWindowRectChange(before, TimeSpan.FromSeconds(2));
        Assert.NotEqual(before.Left, after.Left);
        Assert.NotEqual(before.Top, after.Top);
    }

    private static DesktopPuppyUiHarness StartHarness()
    {
        var executable = Environment.GetEnvironmentVariable("DESKTOP_PUPPY_EXE");
        Assert.False(string.IsNullOrWhiteSpace(executable), "Set DESKTOP_PUPPY_EXE before running UI tests.");
        Assert.True(System.IO.File.Exists(executable), $"DesktopPuppy.exe was not found at '{executable}'.");
        return DesktopPuppyUiHarness.Start(executable!);
    }
}


using System.IO;
using Xunit;

namespace DesktopPuppy.UiTests;

public sealed class DesktopPuppyInteractionUiTests
{
    [Fact]
    [Trait("Category", "Ui")]
    public void Right_click_without_keys_notifies_without_fallback_speech()
    {
        using var harness = StartHarness();
        var window = harness.WaitForPetWindow(TimeSpan.FromSeconds(5));

        harness.RightClickCenter();

        var notice = harness.WaitForAutomationName(window, "AiNotice", name => name.Contains("配置"), TimeSpan.FromSeconds(5));
        Assert.Contains("Key", notice);
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

    [Fact]
    [Trait("Category", "Ui")]
    public void Pet_plays_all_special_sequences_and_returns_to_idle()
    {
        using var harness = StartHarness();
        var window = harness.WaitForPetWindow(TimeSpan.FromSeconds(5));
        harness.WaitForAutomationName(window, "PetAnimation", name => name == "greeting-raised", TimeSpan.FromSeconds(4));
        harness.WaitForAutomationName(window, "PetAnimation", name => name == "idle", TimeSpan.FromSeconds(4));
        foreach (var peak in new[] { "tongue-peak", "glance-brow", "smile-peak", "special-tongue" })
        {
            harness.RightClickCenter();
            harness.WaitForAutomationName(window, "PetAnimation", name => name == peak, TimeSpan.FromSeconds(3));
            harness.WaitForAutomationName(window, "PetAnimation", name => name == "idle", TimeSpan.FromSeconds(3));
        }
        harness.DoubleClickCenter();
        harness.WaitForAutomationName(window, "PetAnimation", name => name == "hit-recover", TimeSpan.FromSeconds(3));
        harness.WaitForAutomationName(window, "PetAnimation", name => name == "idle", TimeSpan.FromSeconds(3));
    }

    private static DesktopPuppyUiHarness StartHarness()
    {
        var executable = Environment.GetEnvironmentVariable("DESKTOP_PUPPY_EXE");
        Assert.False(string.IsNullOrWhiteSpace(executable), "Set DESKTOP_PUPPY_EXE before running UI tests.");
        Assert.True(System.IO.File.Exists(executable), $"DesktopPuppy.exe was not found at '{executable}'.");
        return DesktopPuppyUiHarness.Start(executable!);
    }
}


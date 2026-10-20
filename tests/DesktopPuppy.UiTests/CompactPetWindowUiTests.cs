using System.Windows.Automation;
using System.Runtime.InteropServices;
using DesktopPuppy.Native;
using Xunit;

namespace DesktopPuppy.UiTests;

public sealed class CompactPetWindowUiTests
{
    [Fact]
    [Trait("Category", "Ui")]
    public void Speech_follows_across_available_displays_and_does_not_take_focus()
    {
        using var harness = Start();
        var pet = harness.WaitForPetWindow(TimeSpan.FromSeconds(5));
        harness.WaitForAutomationName(pet, "PetAnimation", n => n == "idle", TimeSpan.FromSeconds(4));
        harness.SingleClickCenter();
        var foreground = GetForegroundWindow();
        var speech = harness.WaitForSpeechWindow();
        Assert.Equal(foreground, GetForegroundWindow());
        var petHandle = new IntPtr(pet.Current.NativeWindowHandle);
        var speechHandle = new IntPtr(speech.Current.NativeWindowHandle);
        Assert.NotEqual(speechHandle, foreground);
        var api = new Win32WindowApi();
        using var displays = new Win32MonitorDisplayProvider();
        foreach (var monitor in displays.GetMonitors())
        {
            var area = monitor.WorkArea;
            Assert.True(api.TryGetWindowRect(petHandle, out var bounds));
            var left = area.Left + area.Width / 2;
            var top = area.Top + area.Height / 2;
            Assert.True(api.SetWindowPosition(petHandle, new PhysicalRect(left, top, left + bounds.Width, top + bounds.Height)));
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(3);
            PhysicalRect actual = default;
            bool settled;
            do
            {
                Thread.Sleep(50);
                settled = api.TryGetWindowRect(speechHandle, out actual) &&
                    actual.Left >= area.Left && actual.Right <= area.Right &&
                    actual.Top >= area.Top && actual.Bottom <= area.Bottom &&
                    api.GetDpiForWindow(speechHandle) == api.GetDpiForWindow(petHandle);
            } while (!settled && DateTime.UtcNow < deadline);
            Assert.True(settled, $"Speech did not follow the pet's display/DPI: speech={actual}; work={area}");
            Assert.Equal(foreground, GetForegroundWindow());
        }
    }

    [Fact]
    [Trait("Category", "Ui")]
    public void Hiding_the_pet_hides_speech_and_exit_closes_both_windows()
    {
        using var harness = Start();
        var pet = harness.WaitForPetWindow(TimeSpan.FromSeconds(5));
        harness.WaitForAutomationName(pet, "PetAnimation", n => n == "idle", TimeSpan.FromSeconds(4));
        harness.SingleClickCenter();
        harness.WaitForSpeechWindow();
        harness.InvokeTrayMenu("隐藏桌宠");
        harness.WaitForTopLevelWindowToDisappear("PetWindow", TimeSpan.FromSeconds(2));
        harness.WaitForTopLevelWindowToDisappear("PetSpeechWindow", TimeSpan.FromSeconds(2));
        harness.InvokeTrayMenu("显示桌宠");
        harness.WaitForPetWindow(TimeSpan.FromSeconds(3));
        Assert.Equal(0, harness.CountTopLevelWindows("PetSpeechWindow"));
        harness.SingleClickCenter();
        harness.WaitForSpeechWindow();
        harness.InvokeTrayMenu("退出桌宠");
        Assert.True(harness.WaitForExit(TimeSpan.FromSeconds(3)));
    }

    [Fact]
    [Trait("Category", "Ui")]
    public void Pet_window_has_only_animation_and_no_extra_layout_space()
    {
        using var harness = Start();
        var pet = harness.WaitForPetWindow(TimeSpan.FromSeconds(5));
        var api = new Win32WindowApi();
        var scale = api.GetDpiForWindow(new IntPtr(pet.Current.NativeWindowHandle)) / 96d;
        var bounds = pet.Current.BoundingRectangle;
        Assert.InRange(bounds.Width / scale, 80, 100);
        Assert.InRange(bounds.Height / scale, 115, 132);
        Assert.Empty(pet.FindAll(TreeScope.Descendants,
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button)).Cast<AutomationElement>());
    }

    [Fact]
    [Trait("Category", "Ui")]
    public void Speech_is_in_a_separate_window_and_does_not_resize_the_pet()
    {
        using var harness = Start();
        var pet = harness.WaitForPetWindow(TimeSpan.FromSeconds(5));
        harness.WaitForAutomationName(pet, "PetAnimation", n => n == "idle", TimeSpan.FromSeconds(4));
        var before = harness.GetWindowRect();
        harness.SingleClickCenter();
        try { harness.WaitForAutomationName(pet, "PetAnimation", n => n == "click-smug", TimeSpan.FromSeconds(2)); }
        catch (TimeoutException ex)
        {
            throw new InvalidOperationException($"before={before}; pet={pet.Current.BoundingRectangle}; image={harness.FindByAutomationId(pet, "PetAnimation").Current.BoundingRectangle}; click={harness.GetPetInteractionPoint()}; status={pet.Current.ItemStatus}", ex);
        }
        var speech = harness.WaitForTopLevelWindow("PetSpeechWindow", TimeSpan.FromSeconds(3));
        var text = harness.WaitForAutomationId(speech, "SpeechBubbleText", TimeSpan.FromSeconds(2));
        Assert.False(string.IsNullOrWhiteSpace(text.Current.Name));
        Assert.NotEqual(pet.Current.NativeWindowHandle, speech.Current.NativeWindowHandle);
        Assert.Equal(before, harness.GetWindowRect());
        var oldSpeech = speech.Current.BoundingRectangle;
        var start = harness.GetPetInteractionPoint();
        harness.Drag(start, (start.X + 100, start.Y + 40));
        harness.WaitForWindowRectChange(before, TimeSpan.FromSeconds(2));
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(2);
        while (speech.Current.BoundingRectangle.Top < oldSpeech.Top + 20 && DateTime.UtcNow < deadline) Thread.Sleep(30);
        Assert.True(speech.Current.BoundingRectangle.Top >= oldSpeech.Top + 20);
        harness.Invoke(harness.FindByAutomationId(speech, "CloseBubbleButton"));
        harness.WaitForAutomationIdToDisappear(speech, "SpeechBubbleText", TimeSpan.FromSeconds(2));
    }

    private static DesktopPuppyUiHarness Start()
    {
        var exe = Environment.GetEnvironmentVariable("DESKTOP_PUPPY_EXE");
        Assert.True(System.IO.File.Exists(exe), "Set DESKTOP_PUPPY_EXE to the built executable.");
        return DesktopPuppyUiHarness.Start(exe!);
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();
}

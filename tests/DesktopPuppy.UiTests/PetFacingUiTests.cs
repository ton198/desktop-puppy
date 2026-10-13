using System.Runtime.InteropServices;
using DesktopPuppy.Native;
using System.Windows.Automation;
using Xunit;
namespace DesktopPuppy.UiTests;
public sealed class PetFacingUiTests
{
    [Fact]
    [Trait("Category", "Ui")]
    public void Moving_across_monitor_midpoint_plays_turn_then_faces_inward()
    {
        var exe = Environment.GetEnvironmentVariable("DESKTOP_PUPPY_EXE");
        Assert.True(System.IO.File.Exists(exe));
        using var harness = DesktopPuppyUiHarness.Start(exe!);
        var window = harness.WaitForPetWindow(TimeSpan.FromSeconds(8));
        harness.RevealPetAndWaitForIdle(window, TimeSpan.FromSeconds(5));
        var image = harness.FindByAutomationId(window, "PetAnimation");
        var handle = new IntPtr(window.Current.NativeWindowHandle);
        using var displays = new Win32MonitorDisplayProvider();
        var area = displays.GetMonitors().First(m => m.IsPrimary).WorkArea;
        var api = new Win32WindowApi();
        Assert.True(api.TryGetWindowRect(handle, out var bounds));
        var scale = api.GetDpiForWindow(handle) / 96d;
        SetCursorPos(area.Left + area.Width / 2, area.Top + 10);
        void Move(double fraction)
        {
            var x = area.Left + (int)(area.Width * fraction) - bounds.Width / 2;
            Assert.True(api.SetWindowPosition(handle, new PhysicalRect(x, area.Top + 150, x + bounds.Width, area.Top + 150 + bounds.Height)));
        }
        Move(.3);
        Wait(image, "facing-right");
        Move(.7);
        Wait(image, "turning");
        Wait(image, "facing-left");
        Move(.5);
        Thread.Sleep(300);
        Assert.Equal("facing-left", image.Current.ItemStatus);
        Move(.3);
        Wait(image, "turning");
        Wait(image, "facing-right");
    }
    private static void Wait(AutomationElement image, string expected)
    {
        var until = DateTime.UtcNow + TimeSpan.FromSeconds(3);
        while (DateTime.UtcNow < until)
        {
            if (image.Current.ItemStatus == expected) return;
            Thread.Sleep(15);
        }
        Assert.Fail($"Expected {expected}, actual {image.Current.ItemStatus}, frame {image.Current.Name}");
    }
    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
}

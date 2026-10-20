using System.ComponentModel;
using System.Runtime.InteropServices;
using DesktopPuppy.Native;
using DesktopPuppy.Settings;
using Xunit;

namespace DesktopPuppy.UiTests;

public sealed class FullscreenOverlayUiTests
{
    [Theory]
    [InlineData("")]
    [InlineData("--startup")]
    [Trait("Category", "Ui")]
    public void Pet_appears_with_passive_fullscreen_overlay_present(string arguments)
    {
        var executable = Environment.GetEnvironmentVariable("DESKTOP_PUPPY_EXE");
        Assert.True(System.IO.File.Exists(executable), "Set DESKTOP_PUPPY_EXE to the published executable.");
        using var monitors = new Win32MonitorDisplayProvider();
        var bounds = monitors.GetMonitors().First(m => m.IsPrimary).Bounds;
        // This HWND belongs to testhost, so the child app must classify a real external
        // window. It remains present until after the pet visibility assertion.
        using var overlay = new PassiveOverlay(bounds);
        using var seed = new UsageStatisticsSeed();
        using var harness = DesktopPuppyUiHarness.Start(executable!, seed.DatabasePath, arguments);
        var pet = harness.WaitForPetWindow(TimeSpan.FromSeconds(20));
        Assert.False(pet.Current.IsOffscreen);
        Assert.NotNull(harness.FindByAutomationId(pet, "PetAnimation"));
        var log = System.IO.File.ReadAllText(LogPath(seed.DatabasePath));
        Assert.Contains("Startup.Begin", log);
        Assert.Contains("reason=NoFullscreen", log);
        Assert.Contains("Visibility shouldShow=True startupWaiting=False", log);
    }

    [Fact]
    [Trait("Category", "Ui")]
    public async Task Disabled_fullscreen_setting_is_respected_at_first_visibility_decision()
    {
        var executable = Environment.GetEnvironmentVariable("DESKTOP_PUPPY_EXE");
        Assert.True(System.IO.File.Exists(executable), "Set DESKTOP_PUPPY_EXE to the published executable.");
        using var seed = new UsageStatisticsSeed();
        var settings = new SettingsService(seed.DatabasePath);
        await settings.LoadAsync();
        await settings.SaveAsync(settings.Current with
        {
            Interaction = settings.Current.Interaction with { SuppressOnFullscreen = false }
        });
        using var monitors = new Win32MonitorDisplayProvider();
        // Layered by itself is deliberately a fullscreen candidate: only the setting
        // can prevent suppression here, independently of the passive-overlay filter.
        using var fullscreen = new PassiveOverlay(monitors.GetMonitors().First(m => m.IsPrimary).Bounds, 0x00080000);
        using var harness = DesktopPuppyUiHarness.Start(executable!, seed.DatabasePath);
        harness.WaitForPetWindow(TimeSpan.FromSeconds(10));
        var initialDecision = System.IO.File.ReadLines(LogPath(seed.DatabasePath))
            .First(line => line.Contains("Visibility shouldShow=") && line.Contains("startupWaiting=False"));
        Assert.Contains("shouldShow=True", initialDecision);
    }

    private static string LogPath(string databasePath) =>
        System.IO.Path.Combine(System.IO.Path.GetDirectoryName(databasePath)!, "startup.log");

    private sealed class PassiveOverlay : IDisposable
    {
        private readonly IntPtr _hwnd;
        public PassiveOverlay(PhysicalRect bounds, uint extendedStyle = 0x080800A0)
        {
            _hwnd = CreateWindowEx(extendedStyle, "STATIC", "DesktopPuppy test overlay", 0x90000000,
                bounds.Left, bounds.Top, bounds.Width, bounds.Height,
                IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
            if (_hwnd == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
            if (!SetLayeredWindowAttributes(_hwnd, 0, 0, 2))
            {
                var error = Marshal.GetLastWin32Error();
                DestroyWindow(_hwnd);
                throw new Win32Exception(error);
            }
        }
        public void Dispose() => DestroyWindow(_hwnd);
    }

    [DllImport("user32.dll", EntryPoint = "CreateWindowExW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowEx(uint exStyle, string className, string title, uint style,
        int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr parameter);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint color, byte alpha, uint flags);
    [DllImport("user32.dll")]
    private static extern bool DestroyWindow(IntPtr hwnd);
}

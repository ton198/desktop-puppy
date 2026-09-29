using DesktopPuppy.Native;
using Xunit;

namespace DesktopPuppy.Tests.Native;

public sealed class Win32WindowPositionerTests
{
    // Purpose: Keep HWND measurements in one physical coordinate/DPI context.
    // Coverage: Fake GetWindowRect, monitor lookup and GetDpiForWindow responses.
    // Target effect: Returned values match the native adapter output and zero/failed DPI is surfaced as failure.
    [Fact]
    public void Window_positioner_reads_physical_bounds_monitor_and_actual_dpi()
    {
        var hwnd = new IntPtr(101);
        var monitorHandle = new IntPtr(202);
        var api = new FakeWindowApi
        {
            Bounds = new PhysicalRect(-500, 40, 100, 640),
            MonitorHandle = monitorHandle,
            Dpi = 144
        };
        var displayApi = new FakeDisplayApi(new Dictionary<IntPtr, NativeMonitorInfo>
        {
            [monitorHandle] = new("DISPLAY2",
                new PhysicalRect(-1920, 0, 0, 1080),
                new PhysicalRect(-1920, 0, 0, 1040),
                true)
        });
        using var events = new FakeDisplaySettingsChangedSource();
        using var provider = new Win32MonitorDisplayProvider(displayApi, events);
        var listedMonitor = Assert.Single(provider.GetMonitors());
        var positioner = new Win32WindowPositioner(api, provider);

        Assert.Equal(api.Bounds, positioner.GetBounds(hwnd));
        Assert.Same(listedMonitor, positioner.GetMonitorForWindow(hwnd));
        Assert.Equal(144u, positioner.GetDpi(hwnd));

        api.Dpi = 0;
        Assert.Throws<System.ComponentModel.Win32Exception>(() => positioner.GetDpi(hwnd));
        Assert.Equal(hwnd, api.LastHwnd);

        api.WindowRectSucceeds = false;
        Assert.Throws<System.ComponentModel.Win32Exception>(() => positioner.GetBounds(hwnd));
        api.WindowRectSucceeds = true;
        api.SetPositionSucceeds = false;
        Assert.Throws<System.ComponentModel.Win32Exception>(() => positioner.SetBounds(hwnd, api.Bounds));
    }

    private sealed class FakeWindowApi : IWin32WindowApi
    {
        public PhysicalRect Bounds { get; init; }
        public IntPtr MonitorHandle { get; init; }
        public uint Dpi { get; set; }
        public IntPtr LastHwnd { get; private set; }
        public bool WindowRectSucceeds { get; set; } = true;
        public bool SetPositionSucceeds { get; set; } = true;

        public bool TryGetWindowRect(IntPtr hwnd, out PhysicalRect bounds)
        {
            LastHwnd = hwnd;
            bounds = Bounds;
            return WindowRectSucceeds;
        }

        public IntPtr GetMonitorFromWindow(IntPtr hwnd)
        {
            LastHwnd = hwnd;
            return MonitorHandle;
        }

        public uint GetDpiForWindow(IntPtr hwnd)
        {
            LastHwnd = hwnd;
            return Dpi;
        }

        public bool SetWindowPosition(IntPtr hwnd, PhysicalRect bounds) => SetPositionSucceeds;
    }

    private sealed class FakeDisplayApi(IReadOnlyDictionary<IntPtr, NativeMonitorInfo> monitors) : IWin32DisplayApi
    {
        public bool EnumerateMonitors(Func<IntPtr, bool> callback)
        {
            foreach (var handle in monitors.Keys)
            {
                if (!callback(handle))
                {
                    break;
                }
            }

            return true;
        }

        public bool TryGetMonitorInfo(IntPtr monitorHandle, out NativeMonitorInfo info) =>
            monitors.TryGetValue(monitorHandle, out info);
    }

    private sealed class FakeDisplaySettingsChangedSource : IDisplaySettingsChangedSource, IDisposable
    {
        private EventHandler? _displaySettingsChanged;
        public event EventHandler? DisplaySettingsChanged { add => _displaySettingsChanged += value; remove => _displaySettingsChanged -= value; }
        public void Dispose() => _displaySettingsChanged = null;
    }
}

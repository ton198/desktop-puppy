using System.ComponentModel;
using Microsoft.Win32;

namespace DesktopPuppy.Native;

public sealed class Win32MonitorDisplayProvider : IMonitorDisplayProvider, IDisposable
{
    private readonly object _gate = new();
    private readonly IWin32DisplayApi _displayApi;
    private readonly IDisplaySettingsChangedSource _displaySettings;
    private IReadOnlyList<MonitorDisplayInfo> _monitors = Array.Empty<MonitorDisplayInfo>();
    private Dictionary<IntPtr, MonitorDisplayInfo> _monitorsByHandle = new();
    private bool _disposed;

    public Win32MonitorDisplayProvider()
        : this(new Win32DisplayApi(), new SystemDisplaySettingsChangedSource())
    {
    }

    public Win32MonitorDisplayProvider(IWin32DisplayApi displayApi, IDisplaySettingsChangedSource displaySettings)
    {
        ArgumentNullException.ThrowIfNull(displayApi);
        ArgumentNullException.ThrowIfNull(displaySettings);
        _displayApi = displayApi;
        _displaySettings = displaySettings;
        _displaySettings.DisplaySettingsChanged += OnDisplaySettingsChanged;
    }

    public event EventHandler? DisplaySettingsChanged;

    public IReadOnlyList<MonitorDisplayInfo> Monitors
    {
        get
        {
            lock (_gate)
            {
                return _monitors;
            }
        }
    }

    public IReadOnlyList<MonitorDisplayInfo> GetMonitors()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var mapped = new List<MonitorDisplayInfo>();
        var byHandle = new Dictionary<IntPtr, MonitorDisplayInfo>();
        var monitorInfoFailed = false;
        Exception? mappingFailure = null;

        var enumerationSucceeded = _displayApi.EnumerateMonitors(handle =>
        {
            if (!_displayApi.TryGetMonitorInfo(handle, out var info))
            {
                monitorInfoFailed = true;
                return false;
            }

            try
            {
                var monitor = new MonitorDisplayInfo(info.DeviceId, info.Bounds, info.WorkArea, info.IsPrimary);
                mapped.Add(monitor);
                byHandle.Add(handle, monitor);
                return true;
            }
            catch (ArgumentException exception)
            {
                mappingFailure = new InvalidOperationException($"Native monitor '{info.DeviceId}' returned invalid geometry or a duplicate handle.", exception);
                return false;
            }
        });

        if (mappingFailure is not null)
        {
            throw mappingFailure;
        }

        if (!enumerationSucceeded)
        {
            throw new Win32Exception(System.Runtime.InteropServices.Marshal.GetLastWin32Error(), "Native monitor enumeration failed.");
        }

        if (monitorInfoFailed)
        {
            throw new Win32Exception(System.Runtime.InteropServices.Marshal.GetLastWin32Error(), "Could not retrieve native monitor information.");
        }

        if (mapped.Count == 0)
        {
            throw new InvalidOperationException("Native monitor enumeration returned no monitors.");
        }

        if (mapped.Count(monitor => monitor.IsPrimary) != 1)
        {
            throw new InvalidOperationException("Native monitor enumeration did not identify exactly one primary monitor.");
        }

        lock (_gate)
        {
            _monitors = mapped.AsReadOnly();
            _monitorsByHandle = byHandle;
            return _monitors;
        }
    }

    internal MonitorDisplayInfo GetMonitorForHandle(IntPtr handle)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        lock (_gate)
        {
            if (_monitorsByHandle.TryGetValue(handle, out var monitor))
            {
                return monitor;
            }
        }

        _ = GetMonitors();
        lock (_gate)
        {
            return _monitorsByHandle.TryGetValue(handle, out var monitor)
                ? monitor
                : throw new InvalidOperationException("The HWND monitor handle was not present in the current native monitor enumeration.");
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
        }

        _displaySettings.DisplaySettingsChanged -= OnDisplaySettingsChanged;
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs args)
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }
        }

        DisplaySettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    private sealed class SystemDisplaySettingsChangedSource : IDisplaySettingsChangedSource
    {
        public event EventHandler? DisplaySettingsChanged
        {
            add => SystemEvents.DisplaySettingsChanged += value;
            remove => SystemEvents.DisplaySettingsChanged -= value;
        }
    }
}

namespace DesktopPuppy.Native;

public readonly record struct NativeMonitorInfo(
    string DeviceId,
    PhysicalRect Bounds,
    PhysicalRect WorkArea,
    bool IsPrimary);

public interface IWin32DisplayApi
{
    bool EnumerateMonitors(Func<IntPtr, bool> callback);
    bool TryGetMonitorInfo(IntPtr monitorHandle, out NativeMonitorInfo info);
}

public interface IDisplaySettingsChangedSource
{
    event EventHandler? DisplaySettingsChanged;
}

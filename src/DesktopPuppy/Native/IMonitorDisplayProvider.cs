namespace DesktopPuppy.Native;

public interface IMonitorDisplayProvider
{
    IReadOnlyList<MonitorDisplayInfo> GetMonitors();
    event EventHandler? DisplaySettingsChanged;
}

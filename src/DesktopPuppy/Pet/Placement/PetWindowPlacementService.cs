using DesktopPuppy.Native;

namespace DesktopPuppy.Pet.Placement;

public sealed class PetWindowPlacementService : IDisposable
{
    private const double MinimumVisiblePhysicalPixels = PetPlacementPolicy.DefaultMinimumVisiblePixels;

    private readonly IMonitorDisplayProvider _monitors;
    private readonly INativeWindowPositioner _windows;
    private readonly Dictionary<IntPtr, WindowPlacementState> _placements = new();
    private bool _disposed;

    public PetWindowPlacementService(IMonitorDisplayProvider monitors, INativeWindowPositioner windows)
    {
        ArgumentNullException.ThrowIfNull(monitors);
        ArgumentNullException.ThrowIfNull(windows);
        _monitors = monitors;
        _windows = windows;
    }

    public void ApplyInitial(IntPtr hwnd, double widthDip, double heightDip) => Apply(hwnd, widthDip, heightDip);

    public void Reconcile(IntPtr hwnd, double widthDip, double heightDip) => Apply(hwnd, widthDip, heightDip);

    public void Forget(IntPtr hwnd)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _placements.Remove(hwnd);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _placements.Clear();
    }

    private void Apply(IntPtr hwnd, double widthDip, double heightDip)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var monitors = _monitors.GetMonitors();
        var workAreas = monitors.Select(ToWorkArea).ToArray();
        var bounds = _windows.GetBounds(hwnd);
        var windowMonitor = _windows.GetMonitorForWindow(hwnd);
        var dpi = _windows.GetDpi(hwnd);
        var width = DpiGeometryPolicy.ToPhysicalExtent(widthDip, dpi);
        var height = DpiGeometryPolicy.ToPhysicalExtent(heightDip, dpi);

        var hasPlacement = _placements.TryGetValue(hwnd, out var previousPlacement);
        var previousMonitorIsConnected = hasPlacement &&
            workAreas.Any(area => area.Id == previousPlacement!.MonitorId);
        var restoringRemovedMonitorRequest = hasPlacement && !previousMonitorIsConnected;

        MonitorDisplayInfo targetMonitor;
        PetPosition requestedPosition;
        PhysicalRect requestToCache;
        if (restoringRemovedMonitorRequest)
        {
            targetMonitor = ResolveMonitor(null, monitors, workAreas);
            var cachedRequest = previousPlacement!.RequestsByMonitorId[previousPlacement.MonitorId];
            requestedPosition = new PetPosition(cachedRequest.Left, cachedRequest.Top);
            requestToCache = new PhysicalRect(
                DpiGeometryPolicy.RoundPhysicalCoordinate(requestedPosition.Left),
                DpiGeometryPolicy.RoundPhysicalCoordinate(requestedPosition.Top),
                checked(DpiGeometryPolicy.RoundPhysicalCoordinate(requestedPosition.Left) + width),
                checked(DpiGeometryPolicy.RoundPhysicalCoordinate(requestedPosition.Top) + height));
        }
        else
        {
            targetMonitor = ResolveMonitor(windowMonitor.DeviceId, monitors, workAreas);
            requestedPosition = new PetPosition(bounds.Left, bounds.Top);
            requestToCache = new PhysicalRect(
                bounds.Left,
                bounds.Top,
                checked(bounds.Left + width),
                checked(bounds.Top + height));
        }

        var workArea = targetMonitor.WorkArea;
        var policyWorkArea = new MonitorWorkArea(
            targetMonitor.DeviceId,
            workArea.Left,
            workArea.Top,
            workArea.Width,
            workArea.Height,
            targetMonitor.IsPrimary);
        var clamped = PetPlacementPolicy.Clamp(
            requestedPosition,
            width,
            height,
            policyWorkArea,
            MinimumVisiblePhysicalPixels);
        var left = DpiGeometryPolicy.RoundPhysicalCoordinate(clamped.Left);
        var top = DpiGeometryPolicy.RoundPhysicalCoordinate(clamped.Top);
        var target = new PhysicalRect(left, top, checked(left + width), checked(top + height));

        if (target != bounds)
        {
            _windows.SetBounds(hwnd, target);
        }

        var requests = hasPlacement
            ? new Dictionary<string, PhysicalRect>(previousPlacement!.RequestsByMonitorId, StringComparer.Ordinal)
            : new Dictionary<string, PhysicalRect>(StringComparer.Ordinal);
        requests[targetMonitor.DeviceId] = restoringRemovedMonitorRequest ? target : requestToCache;
        _placements[hwnd] = new WindowPlacementState(targetMonitor.DeviceId, requests);
    }

    private static MonitorWorkArea ToWorkArea(MonitorDisplayInfo monitor) => new(
        monitor.DeviceId,
        monitor.WorkArea.Left,
        monitor.WorkArea.Top,
        monitor.WorkArea.Width,
        monitor.WorkArea.Height,
        monitor.IsPrimary);

    private static MonitorDisplayInfo ResolveMonitor(
        string? requestedMonitorId,
        IReadOnlyList<MonitorDisplayInfo> monitors,
        IReadOnlyList<MonitorWorkArea> workAreas)
    {
        var selected = PetPlacementPolicy.ResolveMonitor(requestedMonitorId, workAreas);
        return monitors.FirstOrDefault(monitor => monitor.DeviceId == selected.Id)
            ?? throw new InvalidOperationException("Monitor enumeration must include the requested monitor or a primary display.");
    }

    private sealed record WindowPlacementState(
        string MonitorId,
        Dictionary<string, PhysicalRect> RequestsByMonitorId);
}

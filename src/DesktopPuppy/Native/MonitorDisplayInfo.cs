namespace DesktopPuppy.Native;

public sealed record MonitorDisplayInfo
{
    public MonitorDisplayInfo(string deviceId, PhysicalRect bounds, PhysicalRect workArea, bool isPrimary)
    {
        if (string.IsNullOrWhiteSpace(deviceId))
        {
            throw new ArgumentException("Device ID is required.", nameof(deviceId));
        }

        if (!bounds.IsValid)
        {
            throw new ArgumentOutOfRangeException(nameof(bounds));
        }

        if (!workArea.IsValid)
        {
            throw new ArgumentOutOfRangeException(nameof(workArea));
        }

        DeviceId = deviceId;
        Bounds = bounds;
        WorkArea = workArea;
        IsPrimary = isPrimary;
    }

    public string DeviceId { get; }
    public PhysicalRect Bounds { get; }
    public PhysicalRect WorkArea { get; }
    public bool IsPrimary { get; }
}

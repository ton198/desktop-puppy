namespace DesktopPuppy.Core.Settings;
public sealed record ActivitySettings
{
    public TimeSpan GlobalCooldown { get; init; } = TimeSpan.FromMinutes(20);
    public int? DailyActivityLimit { get; init; }
    public TimeOnly QuietHoursStart { get; init; } = new(23,0);
    public TimeOnly QuietHoursEnd { get; init; } = new(8,0);
    public bool QuietHoursEnabled { get; init; } = true;
    public bool Enabled { get; init; } = true;
    public ActivityFrequency Frequency { get; init; } = ActivityFrequency.Normal;
    public bool PeekEnabled { get; init; } = true;
    public bool ProudEnabled { get; init; } = true;
    public bool FunnyFaceEnabled { get; init; } = true;
    public IReadOnlyList<string> Blacklist { get; init; } = Array.Empty<string>();
}

namespace DesktopPuppy.Core.Settings;

public sealed record StatisticsSettings
{
    public bool Enabled { get; init; } = true;
}

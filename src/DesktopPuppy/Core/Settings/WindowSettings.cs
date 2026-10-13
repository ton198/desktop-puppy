namespace DesktopPuppy.Core.Settings;
public sealed record WindowSettings
{
    public double Scale { get; init; } = 1;
    public double Opacity { get; init; } = 1;
    public bool Topmost { get; init; } = true;
}
public enum ActivityFrequency { Low, Normal, High }
public enum ApplicationCategory { Unclassified, Work, Entertainment }

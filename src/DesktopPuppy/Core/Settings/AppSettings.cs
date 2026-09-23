namespace DesktopPuppy.Core.Settings;

public sealed record AppSettings
{
    public ActivitySettings Activity { get; init; } = new();
    public StatisticsSettings Statistics { get; init; } = new();
    public InteractionSettings Interaction { get; init; } = new();
    public AiSettings Ai { get; init; } = new();
}

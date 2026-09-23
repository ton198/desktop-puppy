namespace DesktopPuppy.Core.Settings;

public sealed record InteractionSettings
{
    public bool SoundEnabled { get; init; }
    public bool AllowHit { get; init; } = true;
    public bool SuppressOnFullscreen { get; init; } = true;
}

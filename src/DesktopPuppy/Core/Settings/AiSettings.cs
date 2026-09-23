namespace DesktopPuppy.Core.Settings;

public sealed record AiSettings
{
    public bool Enabled { get; init; } = true;
    public string Provider { get; init; } = "DeepSeek";
    public string Model { get; init; } = "deepseek-flash";
    public int? DailyRequestLimit { get; init; }
}

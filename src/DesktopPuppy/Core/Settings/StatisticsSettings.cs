namespace DesktopPuppy.Core.Settings;
public sealed record StatisticsSettings
{
    public bool Enabled { get; init; } = true;
    public IReadOnlyDictionary<string,ApplicationCategory> Categories { get; init; } = new Dictionary<string,ApplicationCategory>(StringComparer.OrdinalIgnoreCase);
}

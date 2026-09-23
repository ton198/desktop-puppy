namespace DesktopPuppy.Core.Clock;

public interface IClock
{
    DateTimeOffset UtcNow { get; }
}

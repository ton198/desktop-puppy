namespace DesktopPuppy.Usage;

internal static class LocalDayBoundaryPolicy
{
    internal static DateTimeOffset StartOfDateUtc(DateOnly localDate, TimeZoneInfo timeZone)
    {
        var localMidnight = localDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        while (timeZone.IsInvalidTime(localMidnight))
        {
            localMidnight = localMidnight.AddMinutes(1);
        }

        if (timeZone.IsAmbiguousTime(localMidnight))
        {
            var earliestOffset = timeZone.GetAmbiguousTimeOffsets(localMidnight).Max();
            return new DateTimeOffset(localMidnight, earliestOffset).ToUniversalTime();
        }

        return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(localMidnight, timeZone));
    }
}

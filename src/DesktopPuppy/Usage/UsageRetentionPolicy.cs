namespace DesktopPuppy.Usage;

public static class UsageRetentionPolicy
{
    public const int RetainedLocalDates = 15;

    public static DateTimeOffset GetCutoffUtc(DateTimeOffset nowUtc, TimeZoneInfo timeZone)
    {
        ArgumentNullException.ThrowIfNull(timeZone);
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(nowUtc, timeZone).DateTime);
        var oldestRetainedDate = today.AddDays(-(RetainedLocalDates - 1));
        return LocalDayBoundaryPolicy.StartOfDateUtc(oldestRetainedDate, timeZone);
    }
}

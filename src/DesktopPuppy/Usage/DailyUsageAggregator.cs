namespace DesktopPuppy.Usage;

public sealed record DailyProcessUsage(DateOnly LocalDate, string ProcessName, TimeSpan Duration);

public sealed record DailyTitleUsage(
    DateOnly LocalDate,
    string ProcessName,
    string WindowTitle,
    TimeSpan Duration);

public sealed record DailyUsageReport(
    IReadOnlyList<DailyProcessUsage> ProcessUsage,
    IReadOnlyList<DailyTitleUsage> TitleUsage);

public static class DailyUsageAggregator
{
    public static DailyUsageReport Aggregate(
        IEnumerable<ForegroundSession> sessions,
        IEnumerable<WindowTitleInterval> titleIntervals,
        DateTimeOffset asOfUtc,
        TimeZoneInfo timeZone)
    {
        ArgumentNullException.ThrowIfNull(sessions);
        ArgumentNullException.ThrowIfNull(titleIntervals);
        ArgumentNullException.ThrowIfNull(timeZone);

        var processDurations = new Dictionary<(DateOnly Date, string ProcessName), TimeSpan>();
        foreach (var session in sessions)
        {
            ArgumentNullException.ThrowIfNull(session);
            if (session.EndedAt is not { } endedAt)
            {
                continue;
            }

            AddInterval(session.StartedAt, endedAt, timeZone, (date, duration) =>
            {
                var key = (date, session.ProcessName);
                processDurations[key] = processDurations.GetValueOrDefault(key) + duration;
            });
        }

        var titleDurations = new Dictionary<(DateOnly Date, string ProcessName, string Title), TimeSpan>();
        foreach (var interval in titleIntervals)
        {
            ArgumentNullException.ThrowIfNull(interval);
            var end = interval.EndedAt ?? asOfUtc;
            AddInterval(interval.StartedAt, end, timeZone, (date, duration) =>
            {
                var key = (date, interval.ProcessName, interval.WindowTitle);
                titleDurations[key] = titleDurations.GetValueOrDefault(key) + duration;
            });
        }

        var processUsage = processDurations
            .Select(entry => new DailyProcessUsage(entry.Key.Date, entry.Key.ProcessName, entry.Value))
            .OrderBy(item => item.LocalDate)
            .ThenBy(item => item.ProcessName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var titleUsage = titleDurations
            .Select(entry => new DailyTitleUsage(
                entry.Key.Date,
                entry.Key.ProcessName,
                entry.Key.Title,
                entry.Value))
            .OrderBy(item => item.LocalDate)
            .ThenBy(item => item.ProcessName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.WindowTitle, StringComparer.Ordinal)
            .ToArray();

        return new DailyUsageReport(processUsage, titleUsage);
    }

    private static void AddInterval(
        DateTimeOffset start,
        DateTimeOffset end,
        TimeZoneInfo timeZone,
        Action<DateOnly, TimeSpan> addDuration)
    {
        var cursor = start.ToUniversalTime();
        var endUtc = end.ToUniversalTime();
        while (cursor < endUtc)
        {
            var localCursor = TimeZoneInfo.ConvertTime(cursor, timeZone);
            var localDate = DateOnly.FromDateTime(localCursor.DateTime);
            var nextLocalMidnight = localDate.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
            var nextBoundaryUtc = ResolveLocalMidnight(nextLocalMidnight, timeZone);
            var segmentEnd = nextBoundaryUtc > cursor && nextBoundaryUtc < endUtc
                ? nextBoundaryUtc
                : endUtc;

            addDuration(localDate, segmentEnd - cursor);
            cursor = segmentEnd;
        }
    }

    private static DateTimeOffset ResolveLocalMidnight(DateTime localMidnight, TimeZoneInfo timeZone)
    {
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

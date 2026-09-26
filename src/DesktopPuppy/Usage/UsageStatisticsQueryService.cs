namespace DesktopPuppy.Usage;

public sealed record WindowTitleStatistics(string WindowTitle, TimeSpan Duration);

public sealed record ApplicationStatistics(
    string ProcessName,
    TimeSpan Duration,
    IReadOnlyList<WindowTitleStatistics> WindowTitles);

public sealed record DailyUsageStatistics(
    DateOnly LocalDate,
    TimeSpan TotalDuration,
    IReadOnlyList<ApplicationStatistics> Applications);

public interface IUsageStatisticsQuery
{
    Task<DailyUsageStatistics> QueryAsync(
        DateOnly localDate,
        CancellationToken cancellationToken = default);
}

public sealed class UsageStatisticsQueryService : IUsageStatisticsQuery
{
    private readonly UsageSqliteStore _store;
    private readonly UsageCollector _collector;
    private readonly TimeZoneInfo _timeZone;

    public UsageStatisticsQueryService(
        UsageSqliteStore store,
        UsageCollector collector,
        TimeZoneInfo timeZone)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(collector);
        ArgumentNullException.ThrowIfNull(timeZone);

        _store = store;
        _collector = collector;
        _timeZone = timeZone;
    }

    public Task<DailyUsageStatistics> QueryAsync(
        DateOnly localDate,
        CancellationToken cancellationToken = default)
    {
        var snapshot = _collector.CaptureStatisticsSnapshot();
        return Task.Run(
            () => QueryCore(localDate, snapshot, cancellationToken),
            cancellationToken);
    }

    private DailyUsageStatistics QueryCore(
        DateOnly localDate,
        UsageCollectorStatisticsSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var persistedSessions = _store.ReadSessions();
        cancellationToken.ThrowIfCancellationRequested();
        var persistedTitles = _store.ReadWindowTitleIntervals();
        cancellationToken.ThrowIfCancellationRequested();

        var sessionsById = persistedSessions.ToDictionary(
            session => session.Id,
            session => EndAtSnapshot(session, snapshot.AsOfUtc));
        if (snapshot.ActiveSession is { } activeSession && !sessionsById.ContainsKey(activeSession.Id))
        {
            sessionsById.Add(activeSession.Id, activeSession with { EndedAt = snapshot.AsOfUtc });
        }

        var titles = persistedTitles
            .Select(interval => EndAtSnapshot(interval, snapshot.AsOfUtc))
            .ToArray();
        var report = DailyUsageAggregator.Aggregate(
            sessionsById.Values,
            titles,
            snapshot.AsOfUtc,
            _timeZone);

        var titleUsageByProcess = report.TitleUsage
            .Where(item => item.LocalDate == localDate)
            .GroupBy(item => item.ProcessName)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<WindowTitleStatistics>)group
                    .OrderByDescending(item => item.Duration)
                    .ThenBy(item => item.WindowTitle, StringComparer.Ordinal)
                    .Select(item => new WindowTitleStatistics(item.WindowTitle, item.Duration))
                    .ToArray());

        var applications = report.ProcessUsage
            .Where(item => item.LocalDate == localDate)
            .OrderByDescending(item => item.Duration)
            .ThenBy(item => item.ProcessName, StringComparer.OrdinalIgnoreCase)
            .Select(item => new ApplicationStatistics(
                item.ProcessName,
                item.Duration,
                titleUsageByProcess.GetValueOrDefault(item.ProcessName) ?? []))
            .ToArray();
        var totalDuration = applications.Aggregate(
            TimeSpan.Zero,
            (total, application) => total + application.Duration);

        return new DailyUsageStatistics(localDate, totalDuration, applications);
    }

    private static ForegroundSession EndAtSnapshot(
        ForegroundSession session,
        DateTimeOffset asOfUtc)
    {
        var endedAt = session.EndedAt is not { } end || end > asOfUtc
            ? asOfUtc
            : end;
        return session with { EndedAt = endedAt };
    }

    private static WindowTitleInterval EndAtSnapshot(
        WindowTitleInterval interval,
        DateTimeOffset asOfUtc)
    {
        var endedAt = interval.EndedAt is not { } end || end > asOfUtc
            ? asOfUtc
            : end;
        return interval with { EndedAt = endedAt };
    }
}

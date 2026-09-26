namespace DesktopPuppy.Usage;

public sealed class UsageRetentionCoordinator
{
    private readonly object _gate = new();
    private readonly Action<DateTimeOffset> _pruneBefore;
    private readonly TimeZoneInfo _timeZone;
    private DateOnly? _lastSuccessfullyPrunedLocalDate;

    public UsageRetentionCoordinator(Action<DateTimeOffset> pruneBefore, TimeZoneInfo timeZone)
    {
        ArgumentNullException.ThrowIfNull(pruneBefore);
        ArgumentNullException.ThrowIfNull(timeZone);
        _pruneBefore = pruneBefore;
        _timeZone = timeZone;
    }

    public bool CheckAndPrune(DateTimeOffset nowUtc)
    {
        var localDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(nowUtc, _timeZone).DateTime);
        lock (_gate)
        {
            if (_lastSuccessfullyPrunedLocalDate == localDate)
            {
                return false;
            }

            _pruneBefore(UsageRetentionPolicy.GetCutoffUtc(nowUtc, _timeZone));
            _lastSuccessfullyPrunedLocalDate = localDate;
            return true;
        }
    }
}

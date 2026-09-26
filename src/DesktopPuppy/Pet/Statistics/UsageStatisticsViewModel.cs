using System.ComponentModel;
using System.Runtime.CompilerServices;
using DesktopPuppy.Usage;

namespace DesktopPuppy.Pet.Statistics;

public sealed class UsageStatisticsViewModel : INotifyPropertyChanged
{
    private readonly IUsageStatisticsQuery _query;
    private readonly TimeZoneInfo _timeZone;
    private readonly Func<DateTimeOffset> _utcNow;
    private long _latestRequestId;
    private IReadOnlyList<DateOnly> _availableDates;
    private DateOnly _selectedDate;
    private ApplicationStatistics? _selectedApplication;
    private DailyUsageStatistics? _statistics;
    private bool _isLoading;
    private string? _errorMessage;

    public UsageStatisticsViewModel(
        IUsageStatisticsQuery query,
        TimeZoneInfo timeZone,
        Func<DateTimeOffset> utcNow)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(timeZone);
        ArgumentNullException.ThrowIfNull(utcNow);

        _query = query;
        _timeZone = timeZone;
        _utcNow = utcNow;
        _availableDates = BuildAvailableDates();
        _selectedDate = _availableDates[0];
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public IReadOnlyList<DateOnly> AvailableDates
    {
        get => _availableDates;
        private set => SetProperty(ref _availableDates, value);
    }

    public DateOnly SelectedDate
    {
        get => _selectedDate;
        private set => SetProperty(ref _selectedDate, value);
    }

    public ApplicationStatistics? SelectedApplication
    {
        get => _selectedApplication;
        private set => SetProperty(ref _selectedApplication, value);
    }

    public DailyUsageStatistics? Statistics
    {
        get => _statistics;
        private set => SetProperty(ref _statistics, value);
    }

    public bool IsLoading
    {
        get => _isLoading;
        private set => SetProperty(ref _isLoading, value);
    }

    public string? ErrorMessage
    {
        get => _errorMessage;
        private set => SetProperty(ref _errorMessage, value);
    }

    public Task LoadAsync() => RefreshAsync();

    public async Task SelectDateAsync(DateOnly localDate)
    {
        if (SelectedDate != localDate)
        {
            SelectedDate = localDate;
            Statistics = null;
            SelectedApplication = null;
        }

        await RefreshAsync();
    }

    public void SelectApplication(string? processName)
    {
        SelectedApplication = string.IsNullOrWhiteSpace(processName)
            ? null
            : Statistics?.Applications.FirstOrDefault(
                application => string.Equals(
                    application.ProcessName,
                    processName,
                    StringComparison.OrdinalIgnoreCase));
    }

    public async Task RefreshAsync()
    {
        RefreshAvailableDates();
        var requestedDate = SelectedDate;
        var requestId = Interlocked.Increment(ref _latestRequestId);
        var selectedProcessName = SelectedApplication?.ProcessName;

        ErrorMessage = null;
        IsLoading = true;

        try
        {
            var statistics = await _query.QueryAsync(requestedDate);
            if (!IsLatestRequest(requestId, requestedDate))
            {
                return;
            }

            Statistics = statistics;
            SelectApplication(selectedProcessName);
        }
        catch (Exception exception)
        {
            if (!IsLatestRequest(requestId, requestedDate))
            {
                return;
            }

            Statistics = null;
            SelectedApplication = null;
            ErrorMessage = $"无法加载使用统计：{exception.Message}";
        }
        finally
        {
            if (IsLatestRequest(requestId, requestedDate))
            {
                IsLoading = false;
            }
        }
    }

    public void InvalidatePendingRequests()
    {
        Interlocked.Increment(ref _latestRequestId);
        IsLoading = false;
    }

    private IReadOnlyList<DateOnly> BuildAvailableDates()
    {
        var localToday = DateOnly.FromDateTime(
            TimeZoneInfo.ConvertTime(_utcNow(), _timeZone).DateTime);
        return Array.AsReadOnly(
            Enumerable.Range(0, UsageRetentionPolicy.RetainedLocalDates)
                .Select(daysAgo => localToday.AddDays(-daysAgo))
                .ToArray());
    }

    private void RefreshAvailableDates()
    {
        var refreshedDates = BuildAvailableDates();
        if (AvailableDates[0] == refreshedDates[0])
        {
            return;
        }

        AvailableDates = refreshedDates;
        if (AvailableDates.Contains(SelectedDate))
        {
            return;
        }

        SelectedDate = AvailableDates[0];
        Statistics = null;
        SelectedApplication = null;
    }

    private bool IsLatestRequest(long requestId, DateOnly requestedDate)
    {
        return requestId == Interlocked.Read(ref _latestRequestId)
            && requestedDate == SelectedDate;
    }

    private bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }
}

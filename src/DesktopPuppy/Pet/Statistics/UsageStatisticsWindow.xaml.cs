using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;
using DesktopPuppy.Usage;

namespace DesktopPuppy.Pet.Statistics;

public partial class UsageStatisticsWindow : Window
{
    private readonly UsageStatisticsViewModel _viewModel;
    private readonly DispatcherTimer _refreshTimer;
    private bool _isOpen;

    public UsageStatisticsWindow(UsageStatisticsViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        _refreshTimer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher)
        {
            Interval = TimeSpan.FromSeconds(30)
        };
        _refreshTimer.Tick += RefreshTimer_OnTick;
    }

    private async void Window_OnLoaded(object sender, RoutedEventArgs e)
    {
        _isOpen = true;
        _refreshTimer.Start();
        await _viewModel.LoadAsync();
    }

    private async void Window_OnActivated(object? sender, EventArgs e)
    {
        if (_isOpen)
        {
            await _viewModel.RefreshAsync();
        }
    }

    private void Window_OnClosed(object? sender, EventArgs e)
    {
        _isOpen = false;
        _refreshTimer.Stop();
        _viewModel.InvalidatePendingRequests();
    }

    private async void RefreshTimer_OnTick(object? sender, EventArgs e)
    {
        if (_isOpen)
        {
            await _viewModel.RefreshAsync();
        }
    }

    private async void DateSelector_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isOpen && DateSelector.SelectedItem is DateOnly selectedDate)
        {
            await _viewModel.SelectDateAsync(selectedDate);
        }
    }

    private void ApplicationList_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _viewModel.SelectApplication(
            (ApplicationList.SelectedItem as ApplicationStatistics)?.ProcessName);
    }
}

public sealed class ElapsedDurationConverter : IValueConverter
{
    public object Convert(
        object value,
        Type targetType,
        object parameter,
        CultureInfo culture)
    {
        if (value is not TimeSpan duration)
        {
            return "—";
        }

        var totalMinutes = Math.Max(0L, (long)Math.Floor(duration.TotalMinutes));
        var hours = totalMinutes / 60;
        var minutes = totalMinutes % 60;
        return $"{hours} 小时 {minutes} 分钟";
    }

    public object ConvertBack(
        object value,
        Type targetType,
        object parameter,
        CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}

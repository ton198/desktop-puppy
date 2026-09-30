using DesktopPuppy.Pet.Tray;
using Xunit;

namespace DesktopPuppy.Tests.Pet.Tray;

public sealed class TrayCommandServiceTests
{
    // Purpose: Expose tray actions only after a host initializes successfully.
    // Coverage: A fake host completes Start without error.
    // Target effect: IsAvailable changes from false to true only after successful start, and repeated Start does not create a second host start.
    [Fact]
    public void Start_success_makes_tray_available()
    {
        var host = new FakeTrayIconHost();
        using var service = new TrayCommandService(host, new FakeTrayApplicationActions());
        Assert.False(service.IsAvailable);
        service.Start();
        Assert.True(service.IsAvailable);
        service.Start();
        Assert.Equal(1, host.StartCount);
    }

    // Purpose: Restore the existing pet rather than constructing another window.
    // Coverage: A started fake host raises Show once.
    // Target effect: ShowPet is called once; Hide, Statistics, and Exit are not called.
    [Fact]
    public void Show_command_routes_to_existing_pet()
    {
        var host = new FakeTrayIconHost(); var actions = new FakeTrayApplicationActions();
        using var service = new TrayCommandService(host, actions); service.Start(); host.Raise(TrayIconCommand.ShowPet);
        Assert.Equal(1, actions.ShowCount); Assert.Equal(0, actions.HideCount); Assert.Equal(0, actions.StatisticsCount); Assert.Equal(0, actions.ExitCount);
    }

    // Purpose: Keep collection running when the pet is hidden.
    // Coverage: A started host raises Hide twice.
    // Target effect: HidePet is called twice; no Show or Exit action is called.
    [Fact]
    public void Repeated_hide_requests_never_exit_the_application()
    {
        var host = new FakeTrayIconHost(); var actions = new FakeTrayApplicationActions();
        using var service = new TrayCommandService(host, actions); service.Start(); host.Raise(TrayIconCommand.HidePet); host.Raise(TrayIconCommand.HidePet);
        Assert.Equal(2, actions.HideCount); Assert.Equal(0, actions.ShowCount); Assert.Equal(0, actions.ExitCount);
    }

    // Purpose: Open statistics without changing pet visibility.
    // Coverage: Fake actions begin with the pet hidden; host raises Statistics.
    // Target effect: Only OpenStatistics runs and the fake pet remains hidden.
    [Fact]
    public void Statistics_command_does_not_show_a_hidden_pet()
    {
        var host = new FakeTrayIconHost(); var actions = new FakeTrayApplicationActions { PetVisible = false };
        using var service = new TrayCommandService(host, actions); service.Start(); host.Raise(TrayIconCommand.OpenStatistics);
        Assert.Equal(1, actions.StatisticsCount); Assert.Equal(0, actions.ShowCount); Assert.Equal(0, actions.HideCount); Assert.False(actions.PetVisible);
    }

    // Purpose: Make shutdown deterministic and prevent commands racing with shutdown.
    // Coverage: Host raises Exit twice and then Show.
    // Target effect: ExitApplication is called once; later commands have no effect.
    [Fact]
    public void Exit_command_requests_normal_exit_once_and_ignores_later_commands()
    {
        var host = new FakeTrayIconHost(); var actions = new FakeTrayApplicationActions();
        using var service = new TrayCommandService(host, actions); service.Start(); host.Raise(TrayIconCommand.Exit); host.Raise(TrayIconCommand.Exit); host.Raise(TrayIconCommand.ShowPet);
        Assert.Equal(1, actions.ExitCount); Assert.Equal(0, actions.ShowCount);
    }

    // Purpose: Never offer Hide without a working restore entry.
    // Coverage: Fake host throws from Start and then attempts to raise a command.
    // Target effect: Start does not escape the failure, IsAvailable stays false, host is detached/disposed once, and no action runs.
    [Fact]
    public void Host_start_failure_is_nonfatal_and_blocks_tray_commands()
    {
        var host = new FakeTrayIconHost { StartException = new InvalidOperationException("test") }; var actions = new FakeTrayApplicationActions();
        var service = new TrayCommandService(host, actions);
        var exception = Record.Exception(service.Start);
        host.Raise(TrayIconCommand.ShowPet);
        Assert.Null(exception); Assert.False(service.IsAvailable); Assert.Equal(1, host.DisposeCount); Assert.Equal(0, actions.TotalCalls);
        Assert.Equal(0, host.SubscriberCount);
        service.Dispose();
        Assert.Equal(1, host.DisposeCount);
    }

    // Purpose: Keep second-instance activation independent of tray setup.
    // Coverage: Host startup fails, then activation is requested twice.
    // Target effect: Both requests route to the injected actions, while the host has only one start attempt.
    [Fact]
    public void Second_instance_activation_routes_twice_when_tray_start_fails()
    {
        var host = new FakeTrayIconHost { StartException = new InvalidOperationException("test") };
        var actions = new FakeTrayApplicationActions();
        using var service = new TrayCommandService(host, actions);

        service.Start();
        service.ActivateExistingInstance();
        service.ActivateExistingInstance();

        Assert.Equal(2, actions.ShowCount);
        Assert.Equal(0, actions.HideCount);
        Assert.Equal(0, actions.StatisticsCount);
        Assert.Equal(0, actions.ExitCount);
        Assert.False(service.IsAvailable);
        Assert.Equal(1, host.StartCount);
    }

    // Purpose: Remove callbacks/resources safely at shutdown.
    // Coverage: Dispose twice, then raise a fake-host command.
    // Target effect: Host is disposed once and no action is invoked after disposal.
    [Fact]
    public void Dispose_detaches_events_and_disposes_host_once()
    {
        var host = new FakeTrayIconHost(); var actions = new FakeTrayApplicationActions(); var service = new TrayCommandService(host, actions);
        service.Start(); service.Dispose(); service.Dispose(); host.Raise(TrayIconCommand.ShowPet);
        Assert.Equal(1, host.DisposeCount); Assert.Equal(0, actions.TotalCalls);
    }

    // These fakes exercise only the command/lifecycle seam; they do not prove Shell registration.
    private sealed class FakeTrayIconHost : ITrayIconHost
    {
        private EventHandler<TrayIconCommandEventArgs>? _commandRequested;
        public event EventHandler<TrayIconCommandEventArgs>? CommandRequested
        {
            add => _commandRequested += value;
            remove => _commandRequested -= value;
        }
        public int SubscriberCount => _commandRequested?.GetInvocationList().Length ?? 0;
        public int StartCount { get; private set; }
        public int DisposeCount { get; private set; }
        public Exception? StartException { get; init; }
        public void Start() { StartCount++; if (StartException is not null) throw StartException; }
        public void Raise(TrayIconCommand command) => _commandRequested?.Invoke(this, new TrayIconCommandEventArgs(command));
        public void Dispose() => DisposeCount++;
    }

    private sealed class FakeTrayApplicationActions : ITrayApplicationActions
    {
        public int ShowCount { get; private set; }
        public int HideCount { get; private set; }
        public int StatisticsCount { get; private set; }
        public int ExitCount { get; private set; }
        public int TotalCalls => ShowCount + HideCount + StatisticsCount + ExitCount;
        public bool PetVisible { get; set; } = true;
        public void ShowPet() { ShowCount++; PetVisible = true; }
        public void HidePet() { HideCount++; PetVisible = false; }
        public void OpenStatistics() => StatisticsCount++;
        public void ExitApplication() => ExitCount++;
    }
}

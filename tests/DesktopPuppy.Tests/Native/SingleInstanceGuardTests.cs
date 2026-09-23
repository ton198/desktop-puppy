using DesktopPuppy.Native;
using Xunit;

namespace DesktopPuppy.Tests.Native;

public sealed class SingleInstanceGuardTests
{
    [Fact]
    public async Task Second_instance_signals_the_first_instance_instead_of_acquiring_the_guard()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var mutexName = $"DesktopPuppy.Tests.Mutex.{suffix}";
        var activationName = $"DesktopPuppy.Tests.Activation.{suffix}";
        using var first = new SingleInstanceGuard(mutexName, activationName);
        using var activationReceived = new ManualResetEventSlim();

        Assert.True(first.TryAcquire(activationReceived.Set));
        var secondAcquired = await Task.Run(() =>
        {
            using var second = new SingleInstanceGuard(mutexName, activationName);
            return second.TryAcquire(() => throw new Xunit.Sdk.XunitException("Secondary instance must not own activation."));
        });

        Assert.False(secondAcquired);
        Assert.True(activationReceived.Wait(TimeSpan.FromSeconds(2)));
    }
}




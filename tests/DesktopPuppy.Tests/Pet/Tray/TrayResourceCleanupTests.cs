using DesktopPuppy.Pet.Tray;
using Xunit;

namespace DesktopPuppy.Tests.Pet.Tray;

public sealed class TrayResourceCleanupTests
{
    // Purpose: Ensure one cleanup failure does not strand later owned resources.
    // Coverage: The first cleanup action records then throws; a second records normally.
    // Target effect: Cleanup returns and invokes every action once in the supplied order.
    [Fact]
    public void Cleanup_continues_after_one_action_throws()
    {
        var order = new List<int>();

        var failure = Record.Exception(() => TrayResourceCleanup.Run(
            () =>
            {
                order.Add(1);
                throw new InvalidOperationException("simulated cleanup failure");
            },
            () => order.Add(2)));

        Assert.Null(failure);
        Assert.Equal([1, 2], order);
    }
}

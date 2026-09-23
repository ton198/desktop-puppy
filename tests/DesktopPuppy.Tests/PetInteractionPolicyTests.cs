using DesktopPuppy.Pet.Interaction;
using Xunit;

namespace DesktopPuppy.Tests;

public sealed class PetInteractionPolicyTests
{
    [Fact]
    public void Hit_is_rejected_during_cooldown_and_accepted_afterwards()
    {
        var policy = new PetInteractionPolicy(TimeSpan.FromSeconds(1));
        var firstHit = new DateTimeOffset(2026, 9, 23, 12, 0, 0, TimeSpan.Zero);

        Assert.True(policy.TryRegisterHit(firstHit));
        Assert.False(policy.TryRegisterHit(firstHit.AddMilliseconds(999)));
        Assert.True(policy.TryRegisterHit(firstHit.AddSeconds(1)));
    }
}

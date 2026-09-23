namespace DesktopPuppy.Pet.Interaction;

public sealed class PetInteractionPolicy
{
    private readonly TimeSpan _hitCooldown;
    private DateTimeOffset? _lastHitAt;

    public PetInteractionPolicy(TimeSpan hitCooldown)
    {
        if (hitCooldown < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(hitCooldown));
        }

        _hitCooldown = hitCooldown;
    }

    public bool TryRegisterHit(DateTimeOffset occurredAt)
    {
        if (_lastHitAt is { } lastHitAt && occurredAt - lastHitAt < _hitCooldown)
        {
            return false;
        }

        _lastHitAt = occurredAt;
        return true;
    }
}

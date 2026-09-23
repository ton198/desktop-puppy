namespace DesktopPuppy.Pet.Interaction;

public enum PetInteractionEventType
{
    SingleClick,
    Hit,
    RightClick,
    DragStarted,
    DragCompleted
}

public readonly record struct PointerPoint(double X, double Y)
{
    public double DistanceSquaredTo(PointerPoint other)
    {
        var deltaX = X - other.X;
        var deltaY = Y - other.Y;
        return deltaX * deltaX + deltaY * deltaY;
    }
}

public sealed record PetInteractionEvent(
    PetInteractionEventType Type,
    DateTimeOffset OccurredAt,
    PointerPoint Position);

public sealed class InteractionController
{
    private readonly PetInteractionPolicy _hitPolicy;
    private readonly TimeSpan _doubleClickWindow;
    private readonly double _dragThresholdSquared;
    private readonly Action<PetInteractionEvent> _publish;
    private DateTimeOffset? _singleClickDeadline;
    private PointerPoint _pressPoint;
    private bool _pressed;
    private bool _dragging;

    public InteractionController(
        PetInteractionPolicy hitPolicy,
        TimeSpan doubleClickWindow,
        double dragThreshold,
        Action<PetInteractionEvent> publish)
    {
        ArgumentNullException.ThrowIfNull(hitPolicy);
        ArgumentNullException.ThrowIfNull(publish);
        if (doubleClickWindow <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(doubleClickWindow));
        }

        if (dragThreshold < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(dragThreshold));
        }

        _hitPolicy = hitPolicy;
        _doubleClickWindow = doubleClickWindow;
        _dragThresholdSquared = dragThreshold * dragThreshold;
        _publish = publish;
    }

    public bool HasPendingSingleClick => _singleClickDeadline is not null;

    public void PointerPressed(PointerPoint position, DateTimeOffset occurredAt, int clickCount)
    {
        _pressPoint = position;
        _pressed = true;
        _dragging = false;

        if (clickCount >= 2)
        {
            _singleClickDeadline = null;
            if (_hitPolicy.TryRegisterHit(occurredAt))
            {
                Publish(PetInteractionEventType.Hit, occurredAt, position);
            }

            return;
        }

        _singleClickDeadline = occurredAt + _doubleClickWindow;
    }

    public void PointerMoved(PointerPoint position, DateTimeOffset occurredAt, bool leftButtonPressed)
    {
        if (!_pressed || !leftButtonPressed || _dragging)
        {
            return;
        }

        if (_pressPoint.DistanceSquaredTo(position) < _dragThresholdSquared)
        {
            return;
        }

        _singleClickDeadline = null;
        _dragging = true;
        Publish(PetInteractionEventType.DragStarted, occurredAt, position);
    }

    public void PointerReleased(PointerPoint position, DateTimeOffset occurredAt)
    {
        if (!_pressed)
        {
            return;
        }

        _pressed = false;
        if (_dragging)
        {
            _dragging = false;
            Publish(PetInteractionEventType.DragCompleted, occurredAt, position);
        }
    }

    public void Advance(DateTimeOffset now)
    {
        if (_singleClickDeadline is not { } deadline || now < deadline)
        {
            return;
        }

        _singleClickDeadline = null;
        Publish(PetInteractionEventType.SingleClick, now, _pressPoint);
    }

    public void RightClicked(DateTimeOffset occurredAt, PointerPoint position = default)
    {
        Publish(PetInteractionEventType.RightClick, occurredAt, position);
    }

    private void Publish(PetInteractionEventType type, DateTimeOffset occurredAt, PointerPoint position)
    {
        _publish(new PetInteractionEvent(type, occurredAt, position));
    }
}

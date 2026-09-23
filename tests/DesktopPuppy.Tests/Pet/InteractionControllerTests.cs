using DesktopPuppy.Pet.Interaction;
using Xunit;

namespace DesktopPuppy.Tests.Pet;

public sealed class InteractionControllerTests
{
    private static readonly DateTimeOffset Start = new(2026, 9, 23, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Single_click_is_emitted_only_after_double_click_window_expires()
    {
        var events = new List<PetInteractionEvent>();
        var controller = CreateController(events);

        controller.PointerPressed(new PointerPoint(10, 10), Start, clickCount: 1);
        controller.PointerReleased(new PointerPoint(10, 10), Start.AddMilliseconds(10));
        controller.Advance(Start.AddMilliseconds(499));

        Assert.Empty(events);

        controller.Advance(Start.AddMilliseconds(500));

        Assert.Collection(events, item => Assert.Equal(PetInteractionEventType.SingleClick, item.Type));
    }

    [Fact]
    public void Double_click_cancels_pending_single_click_and_emits_hit()
    {
        var events = new List<PetInteractionEvent>();
        var controller = CreateController(events);

        controller.PointerPressed(new PointerPoint(10, 10), Start, clickCount: 1);
        controller.PointerReleased(new PointerPoint(10, 10), Start.AddMilliseconds(10));
        controller.PointerPressed(new PointerPoint(10, 10), Start.AddMilliseconds(100), clickCount: 2);
        controller.Advance(Start.AddSeconds(1));

        Assert.Collection(events, item => Assert.Equal(PetInteractionEventType.Hit, item.Type));
    }

    [Fact]
    public void Drag_emits_drag_events_and_does_not_emit_click()
    {
        var events = new List<PetInteractionEvent>();
        var controller = CreateController(events);

        controller.PointerPressed(new PointerPoint(10, 10), Start, clickCount: 1);
        controller.PointerMoved(new PointerPoint(40, 10), Start.AddMilliseconds(50), leftButtonPressed: true);
        controller.PointerReleased(new PointerPoint(80, 10), Start.AddMilliseconds(100));
        controller.Advance(Start.AddSeconds(1));

        Assert.Collection(
            events,
            started => Assert.Equal(PetInteractionEventType.DragStarted, started.Type),
            completed => Assert.Equal(PetInteractionEventType.DragCompleted, completed.Type));
    }

    [Fact]
    public void Right_click_emits_special_interaction()
    {
        var events = new List<PetInteractionEvent>();
        var controller = CreateController(events);

        controller.RightClicked(Start);

        Assert.Collection(events, item => Assert.Equal(PetInteractionEventType.RightClick, item.Type));
    }

    private static InteractionController CreateController(List<PetInteractionEvent> events)
    {
        return new InteractionController(
            new PetInteractionPolicy(TimeSpan.FromSeconds(1)),
            TimeSpan.FromMilliseconds(500),
            dragThreshold: 8,
            events.Add);
    }
}

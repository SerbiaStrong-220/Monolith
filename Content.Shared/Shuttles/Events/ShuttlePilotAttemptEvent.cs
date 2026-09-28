// Exodus: allow a grid controller to arbitrate pilot acquisition before the current pilot is changed.
namespace Content.Shared.Shuttles.Events;

// Exodus: raised after native pilot cleanup, allowing remote controllers to restore their eye.
[ByRefEvent]
public readonly record struct ShuttlePilotStoppedEvent;

[ByRefEvent]
public record struct ShuttlePilotAttemptEvent(EntityUid User, EntityUid Console, EntityUid? Grid)
{
    public bool Cancelled;
}

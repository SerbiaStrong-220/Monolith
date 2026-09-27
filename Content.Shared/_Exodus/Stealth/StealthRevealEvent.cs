using Content.Shared.Inventory;

namespace Content.Shared._Exodus.Stealth;

/// <summary>An offensive ability exposes the actor and any cloaking equipment.</summary>
public sealed class StealthRevealEvent(EntityUid target) : EntityEventArgs, IInventoryRelayEvent
{
    public EntityUid Target = target;
    public SlotFlags TargetSlots => SlotFlags.WITHOUT_POCKET;
}

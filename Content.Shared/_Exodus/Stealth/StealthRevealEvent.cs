using Content.Shared.Inventory;

namespace Content.Shared._Exodus.Stealth;

/// <summary>Forcibly reveals the target's disguises and any equipped active cloaking devices.</summary>
public sealed class StealthRevealEvent(EntityUid target) : EntityEventArgs, IInventoryRelayEvent
{
    public EntityUid Target = target;
    public SlotFlags TargetSlots => SlotFlags.WITHOUT_POCKET;
}

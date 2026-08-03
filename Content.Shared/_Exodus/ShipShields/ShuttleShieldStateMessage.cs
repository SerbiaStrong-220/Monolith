using Robust.Shared.Serialization;

namespace Content.Shared.Exodus.ShipShields;

[Serializable, NetSerializable]
public sealed class ShuttleShieldStateMessage(ShipShieldState? state) : BoundUserInterfaceMessage
{
    public ShipShieldState? State { get; } = state;
}

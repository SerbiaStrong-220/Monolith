using Robust.Shared.Serialization;

namespace Content.Shared._Exodus.Conveyor;

[Serializable, NetSerializable]
public enum ConveyorUiKey : byte
{
    Key,
}

[Serializable, NetSerializable]
public sealed class ConveyorSetSpeedMessage : BoundUserInterfaceMessage
{
    public readonly ConveyorSpeedTier Tier;

    public ConveyorSetSpeedMessage(ConveyorSpeedTier tier)
    {
        Tier = tier;
    }
}

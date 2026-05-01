using Robust.Shared.Serialization;

namespace Content.Shared.Conveyor;

[Serializable, NetSerializable]
public enum ConveyorUiKey : byte
{
    Key,
}

[Serializable, NetSerializable]
public sealed class ConveyorSetSpeedMessage : BoundUserInterfaceMessage
{
    /// <summary>1, 2 or 3.</summary>
    public readonly byte Tier;

    public ConveyorSetSpeedMessage(byte tier)
    {
        Tier = tier;
    }
}

using Robust.Shared.Serialization;

namespace Content.Shared._Exodus.Conveyor;

[Serializable, NetSerializable]
public enum ConveyorSpeedTier : byte
{
    Low = 0,
    Medium = 1,
    High = 2,
}

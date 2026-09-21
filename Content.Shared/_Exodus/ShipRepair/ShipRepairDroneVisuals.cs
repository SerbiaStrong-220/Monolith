using Content.Shared.DoAfter;
using Robust.Shared.Serialization;

namespace Content.Shared._Exodus.ShipRepair;

[Serializable, NetSerializable]
public enum ShipRepairDroneVisuals : byte
{
    State,
}

[Serializable, NetSerializable]
public enum ShipRepairDroneState : byte
{
    Off,
    Idle,
    Repairing,
    Phased,
    Dead,
}

[Serializable, NetSerializable]
public sealed partial class ShipRepairDroneDoAfterEvent : SimpleDoAfterEvent;

using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._Exodus.ShipRepair;

[Serializable, NetSerializable]
public enum ShipRepairStationVisuals : byte
{
    Active,
}

[Serializable, NetSerializable]
public enum ShipRepairStationUiKey : byte
{
    Key,
}

[Serializable, NetSerializable]
public enum ShipRepairDroneCommand : byte
{
    Idle,
    Repair,
    Return,
}

[Serializable, NetSerializable]
public enum ShipRepairStationAction : byte
{
    Enable,
    Disable,
    Repair,
    Return,
    Recall,
    Eject,
}

[Serializable, NetSerializable]
public enum ShipRepairDroneStatus : byte
{
    Off,
    Destroyed,
    Docked,
    Idle,
    Searching,
    Moving,
    Repairing,
    Prying,
    Stuck,
    WaitingForShip,
    Returning,
    NoReturnPath,
    ExitBlocked,
    Pathfinding,
}

[Serializable, NetSerializable]
public sealed class ShipRepairStationMessage(ShipRepairStationAction action, NetEntity? drone) : BoundUserInterfaceMessage
{
    public readonly ShipRepairStationAction Action = action;
    /// <summary>Null applies the command to all registered drones.</summary>
    public readonly NetEntity? Drone = drone;
}

[Serializable, NetSerializable]
public readonly record struct ShipRepairStationDroneInfo(
    NetEntity Entity,
    EntProtoId? Prototype,
    string Name,
    ShipRepairDroneStatus Status,
    bool Enabled,
    bool Docked,
    bool Alive,
    bool Compatible,
    int RecallSeconds);

[Serializable, NetSerializable]
public sealed class ShipRepairStationUiState(bool active, int capacity, List<ShipRepairStationDroneInfo> drones)
    : BoundUserInterfaceState
{
    public readonly bool Active = active;
    public readonly int Capacity = capacity;
    public readonly List<ShipRepairStationDroneInfo> Drones = drones;
}

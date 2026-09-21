using Robust.Shared.GameStates;

namespace Content.Shared._Exodus.ShipRepair;

/// <summary>A permanent berth is reserved for each registered drone, including deployed drones.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentPause]
public sealed partial class ShipRepairStationComponent : Component
{
    [DataField]
    public string ContainerId = "repair_drones";

    /// <summary>Maximum registered drones, not merely current container occupancy.</summary>
    [DataField]
    public int Capacity = 5;

    /// <summary>Registered entities retain their berth until explicitly ejected or transferred.</summary>
    [DataField]
    public List<EntityUid> Drones = new();

    [DataField, AutoPausedField]
    public TimeSpan NextUiUpdate;

    public ShipRepairStationUiState? LastUiState;
    public EntityUid? LastGrid;
    public bool WasActive;
}

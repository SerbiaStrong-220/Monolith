namespace Content.Shared._Exodus.ShipRepair;

/// <summary>Optional repair ordering override for structures with special dependencies.</summary>
[RegisterComponent]
public sealed partial class ShipRepairPriorityComponent : Component
{
    [DataField]
    public ShipRepairStage Stage = ShipRepairStage.Structure;
}

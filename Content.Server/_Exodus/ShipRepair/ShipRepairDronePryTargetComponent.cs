namespace Content.Server._Exodus.ShipRepair;

/// <summary>
/// Marks a door handled by repair drones so their prying cannot close an already opened passage.
/// Kept on the door to protect overlapping DoAfters; it does not change player prying.
/// </summary>
[RegisterComponent]
public sealed partial class ShipRepairDronePryTargetComponent : Component
{
}

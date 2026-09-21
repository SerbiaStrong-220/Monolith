namespace Content.Shared._Exodus.ShipRepair;

/// <summary>
/// Temporary construction that repair drones may dismantle when it obstructs their work or route.
/// Requires an enabled RCDDeconstructable component, which supplies the dismantling time.
/// </summary>
[RegisterComponent]
public sealed partial class ShipRepairClearableComponent : Component
{
}

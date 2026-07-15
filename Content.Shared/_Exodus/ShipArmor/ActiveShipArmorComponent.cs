// (c) Space Exodus Team
namespace Content.Shared._Exodus.ShipArmor;

/// <summary>
/// Marker for ship armor that is currently regenerating.
/// Update only enumerates this sparse set — full-charge blocks stay out of the loop.
/// </summary>
[RegisterComponent]
public sealed partial class ActiveShipArmorComponent : Component;

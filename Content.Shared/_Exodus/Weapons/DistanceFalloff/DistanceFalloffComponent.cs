using Robust.Shared.GameStates;
using Robust.Shared.Map;

namespace Content.Shared._Exodus.Weapons.DistanceFalloff;

/// <summary>
/// Distance-based strength shared by a projectile's damage, impulse and visuals.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class DistanceFalloffComponent : Component
{
    /// <summary>Distance from the origin at which the effect reaches zero strength, in metres.</summary>
    [DataField, AutoNetworkedField]
    public float Range = 8f;

    /// <summary>Distance over which the effect retains its full strength, in metres.</summary>
    [DataField, AutoNetworkedField]
    public float FullStrengthDistance = 2f;

    /// <summary>Power applied to the remaining strength. One produces linear falloff.</summary>
    [DataField, AutoNetworkedField]
    public float Exponent = 1f;

    /// <summary>Firing position relative to the firing grid or map; assigned once when shot.</summary>
    [DataField, AutoNetworkedField]
    public EntityCoordinates? Origin;
}

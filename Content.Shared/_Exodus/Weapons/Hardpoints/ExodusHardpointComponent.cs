using Robust.Shared.GameStates;

namespace Content.Shared._Exodus.Weapons.Hardpoints;

/// <summary>
/// Optional fire interval bonus for anchored guns on this mount's grid tile.
/// Weapon classes and sizes do not restrict compatibility.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class ExodusHardpointComponent : Component
{
    /// <summary>
    /// Multiplier for the interval between shots, including shots within a burst.
    /// Values outside (0, 1) provide no bonus. Reloading and energy recharge are unaffected.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float FireIntervalMultiplier = 0.8f;
}

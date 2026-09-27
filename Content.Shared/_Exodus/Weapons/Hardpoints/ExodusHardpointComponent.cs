using Content.Shared._Mono.ShipGuns;
using Robust.Shared.GameStates;

namespace Content.Shared._Exodus.Weapons.Hardpoints;

/// <summary>
/// A ship weapon mounting point. Compatibility is checked on its grid tile when a weapon fires.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class ExodusHardpointComponent : Component
{
    /// <summary>
    /// Weapon class supported by this platform. Universal platforms accept every class.
    /// </summary>
    [DataField, AutoNetworkedField]
    public ExodusHardpointClass Class = ExodusHardpointClass.Ballistic;

    /// <summary>
    /// Largest weapon mount size supported without a firing rate penalty.
    /// </summary>
    [DataField, AutoNetworkedField]
    public ShipGunClass Size = ShipGunClass.Medium;
}

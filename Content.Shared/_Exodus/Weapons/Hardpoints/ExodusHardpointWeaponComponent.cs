using Content.Shared._Mono.ShipGuns;
using Robust.Shared.GameStates;

namespace Content.Shared._Exodus.Weapons.Hardpoints;

/// <summary>
/// Mount requirements for ship weapons. An unsuitable or missing mount slows firing, but never disables it.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class ExodusHardpointWeaponComponent : Component
{
    /// <summary>
    /// Required mount class. Universal weapons can use any platform class.
    /// </summary>
    [DataField, AutoNetworkedField]
    public ExodusHardpointClass Class = ExodusHardpointClass.Ballistic;

    /// <summary>
    /// Required mount size. This is independent of ShipGunClassComponent and its processing power cost.
    /// </summary>
    [DataField, AutoNetworkedField]
    public ShipGunClass Size = ShipGunClass.Medium;

    /// <summary>
    /// Fraction of the normal firing rate without a compatible mount, also the lower bound for undersized mounts.
    /// Applies to shot intervals and burst cooldowns; ammo handling and battery recharge are unaffected.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float MinimumFireRateMultiplier = 0.5f;

    /// <summary>
    /// Firing rate fraction lost per size above the platform's capacity. Smaller weapons receive no bonus.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float OversizeFireRatePenalty = 0.25f;
}

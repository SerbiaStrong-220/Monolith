using Content.Shared.Whitelist;

namespace Content.Shared.Weapons.Reflect;

public sealed partial class ReflectComponent
{
    /// <summary>Only reflect while this item is held by the protected user.</summary>
    [DataField]
    public bool RequiresHeld;

    /// <summary>Only reflect while the item is wielded in multiple hands.</summary>
    [DataField]
    public bool RequiresWield;

    /// <summary>Optional exclusions applied to the projectile or hitscan entity.</summary>
    [DataField]
    public EntityWhitelist? ProjectileBlacklist;
}

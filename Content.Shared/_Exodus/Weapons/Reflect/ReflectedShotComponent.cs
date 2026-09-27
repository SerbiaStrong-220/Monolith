namespace Content.Shared._Exodus.Weapons.Reflect;

/// <summary>Server-side provenance and charge accounting for one projectile or hitscan shot.</summary>
[RegisterComponent]
public sealed partial class ReflectedShotComponent : Component
{
    /// <summary>The shooter before the first reflection changed the projectile's owner.</summary>
    [DataField]
    public EntityUid? OriginalShooter;

    /// <summary>A ricochet chain can supply charges only once.</summary>
    [DataField]
    public bool ChargeGranted;
}

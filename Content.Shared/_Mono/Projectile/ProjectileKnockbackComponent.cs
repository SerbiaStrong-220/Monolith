namespace Content.Shared._Mono.Projectile;

/// <summary>
/// Applies knockback on entities hit by this projectile.
/// </summary>
[RegisterComponent]
public sealed partial class ProjectileKnockbackComponent : Component
{
    /// <summary>
    /// Knockback, in kg*m/s.
    /// </summary>
    [DataField]
    public float Knockback = 25f;

    [DataField]
    public float RotateMultiplier = 1f; // evil

    // Exodus-begin: allow personnel weapons to leave the grid's momentum unchanged.
    /// <summary>Whether a hit may push a grid, including through one of its static structures.</summary>
    [DataField]
    public bool AffectGrids = true;
    // Exodus-end
}

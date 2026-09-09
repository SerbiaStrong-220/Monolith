using Content.Shared.Body.Part;

namespace Content.Server._Exodus.Projectiles;

/// <summary>
/// Gives damage dealt by this projectile a chance to sever one attached body part.
/// </summary>
[RegisterComponent]
public sealed partial class SeveringProjectileComponent : Component
{
    /// <summary>
    /// Probability per damaging hit, between zero and one.
    /// </summary>
    [DataField]
    public float Chance = 1f;

    /// <summary>
    /// Eligible part types. Heads and torsos are excluded by default.
    /// </summary>
    [DataField]
    public HashSet<BodyPartType> Parts = [BodyPartType.Arm, BodyPartType.Leg];
}

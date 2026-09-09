namespace Content.Shared._Exodus.Weapons.Projectiles;

/// <summary>Allows projectile effects to adjust the impulse before it is applied.</summary>
[ByRefEvent]
public record struct ProjectileKnockbackEvent(EntityUid Target, float Impulse);

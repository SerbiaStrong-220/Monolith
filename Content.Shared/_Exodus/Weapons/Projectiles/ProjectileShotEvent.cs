namespace Content.Shared._Exodus.Weapons.Projectiles;

/// <summary>Raised on a projectile after the gun has assigned its shooter, velocity and rotation.</summary>
[ByRefEvent]
public readonly record struct ProjectileShotEvent;

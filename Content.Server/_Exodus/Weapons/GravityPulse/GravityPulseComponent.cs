namespace Content.Server._Exodus.Weapons.GravityPulse;

/// <summary>Runtime hit history for a projectile or its volley.</summary>
[RegisterComponent]
public sealed partial class GravityPulseComponent : Component
{
    /// <summary>Allocated once per shared volley, or on the first hit of an independent projectile.</summary>
    public HashSet<EntityUid>? HitEntities;
}

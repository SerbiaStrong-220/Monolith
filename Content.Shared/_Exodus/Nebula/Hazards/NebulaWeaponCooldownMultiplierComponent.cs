namespace Content.Shared._Exodus.Nebula.Hazards;

/// <summary>
/// Directly changes this ship weapon's cooldowns while its grid is inside any nebula.
/// This is a rate-style multiplier: cooldown durations are divided by this value.
/// Values above 1 make the weapon faster in nebulas, values below 1 make it slower.
/// Applied before <see cref="NebulaWeaponCooldownResistanceComponent"/>.
/// </summary>
[RegisterComponent]
public sealed partial class NebulaWeaponCooldownMultiplierComponent : Component
{
    /// <summary>
    /// Direct shot cycle rate in nebulas. 1 means no change, 1.25 means 25% faster
    /// shots (cooldown duration / 1.25), 0.5 means twice as slow.
    /// Server clamps values below 0.1; finite values have no upper cap.
    /// </summary>
    [DataField]
    public float ShotCooldownMultiplier = 1f;

    /// <summary>
    /// Direct reload cycle rate in nebulas. 1 means no change, 1.25 means 25% faster
    /// reloads (cooldown duration / 1.25), 0.5 means twice as slow.
    /// Server clamps values below 0.1; finite values have no upper cap.
    /// </summary>
    [DataField]
    public float ReloadCooldownMultiplier = 1f;
}

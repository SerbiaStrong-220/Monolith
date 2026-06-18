namespace Content.Shared._Exodus.Nebula.Hazards;

/// <summary>
/// Multiplies weapon cooldowns while the weapon's grid is inside this nebula.
/// Values above 1 slow weapons down, values below 1 speed them up.
/// Server clamps values below 0.1; finite values have no upper cap.
/// </summary>
[RegisterComponent]
public sealed partial class NebulaWeaponCooldownModifierComponent : Component
{
    /// <summary>
    /// Multiplies time between shots. Affects normal fire rate and burst fire interval.
    /// 4 means four times slower, 0.5 means twice as fast.
    /// </summary>
    [DataField]
    public float ShotCooldownMultiplier = 1f;

    /// <summary>
    /// Multiplies non-shot reload cooldowns, such as burst cooldown and GCS fire-control cooldown.
    /// 4 means four times slower, 0.5 means twice as fast.
    /// </summary>
    [DataField]
    public float ReloadCooldownMultiplier = 1f;
}

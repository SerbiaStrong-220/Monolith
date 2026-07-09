namespace Content.Shared._Exodus.Nebula.Hazards;

/// <summary>
/// Configures how much nebula weapon cooldown modifier this ship weapon ignores.
/// Weapons without this component use the full nebula cooldown modifier.
/// This affects marker components such as <see cref="NebulaWeaponCooldownModifierComponent"/>,
/// not the direct <see cref="NebulaWeaponCooldownMultiplierComponent"/> rate multiplier.
/// </summary>
[RegisterComponent]
public sealed partial class NebulaWeaponCooldownResistanceComponent : Component
{
    /// <summary>
    /// 0 means full nebula shot cooldown modifier applies, 0.5 halves the marker effect,
    /// 1 means the weapon fully ignores it.
    /// Values above 1 fully ignore the marker effect and then become direct shot rate overdrive
    /// only for nebulas with an explicit shot cooldown modifier.
    /// Example: nebula shot cooldown multiplier 4 and resistance 1.6 produce marker
    /// cooldown multiplier 1, then divide shot cooldown by 1.6.
    /// If this field is omitted on a prototype with this component, the weapon fully ignores it.
    /// </summary>
    [DataField]
    public float ShotCooldownResistance = 1f;

    /// <summary>
    /// 0 means full nebula reload cooldown modifier applies, 0.5 halves the marker effect,
    /// 1 means the weapon fully ignores it.
    /// Values above 1 fully ignore the marker effect and then become direct reload rate overdrive
    /// only for nebulas with an explicit reload cooldown modifier.
    /// Example: nebula reload cooldown multiplier 4 and resistance 1.6 produce marker
    /// cooldown multiplier 1, then divide reload cooldown by 1.6.
    /// If this field is omitted on a prototype with this component, the weapon fully ignores it.
    /// </summary>
    [DataField]
    public float ReloadCooldownResistance = 1f;
}

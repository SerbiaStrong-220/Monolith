namespace Content.Shared._Exodus.Nebula;

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
    /// 0 means full nebula shot cooldown modifier applies, 1 means the weapon fully ignores it.
    /// Values above 1 overcompensate slowing nebulas and turn them into shot acceleration.
    /// Example: nebula shot cooldown multiplier 4 and resistance 1.25 produce marker
    /// cooldown multiplier 0.25 before the direct weapon multiplier is applied.
    /// If this field is omitted on a prototype with this component, the weapon fully ignores it.
    /// </summary>
    [DataField]
    public float ShotCooldownResistance = 1f;

    /// <summary>
    /// 0 means full nebula reload cooldown modifier applies, 1 means the weapon fully ignores it.
    /// Values above 1 overcompensate slowing nebulas and turn them into reload acceleration.
    /// Example: nebula reload cooldown multiplier 4 and resistance 1.25 produce marker
    /// cooldown multiplier 0.25 before the direct weapon multiplier is applied.
    /// If this field is omitted on a prototype with this component, the weapon fully ignores it.
    /// </summary>
    [DataField]
    public float ReloadCooldownResistance = 1f;
}

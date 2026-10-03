// © SS220, An EULA/CLA with a hosting restriction, full text: https://raw.githubusercontent.com/SerbiaStrong-220/space-station-14/master/CLA.txt

using Content.Shared.Damage;

namespace Content.Shared._Exodus.Virology.Behaviors;

[RegisterComponent]
public sealed partial class VirusRadiophasiaComponent : Component
{
    /// <summary>Prevents replaying initialization over the state restored from a saved host.</summary>
    [DataField]
    public bool StateApplied;

    [DataField]
    public float RadiationIntensity = 0.5f;

    /// <summary>Damage healed per rad the carrier receives.</summary>
    [DataField]
    public DamageSpecifier HealPerRad = new();

    /// <summary>Damage healed per rad damage unit the carrier receives.</summary>
    [DataField]
    public DamageSpecifier HealPerDamageUnit = new();

    /// <summary>If the symptome grants the user rad immunity</summary>
    [DataField]
    public bool RadImmunity = false;

    /// <summary>We added host's radiation source, so cure only ours.</summary>
    [DataField]
    public bool AddedRadiation;

    /// <summary>Carrier's own radiation to restore to.</summary>
    [DataField]
    public float? PreviousIntensity;
}

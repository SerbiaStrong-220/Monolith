// © SS220, An EULA/CLA with a hosting restriction, full text: https://raw.githubusercontent.com/SerbiaStrong-220/space-station-14/master/CLA.txt

using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Shared._Exodus.Virology.Behaviors;

[RegisterComponent, AutoGenerateComponentPause]
public sealed partial class VirusRadiophasiaComponent : Component
{
    /// <summary>Prevents replaying initialization over the state restored from a saved host.</summary>
    [DataField]
    public bool StateApplied;

    /// <summary>Radiation emitted by the carrier, excluded from its own healing.</summary>
    [DataField]
    public float RadiationIntensity = 0.5f;

    /// <summary>Damage healed per rad the carrier receives.</summary>
    [DataField]
    public DamageSpecifier HealPerRad = new();

    /// <summary>Damage healed per rad damage unit the carrier receives.</summary>
    [DataField]
    public DamageSpecifier HealPerDamageUnit = new();

    /// <summary>Fraction of incoming radiation damage that passes through the symptom's protection.</summary>
    [DataField]
    public float RadiationDamageCoefficient = 1f;

    /// <summary>Positive healing limits approached as radiation exposure increases during one second.</summary>
    [DataField]
    public DamageSpecifier MaxHealingPerSecond = new();

    /// <summary>Accumulated healing demand before diminishing returns, shared by all radiation sources.</summary>
    [DataField]
    public Dictionary<ProtoId<DamageTypePrototype>, double> HealingDemand = new();

    /// <summary>End of the current healing interval, preserved across saving and pausing.</summary>
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan NextHealingReset;

    /// <summary>We added host's radiation source, so cure only ours.</summary>
    [DataField]
    public bool AddedRadiation;

    /// <summary>Carrier's own radiation to restore to.</summary>
    [DataField]
    public float? PreviousIntensity;
}

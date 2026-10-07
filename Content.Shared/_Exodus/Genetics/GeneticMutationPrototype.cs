using Content.Shared.Chemistry.Components;
using Content.Shared.Damage;
using Content.Shared.EntityEffects;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._Exodus.Genetics;

/// <summary>A structural gene with a separate activation minimum for each hexadecimal digit.</summary>
[Prototype]
public sealed partial class GeneticMutationPrototype : IPrototype
{
    [IdDataField] public string ID { get; private set; } = default!;
    [DataField(required: true)] public LocId Name;
    [DataField(required: true)] public LocId Description;
    [DataField] public int Instability = 10;
    /// <summary>Relative weight among inactive genes activated by radiation. Zero excludes this mutation.</summary>
    [DataField] public float RadiationWeight = 1f;
    /// <summary>Three hexadecimal minima, packed as a number. Default: D/A/C.</summary>
    [DataField] public int ActivationThreshold = 0xDAC;
    /// <summary>Private sensation sent to the carrier when this mutation becomes active.</summary>
    [DataField(required: true)] public LocId ActivationMessage;
    [DataField] public GeneticModifiers Modifiers = new();
    [DataField] public List<EntProtoId> Actions = new();
    [DataField] public DamageSpecifier PeriodicDamage = new();
    /// <summary>Incompatible genes. Checked in both directions before applying a block or sample.</summary>
    [DataField] public HashSet<ProtoId<GeneticMutationPrototype>> Conflicts = new();
    /// <summary>Reversible non-anatomical behavior. Never include organs, containers, health or appearance.</summary>
    [DataField(serverOnly: true)] public ComponentRegistry Components = new();
    /// <summary>Components whose progress survives disabling the gene, such as claw growth and absorbed radiation.</summary>
    [DataField(serverOnly: true)] public HashSet<string> PreserveComponents = new();
    /// <summary>Dedicated reagent reservoirs, created and filled only if absent. Retained while inactive to prevent refilling by toggling the gene.</summary>
    [DataField(serverOnly: true)] public Dictionary<string, Solution> InitialSolutions = new();
    /// <summary>Changes to temperature limits and damage, without changing the body's current temperature.</summary>
    [DataField(serverOnly: true)] public GeneticThermalModifiers Thermal = new();
    /// <summary>Effects evaluated once per genome interval while alive and within the specified temperature range.</summary>
    [DataField(serverOnly: true)] public List<GeneticTemperatureEffect> TemperatureEffects = new();
}

[DataDefinition]
public sealed partial class GeneticThermalModifiers
{
    [DataField] public float ColdThresholdOffset;
    [DataField] public float HeatThresholdOffset;
    [DataField] public float ColdDamageMultiplier = 1f;
    [DataField] public float HeatDamageMultiplier = 1f;
}

[DataDefinition]
public sealed partial class GeneticTemperatureEffect
{
    [DataField] public float MinimumTemperature = float.NegativeInfinity;
    [DataField] public float MaximumTemperature = float.PositiveInfinity;
    [DataField(required: true)] public List<EntityEffect> Effects = new();
}

/// <summary>Independent, source-owned contributions. These never replace organs or disease components.</summary>
[DataDefinition, Serializable, NetSerializable]
public sealed partial class GeneticModifiers
{
    /// <summary>Optional species adaptation. Conflicts use a stable prototype-id order.</summary>
    [DataField] public ProtoId<GeneticTransformationPrototype>? Transformation;
    [DataField] public bool NoBreathing;
    [DataField] public bool LowPressureImmunity;
    [DataField] public bool HighPressureImmunity;
    [DataField] public bool ColdImmunity;
    /// <summary>Blocks cold damage without preventing cooling, for cryogenic physiology.</summary>
    [DataField] public bool ColdDamageImmunity;
    [DataField] public bool HeatImmunity;
    [DataField] public float CoolingMultiplier = 1f;
    [DataField] public float FireDamageMultiplier = 1f;
    [DataField] public float MovementMultiplier = 1f;
    [DataField] public float MeleeMultiplier = 1f;
    /// <summary>Optional replacement for innate melee damage. Active gene contributions add together before multipliers.</summary>
    [DataField] public DamageSpecifier? UnarmedDamage;
    [DataField] public float StaminaMultiplier = 1f;
    [DataField] public float DamageMultiplier = 1f;
    /// <summary>Innate damage resistance, combined across active genes without replacing species or armor modifiers.</summary>
    [DataField] public DamageModifierSet DamageModifiers = new();
    /// <summary>Relative sprite and fixture size contributed by the genome.</summary>
    [DataField] public float SizeMultiplier = 1f;
    /// <summary>Prevents firing ranged weapons while preserving melee attacks.</summary>
    [DataField] public bool BlockRangedWeapons;
    /// <summary>Multipliers for the body's current food and water consumption rates.</summary>
    [DataField] public float NutritionMultiplier = 1f;
    [DataField] public float ThirstMultiplier = 1f;
    /// <summary>Multiplies electrical conductivity without changing other sources of insulation.</summary>
    [DataField] public float ConductivityMultiplier = 1f;
    /// <summary>Multiplier for newly inflicted bleeding; treatment is unaffected.</summary>
    [DataField] public float BleedingMultiplier = 1f;
    /// <summary>Additional bleeding reduction per second, without restoring blood or healing wounds.</summary>
    [DataField] public float ClottingRate;
    /// <summary>Additional nutrition consumed per second.</summary>
    [DataField] public float NutritionDrain;
    /// <summary>Multiplier for flash blindness duration, after immunity checks.</summary>
    [DataField] public float FlashDurationMultiplier = 1f;
    /// <summary>Maximum bright-light washout, from zero to one; does not illuminate dark areas.</summary>
    [DataField] public float PhotophobiaStrength;
    [DataField] public HashSet<string> BlockedStatuses = new();
    [DataField] public GeneticAbility Abilities;
}

[Flags, Serializable, NetSerializable]
public enum GeneticAbility : ushort
{
    None = 0,
    Telekinesis = 1,
    RemoteViewing = 2,
    Cloak = 4,
    Mimic = 16,
    ForcePry = 64,
    NightVision = 128,
    Deflection = 256,
    Pouch = 512,
    Glow = 1024,
    Hearing = 2048,
    Web = 4096,
    FireBreath = 8192,
    BloodExpulsion = 16384,
    Cocoon = 32768,
}

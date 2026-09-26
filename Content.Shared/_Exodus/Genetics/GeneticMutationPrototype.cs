using Content.Shared.Damage;
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
    /// <summary>Three hexadecimal minima, packed as a number. Default: D/A/C.</summary>
    [DataField] public int ActivationThreshold = 0xDAC;
    /// <summary>Private sensation sent to the carrier when this mutation becomes active.</summary>
    [DataField(required: true)] public LocId ActivationMessage;
    [DataField] public GeneticModifiers Modifiers = new();
    [DataField] public List<EntProtoId> Actions = new();
    [DataField] public DamageSpecifier PeriodicDamage = new();
}

/// <summary>Independent, source-owned contributions. These never replace organs or disease components.</summary>
[DataDefinition, Serializable, NetSerializable]
public sealed partial class GeneticModifiers
{
    [DataField] public bool NoBreathing;
    [DataField] public bool LowPressureImmunity;
    [DataField] public bool HighPressureImmunity;
    [DataField] public bool ColdImmunity;
    [DataField] public bool HeatImmunity;
    [DataField] public float MovementMultiplier = 1f;
    [DataField] public float MeleeMultiplier = 1f;
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
}

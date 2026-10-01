using Content.Shared._Exodus.Genetics;
using Content.Shared.Damage;
using Robust.Shared.Prototypes;

namespace Content.Server._Exodus.Genetics;

/// <summary>Somatic DNA, deliberately server-only and separate from forensic DNA.</summary>
[RegisterComponent, AutoGenerateComponentPause]
public sealed partial class GenomeComponent : Component
{
    [DataField] public string Context = string.Empty;
    [DataField] public List<ushort> Blocks = new();
    [DataField] public List<ushort> Baseline = new();
    /// <summary>Native mutations expressed on generation and preserved by genostabilin. Explicit block edits can disable them.</summary>
    [DataField] public List<ProtoId<GeneticMutationPrototype>> InitialMutations = new();
    [DataField] public int Revision;
    [DataField] public int Stability = 60;
    [DataField] public int StabilityCapacity = 60;
    /// <summary>Capacity is taken from the biological species once, before any genetic mimicry.</summary>
    [DataField] public bool CapacityInitialized;
    [DataField] public TimeSpan Interval = TimeSpan.FromSeconds(1);
    [DataField, AutoPausedField] public TimeSpan NextUpdate;
    /// <summary>Radiation cannot activate another mutation before this time; genome resets do not clear it.</summary>
    [DataField, AutoPausedField] public TimeSpan NextRadiationMutation;
    /// <summary>Base damage per interval below zero stability, scaled by the severity of the overload.</summary>
    [DataField] public DamageSpecifier InstabilityDamage = new() { DamageDict = new() { ["Radiation"] = 1, ["Cellular"] = 1 } };
    public HashSet<ProtoId<GeneticMutationPrototype>> Active = new();
    public Dictionary<EntProtoId, EntityUid?> Actions = new();
    public DamageSpecifier PeriodicDamage = new();
    public bool EffectsInitialized;
    /// <summary>Only components actually installed by genes are restored on removal.</summary>
    public Dictionary<string, GeneticComponentGrant> ComponentGrants = new();
    /// <summary>Progress retained while the corresponding gene is disabled; never copied by DNA samples.</summary>
    public Dictionary<string, IComponent> DormantComponents = new();
    public GeneticThermalState? ThermalState;
    public List<GeneticTemperatureEffect> TemperatureEffects = new();
}

public sealed class GeneticComponentGrant
{
    public required ProtoId<GeneticMutationPrototype> Mutation;
    public required IComponent Applied;
    public IComponent? Original;
    public bool Preserve;
}

public sealed class GeneticThermalState
{
    public required Content.Server.Temperature.Components.TemperatureComponent Component;
    public float ColdThreshold;
    public float HeatThreshold;
    public required DamageSpecifier ColdDamage;
    public required DamageSpecifier HeatDamage;
}

/// <summary>Opt-out for bodies without mutable biological DNA.</summary>
[RegisterComponent]
public sealed partial class GeneticIncompatibleComponent : Component;

/// <summary>The shuffled structural-enzyme cipher for one round, never replicated.</summary>
[RegisterComponent]
public sealed partial class GeneticsRoundComponent : Component
{
    /// <summary>Minimum positions, including empty ones. Expanded to include every registered mutation.</summary>
    [DataField] public int BlockCount = 50;
    /// <summary>Mutation rate per point of irradiation damage after protection. Zero disables radiation mutations.</summary>
    [DataField] public double RadiationMutationRate = 0.001;
    /// <summary>Minimum time between successful radiation mutations on the same body.</summary>
    [DataField] public TimeSpan RadiationMutationCooldown = TimeSpan.FromSeconds(60);
    public string Context = string.Empty;
    public List<ProtoId<GeneticMutationPrototype>?> Mutations = new();
    public List<ushort> Thresholds = new();
}

/// <summary>A copied sample. Runtime effects and entity references are never included.</summary>
[DataDefinition]
public sealed partial class GeneticSnapshot
{
    [DataField] public string Context = string.Empty;
    [DataField] public List<ushort> Blocks = new();
}

[RegisterComponent]
public sealed partial class GeneticDiskComponent : Component
{
    [DataField] public GeneticSnapshot? Sample;
    /// <summary>Changes whenever the recording is replaced or edited, invalidating pending disk operations.</summary>
    [DataField] public int Revision;
}

[RegisterComponent]
public sealed partial class GeneticInjectorComponent : Component
{
    [DataField] public string Context = string.Empty;
    [DataField] public int Block;
    [DataField] public ushort Value;
    /// <summary>A full genome replaces all recipient blocks; null selects single-block injection.</summary>
    [DataField] public GeneticSnapshot? Sample;
    /// <summary>ADMIN injectors resolve this mutation against the current round on use.</summary>
    [DataField] public ProtoId<GeneticMutationPrototype>? Mutation;
    /// <summary>Applied once per successful injection, regardless of the number of blocks.</summary>
    [DataField] public DamageSpecifier InjectionDamage = new() { DamageDict = new() { ["Poison"] = 5 } };
    [DataField] public bool Used;
    [DataField] public TimeSpan InjectionTime = TimeSpan.FromSeconds(3);
}

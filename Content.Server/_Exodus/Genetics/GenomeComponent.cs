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
    /// <summary>Mutations expressed when this body's genome is first generated. Chemical resets can disable them.</summary>
    [DataField] public List<ProtoId<GeneticMutationPrototype>> InitialMutations = new();
    [DataField] public int Revision;
    [DataField] public int Stability = 60;
    [DataField] public int StabilityCapacity = 60;
    /// <summary>Capacity is taken from the biological species once, before any genetic mimicry.</summary>
    [DataField] public bool CapacityInitialized;
    [DataField] public TimeSpan Interval = TimeSpan.FromSeconds(1);
    [DataField, AutoPausedField] public TimeSpan NextUpdate;
    [DataField] public DamageSpecifier InstabilityDamage = new() { DamageDict = new() { ["Radiation"] = 1 } };
    public HashSet<ProtoId<GeneticMutationPrototype>> Active = new();
    public Dictionary<EntProtoId, EntityUid?> Actions = new();
    public DamageSpecifier PeriodicDamage = new();
    public bool EffectsInitialized;
}

/// <summary>Opt-out for bodies without mutable biological DNA.</summary>
[RegisterComponent]
public sealed partial class GeneticIncompatibleComponent : Component;

/// <summary>The shuffled structural-enzyme cipher for one round, never replicated.</summary>
[RegisterComponent]
public sealed partial class GeneticsRoundComponent : Component
{
    /// <summary>Total positions, including empty ones. Limited to 50.</summary>
    [DataField] public int BlockCount = 50;
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

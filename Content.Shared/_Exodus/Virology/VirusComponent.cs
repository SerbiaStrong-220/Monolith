// © SS220, An EULA/CLA with a hosting restriction, full text: https://raw.githubusercontent.com/SerbiaStrong-220/space-station-14/master/CLA.txt

using Content.Shared.Chemistry.Reagent;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._Exodus.Virology;

[RegisterComponent]
public sealed partial class VirusComponent : Component
{
    /// <summary>Default name of strain. A mutated strain carries a generated name instead.</summary>
    [DataField]
    public LocId? NameLoc;

    /// <summary>Whether ordinary random outbreaks may introduce this strain.</summary>
    [DataField]
    public bool RandomOutbreakEligible = true;

    /// <summary>Symptoms this strain is made of.</summary>
    [DataField]
    public List<ProtoId<VirusSymptomPrototype>> Symptoms = [];

    /// <summary>How this strain spreads. Null = can't.</summary>
    [DataField]
    public VirusTransmission? Transmission;

    /// <summary>The infected host this strain sits in.</summary>
    [ViewVariables]
    public EntityUid Carrier;

    /// <summary>Strain prototype this was built from, null once mutated. For identity + name.</summary>
    [ViewVariables]
    public EntProtoId? Source;

    /// <summary>Display name, null = prototype/generated name.</summary>
    [ViewVariables]
    public string? Name;

    [ViewVariables]
    public VirusGenome Genome;

    /// <summary>Cure reagents for this strain.</summary>
    [ViewVariables]
    public VirusCure? Cure;

    /// <summary>Formed by merging two compatible strains: can't mutate further.</summary>
    [ViewVariables]
    public bool IsSupervirus;

    /// <summary>While set, virus is suppressed (symptoms off, not contagious). Null = active.</summary>
    [ViewVariables]
    public TimeSpan? SuppressedUntil;

    /// <summary>Per-symptom progression state, keyed by symptom prototype.</summary>
    [ViewVariables]
    public Dictionary<ProtoId<VirusSymptomPrototype>, VirusSymptomState> SymptomStates = [];

    public bool Removing;

    public string? CachedIdentity;
}

/// <summary>Progression state of one symptom within a strain.</summary>
[Serializable, NetSerializable, DataDefinition]
public sealed partial class VirusSymptomState
{
    /// <summary>Current stage.</summary>
    [DataField]
    public int Stage;

    /// <summary>When current stage began (per-stage timers measure from here).</summary>
    [DataField]
    public TimeSpan StageStartTime;

    /// <summary>Reagent that accelerates symptom's stage up.</summary>
    [DataField]
    public ProtoId<ReagentPrototype>? Accelerant;

    /// <summary>Whether a reveal chemistry has decoded this symptom (for diagnoser display).</summary>
    [DataField]
    public bool Revealed;

    /// <summary>When this symptom last rolled its manifestation emote/message.</summary>
    [DataField]
    public TimeSpan LastEmote;

    /// <summary>Current randomised wait until the next emote (0 = roll a fresh one from the stage's range).</summary>
    [DataField]
    public TimeSpan EmoteDelay;
}

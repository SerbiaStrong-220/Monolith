// © SS220, An EULA/CLA with a hosting restriction, full text: https://raw.githubusercontent.com/SerbiaStrong-220/space-station-14/master/CLA.txt

using Content.Shared.Chemistry.Reagent;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._Exodus.Virology;

[Serializable, NetSerializable, DataDefinition]
public sealed partial class VirusDescriptor
{
    /// <summary>Prototype this strain came from (identity/name), null for a mutant.</summary>
    [DataField]
    public EntProtoId? Source;

    [DataField]
    public string? Name;

    [DataField]
    public VirusGenome Genome;

    [DataField]
    public bool IsSupervirus;

    [DataField]
    public VirusCure? Cure;

    [DataField]
    public VirusTransmission? Transmission;

    /// <summary>strain's symptoms and their per-symptom snapshot.</summary>
    [DataField]
    public List<VirusSymptomSnapshot> Symptoms = [];

    /// <summary>Remaining suppression time snapshotted while the source strain was supressed.</summary>
    [DataField]
    public TimeSpan? SuppressedRemaining;

    public VirusDescriptor Clone()
    {
        var symptoms = new List<VirusSymptomSnapshot>(Symptoms.Count);
        foreach (var symptom in Symptoms)
            symptoms.Add(symptom.Clone());

        return new VirusDescriptor
        {
            Source = Source,
            Name = Name,
            Genome = Genome,
            IsSupervirus = IsSupervirus,
            Cure = Cure?.Clone(),
            Transmission = Transmission?.Clone(),
            Symptoms = symptoms,
            SuppressedRemaining = SuppressedRemaining,
        };
    }
}

[Serializable, NetSerializable, DataDefinition]
public sealed partial class VirusSymptomSnapshot
{
    [DataField]
    public ProtoId<VirusSymptomPrototype> Symptom;

    [DataField]
    public int Stage;

    /// <summary>Whether a reveal chemistry has decoded this symptom (for diagnoser display).</summary>
    [DataField]
    public bool Revealed;

    /// <summary>Reagent that accelerates this symptom's progression.</summary>
    [DataField]
    public ProtoId<ReagentPrototype>? Accelerant;

    public VirusSymptomSnapshot Clone() => new()
    {
        Symptom = Symptom,
        Stage = Stage,
        Revealed = Revealed,
        Accelerant = Accelerant,
    };
}

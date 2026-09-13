using Content.Shared.FixedPoint;
using Content.Shared.EntityTable.EntitySelectors;
using Robust.Shared.Prototypes;

namespace Content.Shared._Exodus.Virology.Lifecycle;

/// <summary>A terminal symptom that incubates offspring in its dead host.</summary>
[RegisterComponent, AutoGenerateComponentPause]
public sealed partial class VirusBroodComponent : Component
{
    [DataField(required: true)]
    public ProtoId<VirusSymptomPrototype> Symptom;

    [DataField(required: true)]
    public EntProtoId Offspring;

    /// <summary>Optional replacement for the fixed offspring prototype; rolled separately for each child.</summary>
    [DataField]
    public EntityTableSelector? OffspringTable;

    [DataField]
    public EntProtoId? BurstEffect;

    [DataField]
    public LocId? IncubatingMessage;

    [DataField]
    public TimeSpan MinDelay = TimeSpan.FromSeconds(60);

    [DataField]
    public TimeSpan MaxDelay = TimeSpan.FromSeconds(120);

    /// <summary>Positive death-threshold health per offspring, rounded up with a minimum of one.</summary>
    [DataField]
    public FixedPoint2 HealthPerOffspring = 75;

    [DataField]
    public bool Incubating;

    [DataField]
    public bool Hatched;

    [DataField]
    public TimeSpan Remaining;

    [DataField, AutoPausedField]
    public TimeSpan LastUpdate;
}

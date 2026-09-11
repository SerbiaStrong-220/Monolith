using Content.Shared._Exodus.Virology;
using Robust.Shared.Prototypes;

namespace Content.Server._Exodus.Virology.Lifecycle;

[RegisterComponent, AutoGenerateComponentPause]
public sealed partial class VirusEpidemicRuleComponent : Component
{
    [DataField(required: true)]
    public EntProtoId Virus;

    [DataField(required: true)]
    public ProtoId<VirusSymptomPrototype> Symptom;

    [DataField(required: true)]
    public LocId PreparationMessage;

    [DataField(required: true)]
    public LocId[] WarningMessages = [];

    [DataField(required: true)]
    public LocId ResurgenceMessage;

    [DataField(required: true)]
    public LocId ControlledMessage;

    [DataField(required: true)]
    public LocId RoundEndMessage;

    [DataField]
    public TimeSpan Preparation = TimeSpan.FromMinutes(30);

    [DataField]
    public TimeSpan QuietPeriod = TimeSpan.FromMinutes(10);

    [DataField]
    public int PlayersPerCarrier = 25;

    [DataField]
    public int MaxCarriers = 4;

    [DataField, AutoPausedField]
    public TimeSpan SeedAt;

    [DataField, AutoPausedField]
    public TimeSpan NextCheck;

    [DataField, AutoPausedField]
    public TimeSpan? QuietSince;

    [DataField]
    public bool Seeded;

    [DataField]
    public int SeededCount;

    [DataField]
    public int WarningStage;

    [DataField]
    public bool Controlled;
}

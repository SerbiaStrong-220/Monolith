using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;
using Content.Shared.EntityTable.EntitySelectors;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.DoAfter;
using Content.Shared.FixedPoint;
using Content.Shared.Whitelist;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._Exodus.Virology.Lifecycle;

[RegisterComponent, AutoGenerateComponentPause]
public sealed partial class RotLarvaComponent : Component
{
    [DataField]
    public EntityUid? Nest;

    [DataField]
    public VirusDescriptor? Strain;

    [DataField(required: true)]
    public EntProtoId InitialVirus;

    [DataField(required: true)]
    public EntProtoId Offspring;

    [DataField]
    public EntityTableSelector? OffspringTable;

    [DataField]
    public int Satiety;

    [DataField]
    public int MaxSatiety = 4;

    [DataField]
    public int MealsPerCorpse = 4;

    [DataField]
    public float SearchRange = 8f;

    [DataField]
    public TimeSpan BiteInterval = TimeSpan.FromSeconds(3);

    [DataField]
    public TimeSpan HatchDelay = TimeSpan.FromMinutes(1);

    [DataField]
    public LocId? PupaName;

    [DataField]
    public HashSet<ProtoId<ReagentPrototype>> BloodReagents = [];

    [DataField]
    public FixedPoint2 BloodPerBite = 2.5;

    [DataField]
    public DoAfterId? FeedDoAfter;

    [DataField]
    public TimeSpan ShelterSearchMin = TimeSpan.FromSeconds(24);

    [DataField]
    public TimeSpan ShelterSearchMax = TimeSpan.FromSeconds(48);

    [DataField]
    public EntityWhitelist ShelterWalls = new() { Tags = new() { "Wall" } };

    [DataField]
    public int MinimumShelterWalls = 2;

    [DataField]
    public int PreferredShelterWalls = 3;

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan? PupateBy;

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan? HatchAt;
}

[Serializable, NetSerializable]
public enum RotLarvaVisuals : byte
{
    State,
}

[Serializable, NetSerializable]
public enum RotLarvaState : byte
{
    Hungry,
    Sated,
    Pupa,
    Dead,
}

[Serializable, NetSerializable]
public sealed partial class RotLarvaFeedEvent : SimpleDoAfterEvent;

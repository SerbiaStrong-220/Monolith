using Content.Shared.EntityTable.EntitySelectors;
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

    [DataField, AutoPausedField]
    public TimeSpan? NextBite;

    [DataField, AutoPausedField]
    public TimeSpan? HatchAt;
}

[Serializable, NetSerializable]
public enum RotLarvaVisuals : byte
{
    Sated,
}

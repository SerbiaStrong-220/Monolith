using Robust.Shared.Prototypes;
using Content.Shared.EntityTable.EntitySelectors;

namespace Content.Shared._Exodus.Virology.Lifecycle;

/// <summary>A vector which spreads its parent's strain through melee attacks.</summary>
[RegisterComponent, AutoGenerateComponentPause]
public sealed partial class VirusOffspringComponent : Component
{
    [DataField]
    public VirusDescriptor? Strain;

    /// <summary>Used for directly spawned specimens; offspring inherit an actual strain instead.</summary>
    [DataField]
    public EntProtoId? InitialVirus;

    [DataField(required: true)]
    public EntProtoId Remains;

    [DataField]
    public EntityTableSelector? RemainsTable;

    [DataField]
    public bool RemainsOnDeath;

    /// <summary>Whether an NPC stops attacking a host that can no longer acquire its strain.</summary>
    [DataField]
    public bool RetargetAfterInfection = true;

    [DataField]
    public EntProtoId? DecayEffect;

    [DataField]
    public TimeSpan? Lifetime = TimeSpan.FromMinutes(5);

    [DataField, AutoPausedField]
    public TimeSpan ExpiresAt;

    [DataField]
    public float InfectionChance = 0.65f;

    [DataField]
    public float SearchRange = 10f;

    [DataField]
    public bool Finished;
}

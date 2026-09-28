using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;
using Content.Shared.EntityTable.EntitySelectors;
using Robust.Shared.Prototypes;

namespace Content.Server._Exodus.Virology.Intelligent;

[RegisterComponent, AutoGenerateComponentPause]
public sealed partial class RotNurseryComponent : Component
{
    [DataField(required: true)] public EntityTableSelector Offspring = default!;
    [DataField] public EntProtoId? Selected;
    [DataField] public List<EntProtoId> RemainingOffspring = [];
    [DataField] public TimeSpan Duration = TimeSpan.FromSeconds(120);
    [DataField] public TimeSpan Remaining = TimeSpan.FromSeconds(120);
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField] public TimeSpan LastUpdate;
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField] public TimeSpan NextUpdate;
    [DataField] public bool Finished;
    [DataField] public EntProtoId HatchEffect = "RotNurseryHatchEffect";
}

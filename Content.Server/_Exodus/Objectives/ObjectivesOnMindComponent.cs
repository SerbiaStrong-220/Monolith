using Content.Shared.Objectives.Components;
using Robust.Shared.Prototypes;

namespace Content.Server._Exodus.Objectives;

/// <summary>Fixed objectives granted on taking control of a survival or other non-antagonist role.</summary>
[RegisterComponent]
public sealed partial class ObjectivesOnMindComponent : Component
{
    [DataField(required: true)]
    public List<EntProtoId<ObjectiveComponent>> Objectives = new();

    [DataField]
    public HashSet<EntityUid> GrantedMinds = new();
}

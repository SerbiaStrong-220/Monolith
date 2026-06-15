using Content.Shared.NPC.Prototypes;
using Robust.Shared.Prototypes;

namespace Content.Server._Exodus.Territory;

[RegisterComponent]
public sealed partial class TerritoryNpcFactionSourceComponent : Component
{
    [DataField]
    public ProtoId<NpcFactionPrototype>? AppliedFaction;

    [DataField]
    public bool HadFactionBefore;
}

using Content.Shared.NPC.Prototypes;
using Robust.Shared.Prototypes;

namespace Content.Shared._Mono.Company;

public sealed partial class CompanyPrototype
{
    /// <summary>
    /// Optional NPC faction applied to players spawned with this company selected.
    /// </summary>
    [DataField]
    public ProtoId<NpcFactionPrototype>? NpcFaction { get; private set; }
}

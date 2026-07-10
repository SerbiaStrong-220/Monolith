using Content.Shared._Mono.Company;
using Content.Shared.NPC.Systems;
using Robust.Shared.Prototypes;

namespace Content.Server._Mono.Company;

public sealed partial class CompanySystem
{
    [Dependency] private NpcFactionSystem _npcFaction = default!;

    private void ApplyNpcFaction(EntityUid mob, ProtoId<CompanyPrototype> companyId)
    {
        if (!_prototypeManager.TryIndex(companyId, out var company) ||
            company.NpcFaction is not { } npcFaction)
        {
            return;
        }

        _npcFaction.AddFaction(mob, npcFaction.Id);
    }
}

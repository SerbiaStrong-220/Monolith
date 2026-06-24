using Content.Shared._Exodus.Territory;
using Content.Shared.NPC.Prototypes;
using Content.Shared.NPC.Systems;
using Robust.Shared.Prototypes;

namespace Content.Server._Exodus.Territory;

public sealed partial class GridTerritoryNpcFactionSystem : EntitySystem
{
    [Dependency] private IPrototypeManager _prototype = default!;
    [Dependency] private NpcFactionSystem _npcFaction = default!;

    public void SyncControllerFaction(EntityUid grid, ProtoId<TerritoryFactionPrototype>? territoryFaction)
    {
        ClearAppliedFaction(grid);

        if (territoryFaction == null)
            return;

        if (!_prototype.TryIndex(territoryFaction.Value, out var factionPrototype) ||
            factionPrototype.NpcFaction is not { } npcFaction)
        {
            return;
        }

        ApplyFaction(grid, npcFaction);
    }

    private void ApplyFaction(EntityUid grid, ProtoId<NpcFactionPrototype> faction)
    {
        var alreadyHadFaction = _npcFaction.IsMember(grid, faction.Id);

        _npcFaction.AddFaction(grid, faction.Id);

        var source = EnsureComp<TerritoryNpcFactionSourceComponent>(grid);
        source.AppliedFaction = faction;
        source.HadFactionBefore = alreadyHadFaction;
    }

    private void ClearAppliedFaction(EntityUid grid)
    {
        if (!TryComp<TerritoryNpcFactionSourceComponent>(grid, out var source) ||
            source.AppliedFaction is not { } faction)
        {
            return;
        }

        if (!source.HadFactionBefore)
            _npcFaction.RemoveFactionAndRemoveEmpty(grid, faction.Id);

        RemComp<TerritoryNpcFactionSourceComponent>(grid);
    }
}

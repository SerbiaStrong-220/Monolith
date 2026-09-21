using Content.Shared.Chemistry.Reaction;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.FixedPoint;
using Content.Shared.Whitelist;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.Server._Exodus.Chemistry.TileReactions;

/// <summary>Removes anchored entities matching a configurable whitelist on the affected tile.</summary>
[DataDefinition]
public sealed partial class RemoveAnchoredEntitiesTileReaction : ITileReaction
{
    /// <summary>Entities that the reagent can remove.</summary>
    [DataField(required: true)]
    public EntityWhitelist Whitelist = new();

    /// <summary>Reagent consumed per tile when at least one matching entity is removed.</summary>
    [DataField]
    public FixedPoint2 Usage = FixedPoint2.New(1);

    public FixedPoint2 TileReact(TileRef tile, ReagentPrototype reagent, FixedPoint2 reactVolume,
        IEntityManager entityManager, List<ReagentData>? data)
    {
        if (reactVolume <= FixedPoint2.Zero || reactVolume < Usage
            || !entityManager.TryGetComponent<MapGridComponent>(tile.GridUid, out var grid))
        {
            return FixedPoint2.Zero;
        }

        var whitelist = entityManager.System<EntityWhitelistSystem>();
        var entities = entityManager.System<SharedMapSystem>().GetAnchoredEntitiesEnumerator(tile.GridUid, grid, tile.GridIndices);
        var removed = false;
        while (entities.MoveNext(out var uid))
        {
            if (entityManager.IsQueuedForDeletion(uid.Value) || !whitelist.IsValid(Whitelist, uid.Value))
                continue;

            entityManager.QueueDeleteEntity(uid.Value);
            removed = true;
        }

        return removed ? Usage : FixedPoint2.Zero;
    }
}

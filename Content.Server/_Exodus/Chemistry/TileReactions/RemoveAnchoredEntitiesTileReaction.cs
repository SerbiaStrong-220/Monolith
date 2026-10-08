using Content.Shared.Chemistry.Reaction;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.FixedPoint;
using Content.Shared.Whitelist;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.Server._Exodus.Chemistry.TileReactions;

/// <summary>Removes anchored entities matching a configurable whitelist on and around the affected tile.</summary>
[DataDefinition]
public sealed partial class RemoveAnchoredEntitiesTileReaction : ITileReaction
{
    /// <summary>Entities that the reagent can remove.</summary>
    [DataField(required: true)]
    public EntityWhitelist Whitelist = new();

    /// <summary>Radius in tiles, including diagonals. Zero affects only the reacting tile.</summary>
    [DataField]
    public int TileRadius;

    /// <summary>Reagent consumed per reaction when at least one matching entity is removed.</summary>
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
        var map = entityManager.System<SharedMapSystem>();
        var radius = Math.Max(0, TileRadius);
        var removed = false;
        for (var x = -radius; x <= radius; x++)
        {
            for (var y = -radius; y <= radius; y++)
            {
                var indices = tile.GridIndices + new Vector2i(x, y);
                var entities = map.GetAnchoredEntitiesEnumerator(tile.GridUid, grid, indices);
                while (entities.MoveNext(out var uid))
                {
                    if (entityManager.IsQueuedForDeletion(uid.Value) || !whitelist.IsValid(Whitelist, uid.Value))
                        continue;

                    entityManager.QueueDeleteEntity(uid.Value);
                    removed = true;
                }
            }
        }

        return removed ? Usage : FixedPoint2.Zero;
    }
}

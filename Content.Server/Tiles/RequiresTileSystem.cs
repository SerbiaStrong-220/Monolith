using Content.Shared.Tiles;
using Robust.Shared.Map.Components;
using Robust.Shared.Map.Enumerators;

namespace Content.Server.Tiles;

public sealed partial class RequiresTileSystem : EntitySystem
{
    /*
     * Needs to be on server as client can't predict QueueDel.
     */

    [Dependency] private SharedMapSystem _maps = default!;

    private EntityQuery<RequiresTileComponent> _tilesQuery;

    public override void Initialize()
    {
        base.Initialize();
        _tilesQuery = GetEntityQuery<RequiresTileComponent>();
        SubscribeLocalEvent<TileChangedEvent>(OnTileChange,
            before: [typeof(SharedTransformSystem)]); // Exodus: delete before removed tiles deparent their entities
    }

    private void OnTileChange(ref TileChangedEvent ev)
    {
        if (!TryComp<MapGridComponent>(ev.Entity, out var grid))
            return;

        foreach (var change in ev.Changes)
        {
            var anchored = _maps.GetAnchoredEntitiesEnumerator(ev.Entity, grid, change.GridIndices);

            while (anchored.MoveNext(out var ent))
            {
                if (!_tilesQuery.HasComponent(ent.Value))
                    continue;

                QueueDel(ent.Value);
            }
        }
    }
}

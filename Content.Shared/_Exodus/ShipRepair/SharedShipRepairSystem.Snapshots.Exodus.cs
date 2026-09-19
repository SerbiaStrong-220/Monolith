using System.Diagnostics.CodeAnalysis;
using Content.Shared._Mono.ShipRepair.Components;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;

namespace Content.Shared._Mono.ShipRepair;

public abstract partial class SharedShipRepairSystem
{
    /// <summary>
    /// Builds a replacement snapshot without modifying the current repair data.
    /// </summary>
    public bool TryCreateRepairData(EntityUid gridUid, [NotNullWhen(true)] out ShipRepairDataComponent? snapshot)
    {
        snapshot = null;
        if (TerminatingOrDeleted(gridUid) || !TryComp<MapGridComponent>(gridUid, out var grid))
            return false;

        var repairData = new ShipRepairDataComponent();
        if (TryComp<ShipRepairDataComponent>(gridUid, out var current))
            repairData.ChunkSize = current.ChunkSize;

        if (repairData.ChunkSize <= 0)
            return false;

        var chunkSize = repairData.ChunkSize;

        // tile snapshot
        var tiles = _map.GetAllTilesEnumerator(gridUid, grid);
        while (tiles.MoveNext(out var mTileRef))
        {
            if (mTileRef == null)
                continue;
            var tileRef = mTileRef.Value;

            var gridIndices = tileRef.GridIndices;
            var chunk = GetCreateChunk(repairData, gridIndices);

            var rel = GetRelativeIndices(gridIndices, chunkSize);
            chunk.Tiles[rel.X + rel.Y * chunkSize] = tileRef.Tile.TypeId;
        }

        // entities snapshot
        var repairables = new HashSet<Entity<ShipRepairableComponent>>();
        _lookup.GetLocalEntitiesIntersecting(gridUid, grid.LocalAABB, repairables);
        foreach (var childEnt in repairables)
        {
            if (TerminatingOrDeleted(childEnt))
                continue;

            var childXform = Transform(childEnt);
            // only ents directly parented to grid and anchored
            if (childXform.ParentUid != gridUid || !childXform.Anchored)
                continue;

            var query = new ShipRepairStoreQueryEvent(true);
            RaiseLocalEvent(childEnt, ref query);
            if (!query.Repairable)
                continue;

            var maybeProtoId = childEnt.Comp.RepairTo;
            if (maybeProtoId == null)
            {
                var meta = MetaData(childEnt);
                if (meta.EntityPrototype == null)
                    continue;
                maybeProtoId = new EntProtoId(meta.EntityPrototype.ID);
            }
            var protoId = maybeProtoId.Value;

            var paletteIndex = repairData.EntityPalette.IndexOf(protoId);
            if (paletteIndex == -1)
            {
                repairData.EntityPalette.Add(protoId);
                paletteIndex = repairData.EntityPalette.Count - 1;
            }

            var localPos = childXform.LocalPosition;
            var gridIndices = _map.LocalToTile(gridUid, grid, childXform.Coordinates);
            var chunk = GetCreateChunk(repairData, gridIndices);

            chunk.Entities[chunk.NextUid++] = new ShipRepairEntitySpecifier
            {
                ProtoIndex = paletteIndex,
                OriginalEntity = GetNetEntity(childEnt),
                Rotation = childXform.LocalRotation,
                LocalPosition = localPos
            };
        }

        snapshot = repairData;
        return true;
    }

    /// <summary>
    /// Publishes a completed snapshot on a live grid. Callers validate the grid before committing.
    /// </summary>
    public void ApplyRepairData(Entity<ShipRepairDataComponent> grid, ShipRepairDataComponent snapshot)
    {
        if (TerminatingOrDeleted(grid) || grid.Comp.Deleted)
            throw new InvalidOperationException("Cannot replace the repair snapshot of a deleted grid.");

        var oldChunkSize = grid.Comp.ChunkSize;
        var oldChunks = grid.Comp.Chunks;
        var oldPalette = grid.Comp.EntityPalette;
        var oldRevision = grid.Comp.Revision;
        try
        {
            grid.Comp.ChunkSize = snapshot.ChunkSize;
            grid.Comp.Chunks = snapshot.Chunks;
            grid.Comp.EntityPalette = snapshot.EntityPalette;
            grid.Comp.Revision++;
            Dirty(grid);
        }
        catch
        {
            grid.Comp.ChunkSize = oldChunkSize;
            grid.Comp.Chunks = oldChunks;
            grid.Comp.EntityPalette = oldPalette;
            grid.Comp.Revision = oldRevision;
            throw;
        }
    }
}

using Content.Shared._Mono.ShipRepair.Components;

namespace Content.Shared._Mono.ShipRepair;

public abstract partial class SharedShipRepairSystem
{
    private bool IsClearableRepairObject(EntityUid grid, EntityUid uid)
    {
        return !TerminatingOrDeleted(uid) && !EntityManager.IsQueuedForDeletion(uid) && _repairClearableQuery.HasComponent(uid) &&
               _repairDeconstructableQuery.TryGetComponent(uid, out var rcd) && rcd.Deconstructable &&
               _repairTransformQuery.TryGetComponent(uid, out var xform) && xform.Anchored && xform.ParentUid == grid;
    }

    /// <summary>Only temporary objects on the serviced grid, never intended snapshot occupants.</summary>
    public bool CanClearRepairObstruction(EntityUid grid, EntityUid uid)
    {
        if (!IsClearableRepairObject(grid, uid) || !_repairGridQuery.TryGetComponent(grid, out var mapGrid) ||
            !TryComp<ShipRepairDataComponent>(grid, out var data) || data.ChunkSize <= 0)
            return false;
        var tile = _map.LocalToTile(grid, mapGrid, Transform(uid).Coordinates);
        if (!TryGetChunk(data, tile, out var chunk))
            return true;
        CollectRepairSnapshotOccupants((grid, data), mapGrid, tile, chunk);
        return !_repairSnapshotOccupants.ContainsKey(uid);
    }
}

using Content.Shared._Exodus.ShipRepair;
using Content.Shared._Mono.ShipRepair.Components;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.Shared._Mono.ShipRepair;

public abstract partial class SharedShipRepairSystem
{
    private static readonly Vector2i[] RepairTileNeighbours = { new(1, 0), new(-1, 0), new(0, 1), new(0, -1) };
    // Temporary plan preparation buffers, not persistent grid state.
    private readonly HashSet<Vector2i> _connectedRepairTiles = new();
    private readonly List<ShipRepairWork> _orderedRepairTiles = new();

    /// <summary>Only extend this grid's existing floor. Diagonals and tiles on other grids are not supports.</summary>
    public bool CanRestoreRepairTile(Entity<MapGridComponent> grid, Vector2i indices)
    {
        if (TerminatingOrDeleted(grid))
            return false;
        if (!_map.GetTileRef(grid, grid.Comp, indices).Tile.IsEmpty)
            return true;
        foreach (var offset in RepairTileNeighbours)
        {
            if (!_map.GetTileRef(grid, grid.Comp, indices + offset).Tile.IsEmpty)
                return true;
        }
        return false;
    }

    private bool TryRestoreConnectedTile(Entity<ShipRepairDataComponent> grid, Vector2i indices)
    {
        if (TerminatingOrDeleted(grid) || grid.Comp.ChunkSize <= 0 ||
            !TryGetChunk(grid.Comp, indices, out var chunk) ||
            !_repairGridQuery.TryGetComponent(grid, out var mapGrid) ||
            !CanRestoreRepairTile((grid, mapGrid), indices))
            return false;

        var relative = GetRelativeIndices(indices, grid.Comp.ChunkSize);
        var stored = chunk.Tiles[relative.X + relative.Y * grid.Comp.ChunkSize];
        if (stored == Tile.Empty.TypeId || _map.GetTileRef(grid, mapGrid, indices).Tile.TypeId == stored)
            return false;

        _map.SetTile(grid, mapGrid, indices, new Tile(stored));
        // Tile changes can synchronously split a grid. Never report a detached tile as repaired.
        return !TerminatingOrDeleted(grid) && _map.GetTileRef(grid, mapGrid, indices).Tile.TypeId == stored;
    }

    /// <summary>
    /// Removes disconnected floor and its dependent reconstructions, then orders floor from the hull outwards.
    /// Call again after filtering a batch's access/reservations: a removed tile might have been its only bridge.
    /// Every individual commit still rechecks support, including damage received during the DoAfter.
    /// </summary>
    public void PrepareConnectedRepairPlan(Entity<ShipRepairDataComponent> grid, ShipRepairPlan plan)
    {
        if (TerminatingOrDeleted(grid) || plan.Grid != grid.Owner || plan.Revision != grid.Comp.Revision ||
            !_repairGridQuery.TryGetComponent(grid, out var mapGrid))
        {
            plan.Work.Clear();
            return;
        }

        _connectedRepairTiles.Clear();
        _orderedRepairTiles.Clear();
        bool added;
        do
        {
            added = false;
            foreach (var work in plan.Work)
            {
                if (work.Operation != ShipRepairOperation.Tile || _connectedRepairTiles.Contains(work.Target.Tile))
                    continue;
                var connected = CanRestoreRepairTile((grid, mapGrid), work.Target.Tile);
                foreach (var offset in RepairTileNeighbours)
                    connected |= _connectedRepairTiles.Contains(work.Target.Tile + offset);
                if (!connected)
                    continue;
                _connectedRepairTiles.Add(work.Target.Tile);
                _orderedRepairTiles.Add(work);
                added = true;
            }
        } while (added);

        for (var i = plan.Work.Count - 1; i >= 0; i--)
        {
            var work = plan.Work[i];
            if (work.Operation == ShipRepairOperation.Tile ||
                work.Operation == ShipRepairOperation.Restore &&
                NeedsSnapshotRepair(grid, new ShipRepairTarget(work.Target.Tile)) &&
                !_connectedRepairTiles.Contains(work.Target.Tile))
                plan.Work.RemoveAt(i);
        }
        plan.Work.InsertRange(0, _orderedRepairTiles);
        SortRepairStages(plan);
    }
}

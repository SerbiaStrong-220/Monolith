using Content.Shared._Exodus.Mining.AutoMining;
using Content.Shared.Physics;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics;

namespace Content.Server._Exodus.Mining.AutoMining;

public sealed partial class BulkAutoMiningSystem
{
    private static readonly TimeSpan BeamCheckInterval = TimeSpan.FromSeconds(0.4);

    private void CheckActiveBeams(Entity<BulkAutoMiningConsoleComponent> console, BulkAutoMiningJobComponent job)
    {
        // Check only the current beams between excavation cycles, never the entire target queues.
        job.NextBeamCheckTime = _timing.CurTime + BeamCheckInterval;
        foreach (var uid in job.Emitters)
        {
            if (!_emitterQuery.TryComp(uid, out var emitter) || emitter.Controller != console.Owner ||
                emitter.BeamGrid is not { } grid)
                continue;

            if (!TerminatingOrDeleted(grid) && _gridQuery.TryComp(grid, out var gridComp) &&
                !_map.GetTileRef(grid, gridComp, emitter.BeamTile).Tile.IsEmpty &&
                CanReachTile(console, (uid, emitter), (grid, gridComp), emitter.BeamTile))
                continue;

            ClearBeam((uid, emitter));
            job.Statuses[uid] = BulkAutoMiningLaserStatus.Searching;
        }
    }

    private bool TryFindBeamTile(
        Entity<BulkAutoMiningConsoleComponent> console,
        Entity<BulkAutoMiningEmitterComponent> emitter,
        Entity<MapGridComponent> grid,
        Vector2i tile,
        ref int checks,
        ref bool searchIncomplete,
        out Vector2i reachable)
    {
        reachable = tile;
        while (true)
        {
            var aim = reachable;
            if (!TryFindReachableTile(console, emitter, grid, aim, out reachable))
                return false;

            // Re-centering a surface hit may cross a neighboring rock. Confirm the actual beam's path.
            if (reachable == aim)
                return true;

            // The caller already paid for the candidate's first ray; only re-centering consumes another check.
            if (checks <= 0)
            {
                searchIncomplete = true;
                return false;
            }

            checks--;
        }
    }

    private bool CanReachTile(
        Entity<BulkAutoMiningConsoleComponent> console,
        Entity<BulkAutoMiningEmitterComponent> emitter,
        Entity<MapGridComponent> grid,
        Vector2i tile)
    {
        return TryFindReachableTile(console, emitter, grid, tile, out var reachable) && reachable == tile;
    }

    private bool TryFindReachableTile(
        Entity<BulkAutoMiningConsoleComponent> console,
        Entity<BulkAutoMiningEmitterComponent> emitter,
        Entity<MapGridComponent> grid,
        Vector2i tile,
        out Vector2i reachable)
    {
        reachable = tile;
        var emitterXform = _xformQuery.GetComponent(emitter);
        var gridXform = _xformQuery.GetComponent(grid);
        if (emitterXform.MapUid == null || emitterXform.MapUid != gridXform.MapUid || emitterXform.GridUid == grid.Owner)
            return false;

        var target = _map.GridTileToWorldPos(grid, grid.Comp, tile);
        var origin = _transform.GetWorldPosition(emitterXform);
        var delta = target - origin;
        var distance = delta.Length();
        if (distance > console.Comp.MaxRange)
            return false;

        if (distance < 0.01f)
            return true;

        var ray = new CollisionRay(origin, delta / distance,
            (int)(CollisionGroup.Opaque | CollisionGroup.Impassable | CollisionGroup.BulletImpassable));
        // Only the sorted query guarantees the nearest obstacle across different grids.
        // Mine the exposed surface of the selected grid instead of rejecting its interior tiles.
        var state = (System: this, Emitter: emitter.Owner, Grid: emitterXform.GridUid);
        foreach (var hit in _physics.IntersectRayWithPredicate(emitterXform.MapID, ray, state,
                     static (uid, context) => uid == context.Emitter ||
                         context.System.EntityManager.IsQueuedForDeletion(uid) ||
                         context.System._shieldQuery.TryComp(uid, out var shield) && shield.Shielded == context.Grid,
                     distance, returnOnFirstHit: false))
        {
            // A ship's shield permits its outgoing fire. Foreign shields also block when their grid is selected.
            if (_shieldQuery.HasComp(hit.HitEntity) ||
                !_xformQuery.TryComp(hit.HitEntity, out var xform) || xform.GridUid != grid.Owner)
                return false;

            reachable = _map.TileIndicesFor(grid, xform.Coordinates);
            return true;
        }

        return true;
    }
}

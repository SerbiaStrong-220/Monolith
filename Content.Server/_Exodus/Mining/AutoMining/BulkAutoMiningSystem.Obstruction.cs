using System.Numerics;
using Content.Shared._Exodus.Mining.AutoMining;
using Content.Shared.Physics;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics;

namespace Content.Server._Exodus.Mining.AutoMining;

public sealed partial class BulkAutoMiningSystem
{
    private const int BeamCollisionMask =
        (int)(CollisionGroup.Opaque | CollisionGroup.Impassable | CollisionGroup.BulletImpassable);

    private static readonly TimeSpan BeamCheckInterval = TimeSpan.FromSeconds(0.4);

    private void CheckActiveBeams(Entity<BulkAutoMiningConsoleComponent> console, BulkAutoMiningJobComponent job)
    {
        // Check only the current beams between excavation cycles, never the target surfaces.
        job.NextBeamCheckTime = _timing.CurTime + BeamCheckInterval;
        foreach (var uid in job.Emitters)
        {
            if (!_emitterQuery.TryComp(uid, out var emitter) || emitter.Controller != console.Owner ||
                emitter.BeamGrid is not { } grid)
                continue;

            if (!TerminatingOrDeleted(grid) && _gridQuery.TryComp(grid, out var gridComp) &&
                IsTargetTile((grid, gridComp), emitter.BeamTile) &&
                IsBeamClear(console, (uid, emitter), (grid, gridComp), emitter.BeamTile))
                continue;

            ClearBeam((uid, emitter));
            job.Statuses[uid] = BulkAutoMiningLaserStatus.Searching;
        }
    }

    /// <summary>
    /// Whether a mining beam reaches the tile before any other obstacle. Only the first hit matters,
    /// so the ray stops sorting and filtering after it.
    /// </summary>
    private bool IsBeamClear(
        Entity<BulkAutoMiningConsoleComponent> console,
        Entity<BulkAutoMiningEmitterComponent> emitter,
        Entity<MapGridComponent> grid,
        Vector2i tile)
    {
        var emitterXform = _xformQuery.GetComponent(emitter);
        var gridXform = _xformQuery.GetComponent(grid);
        if (emitterXform.MapUid == null || emitterXform.MapUid != gridXform.MapUid || emitterXform.GridUid == grid.Owner)
            return false;

        var origin = _transform.GetWorldPosition(emitterXform);
        var target = _map.GridTileToWorldPos(grid, grid.Comp, tile);
        if (Vector2.Distance(origin, target) > console.Comp.MaxRange)
            return false;

        var filter = new BeamFilter(this, emitter, emitterXform.GridUid, EntityUid.Invalid, grid, tile);
        return IsRayClear(emitterXform.MapID, origin, target, filter);
    }

    private bool IsRayClear(MapId map, Vector2 origin, Vector2 target, BeamFilter filter)
    {
        var delta = target - origin;
        var distance = delta.Length();
        if (distance < 0.01f)
            return true;

        var ray = new CollisionRay(origin, delta / distance, BeamCollisionMask);
        foreach (var _ in _physics.IntersectRayWithPredicate(map, ray, filter,
                     static (uid, state) => state.System.IsIgnoredByBeam(uid, state), distance))
        {
            return false;
        }

        return true;
    }

    private bool IsIgnoredByBeam(EntityUid uid, BeamFilter filter)
    {
        if (uid == filter.Source || uid == filter.Target || EntityManager.IsQueuedForDeletion(uid))
            return true;

        // A ship's own shield permits its outgoing beam. Linked ships also share each other's shields;
        // any third-party shield blocks, including the shield of a mined grid.
        if (_shieldQuery.TryComp(uid, out var shield))
        {
            return shield.Shielded == filter.SourceGrid ||
                   filter.TargetTile == null && shield.Shielded == filter.TargetGrid;
        }

        // A mining beam ends inside its tile: whatever stands on that tile is what it cuts.
        if (filter.TargetTile is not { } tile || filter.TargetGrid is not { } targetGrid ||
            !_xformQuery.TryComp(uid, out var xform) || xform.GridUid != targetGrid ||
            !_gridQuery.TryComp(targetGrid, out var grid))
            return false;

        return _map.TileIndicesFor(targetGrid, grid, xform.Coordinates) == tile;
    }

    /// <param name="Source">Firing emitter, never an obstacle.</param>
    /// <param name="SourceGrid">Grid of the firing emitter; its shields let the beam out.</param>
    /// <param name="Target">Receiving emitter of a link beam, or invalid for mining.</param>
    /// <param name="TargetGrid">Mined grid, or the partner ship of a link.</param>
    /// <param name="TargetTile">Mined tile; null for link beams.</param>
    private readonly record struct BeamFilter(
        BulkAutoMiningSystem System,
        EntityUid Source,
        EntityUid? SourceGrid,
        EntityUid Target,
        EntityUid? TargetGrid,
        Vector2i? TargetTile);
}

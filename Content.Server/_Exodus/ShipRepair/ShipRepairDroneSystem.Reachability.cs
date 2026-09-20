using System.Numerics;
using Content.Shared._Exodus.ShipRepair;
using Content.Shared._Mono.ShipRepair.Components;
using Content.Shared.Damage;
using Content.Shared.Doors;
using Content.Shared.Doors.Components;
using Content.Shared.Prying.Components;
using Robust.Server.Physics;
using Robust.Shared.Map;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Events;

namespace Content.Server._Exodus.ShipRepair;

public sealed partial class ShipRepairDroneSystem
{
    private const int MaxUnreachableRegions = 16;
    private const int MaxUnreachableTiles = 32768;

    private void InitializeReachability()
    {
        _droneDoorQuery = GetEntityQuery<DoorComponent>();
        _droneFirelockQuery = GetEntityQuery<FirelockComponent>();
        _dronePryingQuery = GetEntityQuery<PryingComponent>();
        SubscribeLocalEvent<TileChangedEvent>(OnRepairTilesChanged);
        SubscribeLocalEvent<ShipRepairableComponent, DamageChangedEvent>(OnRepairDamageChanged);
        SubscribeLocalEvent<GridSplitEvent>(OnRepairGridSplit);
        // Physics shutdown already raises this event when removing a collidable obstacle.
        // PhysicsComponent's lifecycle subscriptions belong to SharedPhysicsSystem.
        SubscribeLocalEvent<CollisionChangeEvent>(OnRepairCollisionChanged);
        SubscribeLocalEvent<CollisionLayerChangeEvent>(OnRepairCollisionLayerChanged);
        SubscribeLocalEvent<PhysicsComponent, MoveEvent>(OnRepairObstacleMoved);
        SubscribeLocalEvent<PhysicsComponent, PhysicsBodyTypeChangedEvent>(OnRepairObstacleTypeChanged);
        SubscribeLocalEvent<DoorComponent, DoorStateChangedEvent>(OnRepairDoorChanged);
        SubscribeLocalEvent<ShipRepairDronePryTargetComponent, BeforePryEvent>(OnBeforeDronePry);
        SubscribeLocalEvent<DoorBoltComponent, DoorBoltsChangedEvent>(OnRepairDoorBoltsChanged);
    }

    private void OnRepairTilesChanged(ref TileChangedEvent args)
    {
        if (!_queueQuery.TryGetComponent(args.Entity.Owner, out var queue))
            return;
        _snapshotQuery.TryGetComponent(args.Entity.Owner, out var data);
        var geometryChanged = false;
        foreach (var change in args.Changes)
        {
            if (data != null)
                RefreshQueuedWork((args.Entity.Owner, data), queue, new ShipRepairTarget(change.GridIndices));
            geometryChanged |= change.NewTile.IsEmpty != change.OldTile.IsEmpty;
        }
        if (geometryChanged)
            InvalidateNavigation(args.Entity.Owner);
    }

    private void OnRepairDamageChanged(Entity<ShipRepairableComponent> ent, ref DamageChangedEvent args)
    {
        if (!_xformQuery.TryGetComponent(ent.Owner, out var xform) || !xform.Anchored)
            return;

        var grid = xform.ParentUid;
        if (!_queueQuery.TryGetComponent(grid, out var queue) || !queue.Indexed ||
            !_mapGridQuery.TryGetComponent(grid, out var mapGrid))
            return;

        var tile = _map.LocalToTile(grid, mapGrid, xform.Coordinates);
        if (!queue.EntriesByTile.TryGetValue(tile, out var entries))
            return;

        // Several snapshot entities may share one tile. Refresh all of them; the set
        // removes duplicates and keeps damage events free of planning or physics work.
        foreach (var target in entries)
            queue.DirtyTargets.Add(target);
    }

    private void OnRepairGridSplit(ref GridSplitEvent args)
    {
        InvalidateNavigation(args.Grid);
    }

    private void OnRepairCollisionChanged(ref CollisionChangeEvent args)
    {
        if (args.Body.BodyType == BodyType.Static)
            InvalidateObstacle(args.BodyUid);
    }

    private void OnRepairCollisionLayerChanged(ref CollisionLayerChangeEvent args)
    {
        if (args.Body.Comp.BodyType == BodyType.Static)
            InvalidateObstacle(args.Body);
    }

    private void OnRepairObstacleTypeChanged(Entity<PhysicsComponent> ent, ref PhysicsBodyTypeChangedEvent args)
    {
        if (args.Old == BodyType.Static || args.New == BodyType.Static)
            InvalidateObstacle(ent);
    }

    private void OnRepairObstacleMoved(Entity<PhysicsComponent> ent, ref MoveEvent args)
    {
        if (ent.Comp.BodyType != BodyType.Static)
            return;
        InvalidateObstacle(ent);
        InvalidateNavigation(args.OldPosition.EntityId);
    }

    private void OnRepairDoorChanged(Entity<DoorComponent> ent, ref DoorStateChangedEvent args)
    {
        InvalidateObstacle(ent);
    }

    private void OnBeforeDronePry(Entity<ShipRepairDronePryTargetComponent> ent, ref BeforePryEvent args)
    {
        // Prying toggles doors. This event is also checked when the DoAfter completes, so a second
        // drone must not close a door already opened by another drone or a player.
        if (_droneQuery.HasComponent(args.User) && TryComp<DoorComponent>(ent, out var door) &&
            door.State is DoorState.Open or DoorState.Opening)
            args.Cancelled = true;
    }

    private void OnRepairDoorBoltsChanged(Entity<DoorBoltComponent> ent, ref DoorBoltsChangedEvent args)
    {
        InvalidateObstacle(ent);
    }

    private void InvalidateObstacle(EntityUid uid)
    {
        if (_xformQuery.TryGetComponent(uid, out var xform) && xform.GridUid is { } grid && grid != uid)
            InvalidateNavigation(grid);
    }

    private void InvalidateNavigation(EntityUid grid)
    {
        if (!_queueQuery.TryGetComponent(grid, out var queue))
            return;
        queue.NavigationRevision++;
        queue.StageRetryRevision++;
        ClearUnreachable(queue);
    }

    private static void ClearUnreachable(ShipRepairWorkQueueComponent queue)
    {
        queue.Unreachable.Clear();
        queue.UnreachableTileCount = 0;
    }

    private void RefreshNavigationFailures(Entity<ShipRepairDroneComponent> ent, ShipRepairWorkQueueComponent queue,
        Vector2 position)
    {
        if (ent.Comp.NavigationRevision == queue.NavigationRevision && ent.Comp.FailureOrigin is { } origin &&
            Vector2.DistanceSquared(position, origin) < 1f)
            return;
        ent.Comp.NavigationRevision = queue.NavigationRevision;
        ent.Comp.FailureOrigin = position;
        ent.Comp.FailedTargets.Clear();
        ent.Comp.FailedPositions.Clear();
        ent.Comp.DeferredSearches.Clear();
    }

    private void RememberUnreachable(Entity<ShipRepairDroneComponent> ent, ShipRepairWorkQueueComponent queue,
        ShipRepairPathSearch search, HashSet<Vector2i> region, bool fromTarget)
    {
        if (search.TransientObstruction || search.Revision != queue.NavigationRevision || ent.Comp.BatchWorkPosition != null ||
            ent.Comp.Target is not { } target || region.Count == 0)
            return;
        // Retain only the fully explored side, not the two search trees. Memory is bounded per serviced ship.
        if (region.Count > MaxUnreachableTiles)
            return;
        while (queue.Unreachable.Count >= MaxUnreachableRegions ||
               queue.UnreachableTileCount + region.Count > MaxUnreachableTiles)
        {
            var removed = queue.Unreachable[0];
            queue.Unreachable.RemoveAt(0);
            queue.UnreachableTileCount -= removed.Tiles.Count;
        }

        var stored = new ShipRepairUnreachableRegion
        {
            Target = target,
            Tiles = region,
            FromTarget = fromTarget,
            Clearance = ent.Comp.Clearance,
            BodyRadius = GetDroneBodyRadius(ent),
            RepairRange = ent.Comp.RepairRange,
            RepairRadius = ent.Comp.RepairRadius,
            StructuralRepairTileRange = ent.Comp.StructuralRepairTileRange,
            ExteriorMargin = ent.Comp.ExteriorMargin,
        };
        queue.Unreachable.Add(stored);
        queue.UnreachableTileCount += stored.Tiles.Count;
    }

    private bool IsKnownUnreachable(Entity<ShipRepairDroneComponent> ent, EntityUid grid,
        ShipRepairWorkQueueComponent queue, ShipRepairWork work, Vector2 position)
    {
        if (ent.Comp.CanPhase || queue.Unreachable.Count == 0 || !_mapGridQuery.TryGetComponent(grid, out var mapGrid))
            return false;
        var tile = _map.LocalToTile(grid, mapGrid, new EntityCoordinates(grid, position));
        var bodyRadius = GetDroneBodyRadius(ent);
        foreach (var region in queue.Unreachable)
        {
            if (region.Target != work.Target || region.Clearance != ent.Comp.Clearance ||
                region.BodyRadius != bodyRadius ||
                region.RepairRange != ent.Comp.RepairRange || region.RepairRadius != ent.Comp.RepairRadius ||
                region.StructuralRepairTileRange != ent.Comp.StructuralRepairTileRange ||
                region.ExteriorMargin != ent.Comp.ExteriorMargin ||
                region.Tiles.Contains(tile) == region.FromTarget)
                continue;
            // A pushed drone may already be within tool range even if no path between cell centers exists.
            if (CanReachWork(ent, grid, work, position) && IsClear(ent, grid, position))
                return false;
            // A drone near a diagonal wall may be in a different pocket than its cell's center.
            if (IsSegmentClear(ent, grid, position, _map.TileCenterToVector(grid, mapGrid, tile), true, out _))
                return true;
        }
        return false;
    }
}

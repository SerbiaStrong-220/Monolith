using System.Numerics;
using Content.Shared._Exodus.ShipRepair;
using Content.Shared.Doors;
using Content.Shared.Doors.Components;
using Robust.Server.Physics;
using Robust.Shared.Map;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Events;

namespace Content.Server._Exodus.ShipRepair;

public sealed partial class ShipRepairDroneSystem
{
    private void InitializeReachability()
    {
        SubscribeLocalEvent<TileChangedEvent>(OnRepairTilesChanged);
        SubscribeLocalEvent<GridSplitEvent>(OnRepairGridSplit);
        // Physics shutdown already raises this event when removing a collidable obstacle.
        // PhysicsComponent's lifecycle subscriptions belong to SharedPhysicsSystem.
        SubscribeLocalEvent<CollisionChangeEvent>(OnRepairCollisionChanged);
        SubscribeLocalEvent<CollisionLayerChangeEvent>(OnRepairCollisionLayerChanged);
        SubscribeLocalEvent<PhysicsComponent, MoveEvent>(OnRepairObstacleMoved);
        SubscribeLocalEvent<PhysicsComponent, PhysicsBodyTypeChangedEvent>(OnRepairObstacleTypeChanged);
        SubscribeLocalEvent<DoorComponent, DoorStateChangedEvent>(OnRepairDoorChanged);
        SubscribeLocalEvent<DoorBoltComponent, DoorBoltsChangedEvent>(OnRepairDoorBoltsChanged);
    }

    private void OnRepairTilesChanged(ref TileChangedEvent args)
    {
        foreach (var change in args.Changes)
        {
            if (change.NewTile.IsEmpty == change.OldTile.IsEmpty)
                continue;
            InvalidateNavigation(args.Entity.Owner);
            return;
        }
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
        queue.Unreachable.Clear();
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
        if (search.TransientObstruction || search.Revision != queue.NavigationRevision ||
            ent.Comp.Target is not { } target || region.Count == 0)
            return;
        // Retain only the fully explored side, not the two search trees. Memory is bounded per serviced ship.
        if (queue.Unreachable.Count >= 16)
            queue.Unreachable.RemoveAt(0);
        queue.Unreachable.Add(new ShipRepairUnreachableRegion
        {
            Target = target,
            Tiles = region,
            FromTarget = fromTarget,
            Clearance = ent.Comp.Clearance,
            RepairRange = ent.Comp.RepairRange,
            RepairRadius = ent.Comp.RepairRadius,
            ExteriorMargin = ent.Comp.ExteriorMargin,
        });
    }

    private bool IsKnownUnreachable(Entity<ShipRepairDroneComponent> ent, EntityUid grid,
        ShipRepairWorkQueueComponent queue, ShipRepairWork work, Vector2 position)
    {
        if (ent.Comp.CanPhase || queue.Unreachable.Count == 0 || !_mapGridQuery.TryGetComponent(grid, out var mapGrid))
            return false;
        var tile = _map.LocalToTile(grid, mapGrid, new EntityCoordinates(grid, position));
        foreach (var region in queue.Unreachable)
        {
            if (region.Target != work.Target || region.Clearance != ent.Comp.Clearance ||
                region.RepairRange != ent.Comp.RepairRange || region.RepairRadius != ent.Comp.RepairRadius ||
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

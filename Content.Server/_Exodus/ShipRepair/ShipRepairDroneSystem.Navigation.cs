using System.Numerics;
using Content.Shared._Exodus.ShipRepair;
using Content.Shared._Mono.ShipRepair.Components;
using Content.Shared.DoAfter;
using Content.Shared.Doors.Components;
using Content.Shared.NPC;
using Content.Shared.Physics;
using Content.Shared.Prying.Components;
using Robust.Shared.Map;
using Robust.Shared.Physics.Collision.Shapes;
using PhysicsTransform = Robust.Shared.Physics.Transform;

namespace Content.Server._Exodus.ShipRepair;

public sealed partial class ShipRepairDroneSystem
{
    private static readonly Vector2i[] Neighbours = { new(1, 0), new(-1, 0), new(0, 1), new(0, -1) };
    // Scratch buffers, never retained as world state or shared with an asynchronous pathfinder.
    private readonly HashSet<EntityUid> _intersections = new();
    private readonly PolygonShape _sweep = new();

    private bool CanServiceShip(Entity<ShipRepairDroneComponent> ent, TransformComponent xform,
        EntityUid grid, ShipRepairWorkQueueComponent queue)
    {
        if (!_xformQuery.TryGetComponent(grid, out var gridXform) || xform.MapID != gridXform.MapID)
            return false;
        if (xform.GridUid == grid)
            return true;

        var position = _transform.ToCoordinates(grid, _transform.GetMapCoordinates(xform)).Position;
        if (!queue.Bounds.Enlarged(ent.Comp.ExteriorMargin).Contains(position))
            return false;

        // An undocked drone waits for a departing ship instead of chasing it across the map.
        if (_bodyQuery.TryGetComponent(grid, out var body))
        {
            var offset = _transform.GetWorldPosition(xform) - _transform.GetWorldPosition(gridXform);
            var velocity = body.LinearVelocity + new Vector2(-offset.Y, offset.X) * body.AngularVelocity;
            if (velocity.LengthSquared() > ent.Comp.MaximumShipSpeed * ent.Comp.MaximumShipSpeed)
                return false;
        }
        return true;
    }

    private bool StartNavigation(Entity<ShipRepairDroneComponent> ent, Entity<ShipRepairDataComponent> grid,
        ShipRepairWorkQueueComponent queue, ShipRepairWork work)
    {
        if (!_mapGridQuery.TryGetComponent(grid, out var mapGrid))
            return false;
        StopMoving(ent);
        ent.Comp.Path.Clear();
        ent.Comp.PathIndex = 0;
        var position = _transform.ToCoordinates(grid.Owner, _transform.GetMapCoordinates(ent)).Position;
        var start = _map.LocalToTile(grid, mapGrid, new EntityCoordinates(grid, position));
        var search = new ShipRepairPathSearch
        {
            Bounds = queue.Bounds.Enlarged(ent.Comp.ExteriorMargin),
        };
        var center = work.Target.Tile;
        for (var y = -1; y <= 1; y++)
        for (var x = -1; x <= 1; x++)
        {
            // Stay beside the target: never reconstruct a wall around our own body.
            if (x == 0 && y == 0)
                continue;
            var tile = center + new Vector2i(x, y);
            var point = _map.TileCenterToVector(grid, mapGrid, tile);
            if (Vector2.DistanceSquared(point, work.Position) > ent.Comp.RepairRange * ent.Comp.RepairRange ||
                !IsClear(ent, grid, point) || !CanReachWork(ent, grid, work, point))
                continue;
            search.Goals.Add(tile);
        }
        if (search.Goals.Count == 0 || !search.Bounds.Contains(position))
            return false;

        ent.Comp.LastPosition = position;
        ent.Comp.ProgressDeadline = _timing.CurTime + ent.Comp.StuckTimeout;
        if (ent.Comp.CanPhase)
        {
            var best = Vector2.Zero;
            var distance = float.MaxValue;
            foreach (var goal in search.Goals)
            {
                var point = _map.TileCenterToVector(grid, mapGrid, goal);
                var length = Vector2.DistanceSquared(point, position);
                if (length >= distance)
                    continue;
                distance = length;
                best = point;
            }
            // Short waypoints switch the phase on before each actual obstacle, not for the whole trip.
            var steps = Math.Max(1, (int) MathF.Ceiling(Vector2.Distance(position, best)));
            if (steps > ent.Comp.PathNodeLimit)
                return false;
            for (var i = 1; i <= steps; i++)
                ent.Comp.Path.Add(Vector2.Lerp(position, best, (float) i / steps));
            ent.Comp.Search = null;
            return true;
        }

        var startCenter = _map.TileCenterToVector(grid, mapGrid, start);
        if (!IsSegmentClear(ent, grid, position, startCenter, true, out _))
            return false;
        search.Costs[start] = 0f;
        search.Open.Enqueue(start, 0f);
        ent.Comp.Search = search;
        return true;
    }

    private void UpdateNavigation(Entity<ShipRepairDroneComponent> ent, Entity<ShipRepairToolComponent> tool,
        Entity<ShipRepairDataComponent> grid, ShipRepairWorkQueueComponent queue, int budget)
    {
        if (ent.Comp.Search is { } search)
        {
            ExpandPath(ent, grid, search, budget);
            return;
        }

        if (ent.Comp.PryDoAfter is { } pry)
        {
            if (_doAfter.GetStatus(pry) == DoAfterStatus.Running)
                return;
            ent.Comp.PryDoAfter = null;
            ent.Comp.ProgressDeadline = _timing.CurTime + ent.Comp.StuckTimeout;
        }

        var position = _transform.ToCoordinates(grid.Owner, _transform.GetMapCoordinates(ent)).Position;
        if (Vector2.DistanceSquared(position, ent.Comp.LastPosition) > 0.04f)
        {
            ent.Comp.LastPosition = position;
            ent.Comp.ProgressDeadline = _timing.CurTime + ent.Comp.StuckTimeout;
        }
        else if (_timing.CurTime > ent.Comp.ProgressDeadline)
        {
            FailJob(ent);
            TryLeavePhase(ent, eject: true);
            return;
        }

        if (ent.Comp.PathIndex < ent.Comp.Path.Count &&
            Vector2.DistanceSquared(position, ent.Comp.Path[ent.Comp.PathIndex]) <= 0.04f)
            ent.Comp.PathIndex++;

        if (ent.Comp.PathIndex >= ent.Comp.Path.Count)
        {
            StopMoving(ent);
            if (!TryLeavePhase(ent, eject: false))
            {
                FailJob(ent);
                TryLeavePhase(ent, eject: true);
                return;
            }
            StartRepair(ent, tool, grid, queue);
            return;
        }

        var next = ent.Comp.Path[ent.Comp.PathIndex];
        if (ent.Comp.CanPhase)
        {
            if (!IsSegmentClear(ent, grid, position, next, false, out _))
                EnterPhase(ent);
            else
                TryLeavePhase(ent, eject: false);
        }
        else if (!IsSegmentClear(ent, grid, position, next, true, out var door))
        {
            Repath(ent, tool, grid, queue);
            return;
        }
        else if (door is { } doorUid)
        {
            StopMoving(ent);
            if (!HandleDoor(ent, doorUid))
            {
                ent.Comp.BlockedDoors.Add(doorUid);
                Repath(ent, tool, grid, queue);
            }
            return;
        }

        EnsureComp<ActiveNPCComponent>(ent);
        var steering = _steering.Register(ent, new EntityCoordinates(grid, next));
        steering.DirectMove = true;
        steering.Range = 0.12f;
        steering.Radius = 0.25f;
    }

    private void Repath(Entity<ShipRepairDroneComponent> ent, Entity<ShipRepairToolComponent> tool,
        Entity<ShipRepairDataComponent> grid, ShipRepairWorkQueueComponent queue)
    {
        if (ent.Comp.Target is not { } target ||
            !_repair.TryPlanRepair(tool, grid, target, true, out var work) ||
            !StartNavigation(ent, grid, queue, work))
            FailJob(ent);
    }

    private void ExpandPath(Entity<ShipRepairDroneComponent> ent, Entity<ShipRepairDataComponent> grid,
        ShipRepairPathSearch search, int budget)
    {
        if (!_mapGridQuery.TryGetComponent(grid, out var mapGrid))
        {
            FailJob(ent);
            return;
        }
        for (var i = 0; i < budget; i++)
        {
            if (search.Closed.Count >= ent.Comp.PathNodeLimit || !search.Open.TryDequeue(out var current, out _))
            {
                FailJob(ent);
                return;
            }
            if (!search.Closed.Add(current))
                continue;

            if (search.Goals.Contains(current))
            {
                ent.Comp.Path.Add(_map.TileCenterToVector(grid, mapGrid, current));
                while (search.Previous.TryGetValue(current, out var previous))
                {
                    ent.Comp.Path.Add(_map.TileCenterToVector(grid, mapGrid, previous));
                    current = previous;
                }
                ent.Comp.Path.Reverse();
                ent.Comp.Search = null;
                ent.Comp.ProgressDeadline = _timing.CurTime + ent.Comp.StuckTimeout;
                return;
            }

            var position = _map.TileCenterToVector(grid, mapGrid, current);
            foreach (var offset in Neighbours)
            {
                var next = current + offset;
                var point = _map.TileCenterToVector(grid, mapGrid, next);
                if (search.Closed.Contains(next) || !search.Bounds.Contains(point) ||
                    !IsSegmentClear(ent, grid, position, point, true, out var door))
                    continue;

                var cost = search.Costs[current] + (door == null ? 1f : 6f);
                if (search.Costs.TryGetValue(next, out var old) && cost >= old)
                    continue;

                search.Costs[next] = cost;
                search.Previous[next] = current;
                var heuristic = float.MaxValue;
                foreach (var goal in search.Goals)
                    heuristic = Math.Min(heuristic, Math.Abs(next.X - goal.X) + Math.Abs(next.Y - goal.Y));
                search.Open.Enqueue(next, cost + heuristic);
            }
        }
    }

    private bool HandleDoor(Entity<ShipRepairDroneComponent> ent, EntityUid uid)
    {
        if (!TryComp<DoorComponent>(uid, out var door))
            return false;
        if (door.State is DoorState.Open or DoorState.Opening)
            return true;
        if (_doors.TryOpen(uid, door, ent, quiet: true))
            return true;

        // TryPry's boolean also means "interaction handled" on rejection; the DoAfter ID is authoritative.
        _prying.TryPry(uid, ent, out var id, ent);
        ent.Comp.PryDoAfter = id;
        return id != null;
    }

    private bool IsClear(Entity<ShipRepairDroneComponent> ent, EntityUid grid, Vector2 position)
    {
        var map = _transform.ToMapCoordinates(new EntityCoordinates(grid, position));
        return IsWorldClear(ent, map);
    }

    private bool IsWorldClear(Entity<ShipRepairDroneComponent> ent, MapCoordinates position)
    {
        var clearance = ent.Comp.ClearanceShape ??= new PhysShapeCircle(ent.Comp.Clearance);
        _intersections.Clear();
        _lookup.GetEntitiesIntersecting(position.MapId, clearance, new PhysicsTransform(position.Position, Angle.Zero),
            _intersections, LookupFlags.Static | LookupFlags.Dynamic);
        foreach (var uid in _intersections)
        {
            if (BlocksDrone(ent, uid))
                return false;
        }
        return true;
    }

    private bool IsSegmentClear(Entity<ShipRepairDroneComponent> ent, EntityUid grid, Vector2 start, Vector2 end,
        bool allowDoors, out EntityUid? door)
    {
        door = null;
        var from = _transform.ToMapCoordinates(new EntityCoordinates(grid, start));
        var to = _transform.ToMapCoordinates(new EntityCoordinates(grid, end));
        var delta = to.Position - from.Position;
        // Conservative swept body, including edges/diagonal fixtures between tile centers.
        _sweep.SetAsBox(delta.Length() * 0.5f + ent.Comp.Clearance, ent.Comp.Clearance);
        _intersections.Clear();
        _lookup.GetEntitiesIntersecting(from.MapId, _sweep,
            new PhysicsTransform((from.Position + to.Position) * 0.5f, delta.ToAngle()), _intersections,
            LookupFlags.Static | LookupFlags.Dynamic);
        foreach (var uid in _intersections)
        {
            if (!BlocksDrone(ent, uid))
                continue;
            if (!allowDoors || ent.Comp.BlockedDoors.Contains(uid) || !HasComp<DoorComponent>(uid))
                return false;

            if (!_doors.CanOpen(uid, user: ent))
            {
                var canPry = new BeforePryEvent(ent, true, false, true);
                RaiseLocalEvent(uid, ref canPry);
                if (canPry.Cancelled)
                    return false;
            }
            door ??= uid;
        }
        return true;
    }

    private bool BlocksDrone(EntityUid drone, EntityUid other)
    {
        if (other == drone || !_bodyQuery.TryGetComponent(other, out var body) || !body.CanCollide || !body.Hard)
            return false;
        return (body.CollisionLayer & (int) CollisionGroup.FlyingMobMask) != 0 ||
               (body.CollisionMask & (int) CollisionGroup.FlyingMobLayer) != 0;
    }

    private void EnterPhase(Entity<ShipRepairDroneComponent> ent)
    {
        if (ent.Comp.Phased || !_fixturesQuery.TryGetComponent(ent, out var fixtures))
            return;
        if (IsWorldClear(ent, _transform.GetMapCoordinates(ent)))
            ent.Comp.LastSafePosition = Transform(ent).Coordinates;

        ent.Comp.SolidMasks.Clear();
        foreach (var (id, fixture) in fixtures.Fixtures)
        {
            if (!fixture.Hard)
                continue;
            ent.Comp.SolidMasks[id] = fixture.CollisionMask;
            // Keep layers (especially BulletImpassable/Opaque) so weapons still hit the drone.
            _physics.SetCollisionMask(ent, id, fixture, 0, fixtures);
        }
        ent.Comp.Phased = true;
        ent.Comp.EjectRing = 1;
        SetVisual(ent);
    }

    private bool TryLeavePhase(Entity<ShipRepairDroneComponent> ent, bool eject)
    {
        if (!ent.Comp.Phased)
            return true;
        var map = _transform.GetMapCoordinates(ent);
        if (!_containers.IsEntityInContainer(ent) && !IsWorldClear(ent, map))
        {
            if (!eject || !TryEject(ent, map))
                return false;
        }

        if (_fixturesQuery.TryGetComponent(ent, out var fixtures))
        {
            foreach (var (id, mask) in ent.Comp.SolidMasks)
            {
                if (fixtures.Fixtures.TryGetValue(id, out var fixture))
                    _physics.SetCollisionMask(ent, id, fixture, mask, fixtures);
            }
        }
        ent.Comp.SolidMasks.Clear();
        ent.Comp.Phased = false;
        ent.Comp.LastSafePosition = Transform(ent).Coordinates;
        ent.Comp.EjectRing = 1;
        SetVisual(ent);
        return true;
    }

    private bool TryEject(Entity<ShipRepairDroneComponent> ent, MapCoordinates origin)
    {
        // Search nearest rings on the current map, not the remembered ship (which may have departed).
        // Bounded per update, with a previously safe position as a checked fallback.
        for (var batch = 0; batch < 4; batch++, ent.Comp.EjectRing++)
        {
            var radius = ent.Comp.EjectRing * 0.5f;
            for (var i = 0; i < 16; i++)
            {
                var offset = new Angle(i * Math.Tau / 16).ToVec() * radius;
                var point = new MapCoordinates(origin.Position + offset, origin.MapId);
                if (!IsWorldClear(ent, point))
                    continue;
                _transform.SetCoordinates(ent, _transform.ToCoordinates(point));
                _transform.AttachToGridOrMap(ent);
                return true;
            }
        }

        if (ent.Comp.LastSafePosition is { } safe && safe.IsValid(EntityManager))
        {
            var point = _transform.ToMapCoordinates(safe);
            if (point.MapId == origin.MapId && Vector2.DistanceSquared(point.Position, origin.Position) <= 64 &&
                IsWorldClear(ent, point))
            {
                _transform.SetCoordinates(ent, _transform.ToCoordinates(point));
                _transform.AttachToGridOrMap(ent);
                return true;
            }
        }
        return false;
    }
}

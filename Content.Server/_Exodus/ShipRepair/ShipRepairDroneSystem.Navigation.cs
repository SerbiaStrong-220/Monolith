using System.Numerics;
using Content.Server.NPC.Components;
using Content.Shared._Exodus.ShipRepair;
using Content.Shared._Mono.ShipRepair.Components;
using Content.Shared.DoAfter;
using Content.Shared.Doors.Components;
using Content.Shared.Physics;
using Content.Shared.Prying.Components;
using Robust.Shared.Map;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Collision.Shapes;
using PhysicsTransform = Robust.Shared.Physics.Transform;

namespace Content.Server._Exodus.ShipRepair;

public sealed partial class ShipRepairDroneSystem
{
    private static readonly Vector2i[] Neighbours = { new(1, 0), new(-1, 0), new(0, 1), new(0, -1) };
    // Scratch buffers, never retained as world state or shared with an asynchronous pathfinder.
    private readonly HashSet<EntityUid> _intersections = new();
    // The middle of a swept circle has no extra polygon skin. Its ends are queried as circles.
    private readonly PolygonShape _sweep = new(0f);

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
        ReleaseWorkPosition(ent, queue);
        ent.Comp.Path.Clear();
        ent.Comp.PathIndex = 0;
        ent.Comp.Search = null;
        ent.Comp.Settling = false;
        ent.Comp.BestWaypointDistance = float.PositiveInfinity;
        ent.Comp.ProgressDeadline = _timing.CurTime + ent.Comp.StuckTimeout;
        var position = _transform.ToCoordinates(grid.Owner, _transform.GetMapCoordinates(ent)).Position;
        var start = _map.LocalToTile(grid, mapGrid, new EntityCoordinates(grid, position));
        if (CanWorkHere(ent, grid, position) && TryClaimWorkPosition(ent, queue, start, position))
            return true;

        var search = new ShipRepairPathSearch
        {
            Bounds = queue.Bounds.Enlarged(ent.Comp.ExteriorMargin),
            Revision = queue.NavigationRevision,
        };
        var center = work.Target.Tile;
        var approachRadius = Math.Max(1, ent.Comp.RepairRadius + 1);
        for (var y = -approachRadius; y <= approachRadius; y++)
        for (var x = -approachRadius; x <= approachRadius; x++)
        {
            // Stay beside the target: never reconstruct a wall around our own body.
            if (x == 0 && y == 0)
                continue;
            var tile = center + new Vector2i(x, y);
            var point = _map.TileCenterToVector(grid, mapGrid, tile);
            if (!IsWorkPositionAvailable(ent, queue, tile, point))
            {
                search.TransientObstruction = true;
                continue;
            }
            if (!search.Bounds.Contains(point) || !IsClear(ent, grid, point, search) ||
                !CanReachWork(ent, grid, work, point, search))
                continue;
            search.Goals.Add(tile);
            search.Reverse.Costs[tile] = 0f;
            search.Reverse.Open.Enqueue(tile, 0f);
        }
        if (search.Goals.Count == 0 || !search.Bounds.Contains(position))
            return false;

        if (ent.Comp.CanPhase)
        {
            var best = start;
            var distance = float.MaxValue;
            foreach (var goal in search.Goals)
            {
                var point = _map.TileCenterToVector(grid, mapGrid, goal);
                var length = Vector2.DistanceSquared(point, position);
                if (length >= distance)
                    continue;
                distance = length;
                best = goal;
            }
            var end = _map.TileCenterToVector(grid, mapGrid, best);
            if (Vector2.Distance(position, end) > ent.Comp.PathNodeLimit ||
                !TryClaimWorkPosition(ent, queue, best, end))
                return false;
            SetDirectPath(ent, position, end);
            return true;
        }

        var startCenter = _map.TileCenterToVector(grid, mapGrid, start);
        if (IsSegmentClear(ent, grid, position, startCenter, true, out _, search))
        {
            search.Starts.Add(start);
            search.Forward.Costs[start] = 0f;
            search.Forward.Open.Enqueue(start, 0f);
        }
        else
        {
            // Beside a diagonal wall, the current cell's center can be blocked while a neighbor is reachable.
            for (var y = -1; y <= 1; y++)
            for (var x = -1; x <= 1; x++)
            {
                if (x == 0 && y == 0)
                    continue;
                var tile = start + new Vector2i(x, y);
                var point = _map.TileCenterToVector(grid, mapGrid, tile);
                if (!search.Bounds.Contains(point) || !IsSegmentClear(ent, grid, position, point, true, out _, search))
                    continue;
                var cost = Vector2.Distance(position, point);
                search.Starts.Add(tile);
                search.Forward.Costs[tile] = cost;
                search.Forward.Open.Enqueue(tile, cost);
            }
            if (search.Forward.Open.Count == 0)
            {
                // Failure to join the navigation graph says nothing about the repair target itself.
                BeginClearanceRecovery(ent, grid, queue, position, ShipRepairNavigationIssue.StartBlocked);
                return true;
            }
        }
        ent.Comp.SearchDeadline = _timing.CurTime +
            (ent.Comp.DeferredSearches.Contains(work.Target) ? ent.Comp.ExtendedSearchTimeout : ent.Comp.SearchTimeout);
        ent.Comp.Search = search;
        ent.Comp.NavigationIssue = ShipRepairNavigationIssue.None;
        return true;
    }

    private bool HasReachedWaypoint(Entity<ShipRepairDroneComponent> ent, TransformComponent xform)
    {
        if (!ent.Comp.Enabled || ent.Comp.WaitingForShip || ent.Comp.Settling || ent.Comp.Search != null ||
            ent.Comp.RepairDoAfter != null || ent.Comp.PryDoAfter != null ||
            ent.Comp.PathIndex >= ent.Comp.Path.Count || ent.Comp.Grid is not { } grid ||
            TerminatingOrDeleted(grid) || !_xformQuery.TryGetComponent(grid, out var gridXform) ||
            xform.MapID != gridXform.MapID)
            return false;

        var position = _transform.ToCoordinates(grid, _transform.GetMapCoordinates(xform)).Position;
        return Vector2.DistanceSquared(position, ent.Comp.Path[ent.Comp.PathIndex]) <=
               ent.Comp.ArrivalRange * ent.Comp.ArrivalRange;
    }

    private void UpdateNavigation(Entity<ShipRepairDroneComponent> ent, Entity<ShipRepairToolComponent> tool,
        Entity<ShipRepairDataComponent> grid, ShipRepairWorkQueueComponent queue, int budget)
    {
        if (_timing.CurTime > ent.Comp.NavigationDeadline)
        {
            FailJob(ent);
            return;
        }
        if (ent.Comp.Search is { } search)
        {
            ExpandPath(ent, grid, queue, search, budget);
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
        if (ent.Comp.Settling)
        {
            if (_timing.CurTime > ent.Comp.ProgressDeadline)
            {
                Repath(ent, tool, grid, queue);
                return;
            }

            // Sample motion in grid coordinates: a travelling/rotating ship is not drone drift.
            var elapsed = (_timing.CurTime - ent.Comp.SettleTime).TotalSeconds;
            var movement = Vector2.DistanceSquared(position, ent.Comp.SettlePosition);
            ent.Comp.SettlePosition = position;
            ent.Comp.SettleTime = _timing.CurTime;
            if (elapsed <= 0 || movement > Math.Pow(ent.Comp.RepairSpeedLimit * elapsed, 2))
                return;

            if (!TryLeavePhase(ent, eject: false))
            {
                Repath(ent, tool, grid, queue);
                return;
            }
            // Braking or an external push may have changed the cell since it was reserved.
            if (!_mapGridQuery.TryGetComponent(grid, out var settledGrid) || !IsClear(ent, grid, position) ||
                !TryClaimWorkPosition(ent, queue,
                    _map.LocalToTile(grid, settledGrid, new EntityCoordinates(grid, position)), position))
            {
                Repath(ent, tool, grid, queue);
                return;
            }
            if (ent.Comp.Yielding)
            {
                CancelJob(ent);
                ent.Comp.NextSearch = _timing.CurTime + ent.Comp.IdleInterval;
            }
            else
                StartRepair(ent, tool, grid, queue);
            return;
        }

        // An already reachable job does not require hitting the exact center of the final waypoint.
        if (!ent.Comp.Yielding && _mapGridQuery.TryGetComponent(grid, out var mapGrid) &&
            CanWorkHere(ent, grid, position) &&
            TryClaimWorkPosition(ent, queue, _map.LocalToTile(grid, mapGrid, new EntityCoordinates(grid, position)), position))
        {
            BeginSettling(ent, position);
            return;
        }

        while (ent.Comp.PathIndex < ent.Comp.Path.Count &&
               Vector2.DistanceSquared(position, ent.Comp.Path[ent.Comp.PathIndex]) <= ent.Comp.ArrivalRange * ent.Comp.ArrivalRange)
        {
            ent.Comp.PathIndex++;
            ent.Comp.BestWaypointDistance = float.PositiveInfinity;
            ent.Comp.ProgressDeadline = _timing.CurTime + ent.Comp.StuckTimeout;
        }

        if (ent.Comp.PathIndex >= ent.Comp.Path.Count)
        {
            BeginSettling(ent, position);
            return;
        }

        var next = ent.Comp.Path[ent.Comp.PathIndex];
        var distance = Vector2.Distance(position, next);
        if (distance < ent.Comp.BestWaypointDistance - 0.05f)
        {
            ent.Comp.BestWaypointDistance = distance;
            ent.Comp.ProgressDeadline = _timing.CurTime + ent.Comp.StuckTimeout;
        }
        if (_timing.CurTime > ent.Comp.ProgressDeadline ||
            _steeringQuery.TryGetComponent(ent, out var previousSteering) && previousSteering.Status == SteeringStatus.NoPath)
        {
            Repath(ent, tool, grid, queue);
            return;
        }
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

        // Intermediate points are fly-through waypoints, not separate stopping destinations.
        SetMovementTarget(ent, grid, next, ent.Comp.ArrivalRange,
            ent.Comp.PathIndex == ent.Comp.Path.Count - 1 ? ent.Comp.RepairSpeedLimit : null);
    }

    private bool CanWorkHere(Entity<ShipRepairDroneComponent> ent, EntityUid grid, Vector2 position)
    {
        if (ent.Comp.Plan is not { } plan || plan.Work.Count == 0 || ent.Comp.Phased)
            return false;
        foreach (var work in plan.Work)
        {
            if (!CanReachWork(ent, grid, work, position))
                return false;
        }
        return IsClear(ent, grid, position);
    }

    private void BeginSettling(Entity<ShipRepairDroneComponent> ent, Vector2 position)
    {
        StopMoving(ent);
        ent.Comp.Settling = true;
        ent.Comp.SettlePosition = position;
        ent.Comp.SettleTime = _timing.CurTime;
        ent.Comp.ProgressDeadline = _timing.CurTime + ent.Comp.StuckTimeout;
    }

    private void SetDirectPath(Entity<ShipRepairDroneComponent> ent, Vector2 start, Vector2 end)
    {
        ent.Comp.Path.Clear();
        ent.Comp.PathIndex = 0;
        ent.Comp.Search = null;
        ent.Comp.BestWaypointDistance = float.PositiveInfinity;
        ent.Comp.ProgressDeadline = _timing.CurTime + ent.Comp.StuckTimeout;
        // Short waypoints enter phase before the obstacle, not for the whole trip.
        var steps = Math.Max(1, (int) MathF.Ceiling(Vector2.Distance(start, end)));
        for (var i = 1; i <= steps; i++)
            ent.Comp.Path.Add(Vector2.Lerp(start, end, (float) i / steps));
    }

    private void Repath(Entity<ShipRepairDroneComponent> ent, Entity<ShipRepairToolComponent> tool,
        Entity<ShipRepairDataComponent> grid, ShipRepairWorkQueueComponent queue)
    {
        if (ent.Comp.WorkTile is { } tile)
            ent.Comp.FailedPositions[tile] = _timing.CurTime + ent.Comp.RetryInterval;
        if (ent.Comp.Yielding || ++ent.Comp.Repaths > ent.Comp.RepathLimit || ent.Comp.Target is not { } target ||
            !_repair.TryPlanRepair(tool, grid, target, true, out var work, checkMobileObstructions: false) ||
            !StartNavigation(ent, grid, queue, work))
            FailJob(ent);
    }

    private void ExpandPath(Entity<ShipRepairDroneComponent> ent, Entity<ShipRepairDataComponent> grid,
        ShipRepairWorkQueueComponent queue, ShipRepairPathSearch search, int budget)
    {
        if (!_mapGridQuery.TryGetComponent(grid, out var mapGrid))
        {
            FailJob(ent);
            return;
        }
        // Keep a bounded search running when another drone repairs the ship. Each movement segment is
        // checked again before travel, and a search across geometry revisions cannot prove unreachability.
        if (_timing.CurTime >= ent.Comp.SearchDeadline)
        {
            DeferSearch(ent);
            return;
        }
        for (var i = 0; i < budget; i++)
        {
            if (search.Forward.Closed.Count + search.Reverse.Closed.Count >= ent.Comp.PathNodeLimit)
            {
                DeferSearch(ent);
                return;
            }

            search.ReverseTurn = !search.ReverseTurn;
            var frontier = search.ReverseTurn ? search.Reverse : search.Forward;
            var other = search.ReverseTurn ? search.Forward : search.Reverse;
            if (!frontier.Open.TryDequeue(out var current, out _))
            {
                if (search.Revision != queue.NavigationRevision)
                {
                    CancelJob(ent);
                    ent.Comp.NextSearch = _timing.CurTime;
                    return;
                }
                RememberUnreachable(ent, queue, search, frontier.Closed, search.ReverseTurn);
                FailJob(ent);
                return;
            }
            if (!frontier.Closed.Add(current))
                continue;
            if (other.Costs.ContainsKey(current))
            {
                FinishPath(ent, grid, queue, mapGrid, search, current);
                return;
            }

            var position = _map.TileCenterToVector(grid, mapGrid, current);
            foreach (var offset in Neighbours)
            {
                var next = current + offset;
                var point = _map.TileCenterToVector(grid, mapGrid, next);
                // Reverse expansion validates the direction the drone will actually travel.
                if (frontier.Closed.Contains(next) || !search.Bounds.Contains(point) ||
                    !IsSegmentClear(ent, grid, search.ReverseTurn ? point : position,
                        search.ReverseTurn ? position : point, true, out var door, search))
                    continue;

                var cost = frontier.Costs[current] + (door == null ? 1f : 6f);
                if (frontier.Costs.TryGetValue(next, out var old) && cost >= old)
                    continue;

                frontier.Costs[next] = cost;
                frontier.Previous[next] = current;
                var heuristic = float.MaxValue;
                foreach (var goal in search.ReverseTurn ? search.Starts : search.Goals)
                    heuristic = Math.Min(heuristic, Math.Abs(next.X - goal.X) + Math.Abs(next.Y - goal.Y));
                frontier.Open.Enqueue(next, cost + heuristic);
            }
        }
    }

    private void FinishPath(Entity<ShipRepairDroneComponent> ent, EntityUid grid, ShipRepairWorkQueueComponent queue,
        Robust.Shared.Map.Components.MapGridComponent mapGrid, ShipRepairPathSearch search, Vector2i meeting)
    {
        var current = meeting;
        ent.Comp.Path.Add(_map.TileCenterToVector(grid, mapGrid, current));
        while (search.Forward.Previous.TryGetValue(current, out var previous))
        {
            current = previous;
            ent.Comp.Path.Add(_map.TileCenterToVector(grid, mapGrid, current));
        }
        ent.Comp.Path.Reverse();
        current = meeting;
        while (search.Reverse.Previous.TryGetValue(current, out var next))
        {
            current = next;
            ent.Comp.Path.Add(_map.TileCenterToVector(grid, mapGrid, current));
        }
        if (!TryClaimWorkPosition(ent, queue, current, _map.TileCenterToVector(grid, mapGrid, current)))
        {
            FailJob(ent);
            return;
        }
        ent.Comp.Search = null;
        if (ent.Comp.Target is { } target)
            ent.Comp.DeferredSearches.Remove(target);
        ent.Comp.ProgressDeadline = _timing.CurTime + ent.Comp.StuckTimeout;
    }

    private void DeferSearch(Entity<ShipRepairDroneComponent> ent)
    {
        if (ent.Comp.Target is { } target)
            ent.Comp.DeferredSearches.Add(target);
        FailJob(ent);
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

    private bool IsClear(Entity<ShipRepairDroneComponent> ent, EntityUid grid, Vector2 position,
        ShipRepairPathSearch? search = null)
    {
        var map = _transform.ToMapCoordinates(new EntityCoordinates(grid, position));
        return IsWorldClear(ent, map, search, grid);
    }

    private bool IsWorldClear(Entity<ShipRepairDroneComponent> ent, MapCoordinates position,
        ShipRepairPathSearch? search = null, EntityUid? grid = null, PhysShapeCircle? shape = null)
    {
        var clearance = shape ?? GetNavigationShape(ent);
        _intersections.Clear();
        _lookup.GetEntitiesIntersecting(position.MapId, clearance, new PhysicsTransform(position.Position, Angle.Zero),
            _intersections, LookupFlags.Static | LookupFlags.Dynamic);
        foreach (var uid in _intersections)
        {
            if (BlocksDrone(ent, uid))
            {
                TrackSearchObstruction(search, grid, uid);
                return false;
            }
        }
        return true;
    }

    private bool IsSegmentClear(Entity<ShipRepairDroneComponent> ent, EntityUid grid, Vector2 start, Vector2 end,
        bool allowDoors, out EntityUid? door, ShipRepairPathSearch? search = null, bool recovering = false)
    {
        door = null;
        var from = _transform.ToMapCoordinates(new EntityCoordinates(grid, start));
        var to = _transform.ToMapCoordinates(new EntityCoordinates(grid, end));
        var delta = to.Position - from.Position;
        var circle = GetNavigationShape(ent, recovering);
        // Exact swept disk: a central rectangle plus circular end caps. The old extended rectangle
        // falsely intersected walls beside the starting point, especially when moving diagonally away.
        _intersections.Clear();
        _lookup.GetEntitiesIntersecting(from.MapId, circle,
            new PhysicsTransform(from.Position, Angle.Zero), _intersections,
            LookupFlags.Static | LookupFlags.Dynamic);
        if (delta.LengthSquared() > 0.000001f)
        {
            _sweep.SetAsBox(delta.Length() * 0.5f, circle.Radius);
            _lookup.GetEntitiesIntersecting(from.MapId, _sweep,
                new PhysicsTransform((from.Position + to.Position) * 0.5f, delta.ToAngle()), _intersections,
                LookupFlags.Static | LookupFlags.Dynamic);
            _lookup.GetEntitiesIntersecting(to.MapId, circle,
                new PhysicsTransform(to.Position, Angle.Zero), _intersections,
                LookupFlags.Static | LookupFlags.Dynamic);
        }
        foreach (var uid in _intersections)
        {
            if (!BlocksDrone(ent, uid))
                continue;
            TrackSearchObstruction(search, grid, uid);
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

    private void TrackSearchObstruction(ShipRepairPathSearch? search, EntityUid? grid, EntityUid obstacle)
    {
        if (search != null && (_bodyQuery.TryGetComponent(obstacle, out var body) && body.BodyType != BodyType.Static ||
            HasComp<DoorComponent>(obstacle) || Transform(obstacle).GridUid != grid))
            search.TransientObstruction = true;
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

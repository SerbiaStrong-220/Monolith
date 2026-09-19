using System.Numerics;
using Content.Shared._Exodus.ShipRepair;
using Content.Shared._Mono.ShipRepair.Components;
using Content.Shared.DoAfter;
using Content.Shared.Physics;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics;

namespace Content.Server._Exodus.ShipRepair;

public sealed partial class ShipRepairDroneSystem
{
    // Scratch space for rechecking a batch while a mobile obstruction is leaving.
    private readonly List<ShipRepairWork> _readyWork = new();

    private void UpdateQueues()
    {
        var activeQueues = 0;
        var activeQuery = EntityQueryEnumerator<ShipRepairWorkQueueComponent>();
        while (activeQuery.MoveNext(out _, out var active))
        {
            if (active.Drones.Count > 0)
                activeQueues++;
        }
        var quota = Math.Max(1, 256 / Math.Max(1, activeQueues));
        var query = EntityQueryEnumerator<ShipRepairWorkQueueComponent, ShipRepairDataComponent, MapGridComponent>();
        while (query.MoveNext(out var uid, out var queue, out var data, out var grid))
        {
            if (queue.Drones.Count == 0)
                continue;

            if (_timing.CurTime >= queue.NextNavigationRetry)
            {
                queue.Unreachable.Clear();
                queue.NextNavigationRetry = _timing.CurTime + TimeSpan.FromSeconds(60);
            }

            if (queue.Revision != data.Revision)
            {
                queue.Revision = data.Revision;
                queue.Entries.Clear();
                queue.Pending.Clear();
                queue.PendingSet.Clear();
                queue.Reservations.Clear();
                queue.ClearableReservations.Clear();
                queue.WorkPositions.Clear();
                InvalidateNavigation(uid);
                queue.Chunks = data.Chunks.GetEnumerator();
                queue.Chunk = null;
                queue.Indexed = false;
                queue.ScanIndex = 0;
                queue.NextScan = _timing.CurTime;
                queue.Bounds = grid.LocalAABB;
            }

            if (!queue.Indexed)
            {
                IndexSnapshot((uid, data), grid, queue, quota);
                continue;
            }

            if (_timing.CurTime < queue.NextScan || queue.Entries.Count == 0)
                continue;

            for (var i = 0; i < quota; i++)
            {
                var target = queue.Entries[queue.ScanIndex++];
                if (!queue.PendingSet.Contains(target) && !queue.Reservations.ContainsKey(target) &&
                    _repair.NeedsSnapshotRepair((uid, data), target))
                {
                    queue.Pending.Enqueue(target);
                    queue.PendingSet.Add(target);
                }

                if (queue.ScanIndex < queue.Entries.Count)
                    continue;

                queue.ScanIndex = 0;
                queue.NextScan = _timing.CurTime + TimeSpan.FromSeconds(2);
                break;
            }
        }
    }

    private void IndexSnapshot(Entity<ShipRepairDataComponent> ent, MapGridComponent grid,
        ShipRepairWorkQueueComponent queue, int budget)
    {
        var size = ent.Comp.ChunkSize;
        if (size <= 0)
            return;

        for (var i = 0; i < budget; i++)
        {
            if (queue.Chunk == null)
            {
                if (!queue.Chunks.MoveNext())
                {
                    queue.Indexed = true;
                    return;
                }
                var (position, chunk) = queue.Chunks.Current;
                queue.Chunk = chunk;
                queue.ChunkPosition = position * size;
                queue.TileIndex = 0;
                queue.Entities = chunk.Entities.GetEnumerator();
                var bottom = _map.TileCenterToVector(ent, grid, queue.ChunkPosition) - grid.TileSizeHalfVector;
                var top = _map.TileCenterToVector(ent, grid, queue.ChunkPosition + new Vector2i(size - 1, size - 1)) + grid.TileSizeHalfVector;
                queue.Bounds = queue.Bounds.Union(new Box2(bottom, top));
            }

            if (queue.TileIndex < queue.Chunk.Tiles.Length)
            {
                var index = queue.TileIndex++;
                if (queue.Chunk.Tiles[index] != Tile.Empty.TypeId)
                {
                    var tile = queue.ChunkPosition + new Vector2i(index % size, index / size);
                    queue.Entries.Add(new ShipRepairTarget(tile));
                }
                continue;
            }

            if (queue.Entities.MoveNext())
            {
                var (id, spec) = queue.Entities.Current;
                var tile = _map.LocalToTile(ent, grid, new EntityCoordinates(ent, spec.LocalPosition));
                queue.Entries.Add(new ShipRepairTarget(tile, id));
                continue;
            }
            queue.Chunk = null;
        }
    }

    private void TryChooseJob(Entity<ShipRepairDroneComponent> ent, Entity<ShipRepairToolComponent> tool,
        Entity<ShipRepairDataComponent> grid, ShipRepairWorkQueueComponent queue)
    {
        var position = _transform.ToCoordinates(grid.Owner, _transform.GetMapCoordinates(ent)).Position;
        RefreshNavigationFailures(ent, queue, position);
        if (ent.Comp.FocusTile is { } focus &&
            TryPlanDroneWork(ent, tool, grid, queue, focus, position, out var continuation))
        {
            AssignDroneWork(ent, grid, queue, focus, continuation);
            return;
        }
        ent.Comp.FocusTile = null;
        ShipRepairWork? selected = null;
        var selectedDeferred = true;
        var bestDistance = float.PositiveInfinity;
        var attempts = Math.Min(24, queue.Pending.Count);
        for (var i = 0; i < attempts; i++)
        {
            var target = queue.Pending.Dequeue();
            queue.PendingSet.Remove(target);
            if (queue.Reservations.ContainsKey(target) || !_repair.NeedsSnapshotRepair(grid, target))
                continue;

            if (target.EntityId != null && ent.Comp.RepairRadius == 0 &&
                _repair.NeedsSnapshotRepair(grid, new ShipRepairTarget(target.Tile)) ||
                ent.Comp.FailedTargets.TryGetValue(target, out var retry) && _timing.CurTime < retry ||
                !_repair.TryPlanRepair(tool, grid, target, true, out var work, checkMobileObstructions: false,
                    allowClearables: true))
            {
                EnqueueWork(queue, target);
                continue;
            }

            if (IsKnownUnreachable(ent, grid, queue, work, position) ||
                work.Operation == ShipRepairOperation.Restore && !work.Underfloor &&
                queue.WorkPositions.TryGetValue(target.Tile, out var worker) && worker != ent.Owner)
            {
                EnqueueWork(queue, target);
                continue;
            }

            var distance = Vector2.DistanceSquared(position, work.Position);
            var deferred = ent.Comp.DeferredSearches.Contains(target);
            if (selected != null && (deferred && !selectedDeferred || deferred == selectedDeferred && distance >= bestDistance))
            {
                EnqueueWork(queue, target);
                continue;
            }
            if (selected != null)
                EnqueueWork(queue, selected.Target);
            selected = work;
            selectedDeferred = deferred;
            bestDistance = distance;
        }

        if (selected == null)
            return;

        // Tile planning may select a higher-priority entry than the candidate drawn from the queue.
        EnqueueWork(queue, selected.Target);
        if (TryPlanDroneWork(ent, tool, grid, queue, selected.Target.Tile, position, out var plan))
            AssignDroneWork(ent, grid, queue, selected.Target.Tile, plan);
    }

    private bool TryPlanDroneWork(Entity<ShipRepairDroneComponent> ent, Entity<ShipRepairToolComponent> tool,
        Entity<ShipRepairDataComponent> grid, ShipRepairWorkQueueComponent queue, Vector2i center,
        Vector2 position, out ShipRepairPlan plan)
    {
        plan = new ShipRepairPlan { Grid = grid.Owner, Revision = grid.Comp.Revision };
        _repair.PlanRepairArea(tool, grid, center, ent.Comp.RepairRadius, true, plan,
            checkMobileObstructions: false, allowClearables: true);

        for (var i = plan.Work.Count - 1; i >= 0; i--)
        {
            var item = plan.Work[i];
            if (queue.Reservations.ContainsKey(item.Target) || IsKnownUnreachable(ent, grid, queue, item, position) ||
                ent.Comp.FailedTargets.TryGetValue(item.Target, out var retry) && _timing.CurTime < retry ||
                item.Operation == ShipRepairOperation.Restore && !item.Underfloor &&
                queue.WorkPositions.TryGetValue(item.Target.Tile, out var worker) && worker != ent.Owner ||
                item.Operation == ShipRepairOperation.Restore &&
                _repair.NeedsSnapshotRepair(grid, new ShipRepairTarget(item.Target.Tile)) &&
                queue.Reservations.ContainsKey(new ShipRepairTarget(item.Target.Tile)))
                plan.Work.RemoveAt(i);
        }

        _repair.PrepareConnectedRepairPlan(grid, plan);
        if (plan.Work.Count == 0)
            return false;

        // Ordinary drones still perform one operation per cycle, keeping SRD timings unchanged.
        if (ent.Comp.RepairRadius == 0 && plan.Work.Count > 1)
            plan.Work.RemoveRange(1, plan.Work.Count - 1);
        return true;
    }

    private void AssignDroneWork(Entity<ShipRepairDroneComponent> ent, Entity<ShipRepairDataComponent> grid,
        ShipRepairWorkQueueComponent queue, Vector2i center, ShipRepairPlan plan)
    {
        CancelJob(ent);
        var selected = plan.Work[0];
        ent.Comp.FocusTile = center;
        ent.Comp.Target = selected.Target;
        ent.Comp.Plan = plan;
        ent.Comp.NavigationDeadline = _timing.CurTime + ent.Comp.NavigationTimeout;
        ent.Comp.Repaths = 0;
        ent.Comp.BlockedDoors.Clear();
        // Reserve the actual batch before travelling, not after another drone has started approaching it.
        foreach (var item in plan.Work)
            queue.Reservations[item.Target] = ent;
        if (!StartNavigation(ent, grid, queue, selected))
            FailJob(ent);
    }

    private void StartRepair(Entity<ShipRepairDroneComponent> ent, Entity<ShipRepairToolComponent> tool,
        Entity<ShipRepairDataComponent> grid, ShipRepairWorkQueueComponent queue)
    {
        if (ent.Comp.Plan is not { } claimed || ent.Comp.Phased)
            return;

        _readyWork.Clear();
        foreach (var item in claimed.Work)
        {
            if (queue.Reservations.TryGetValue(item.Target, out var owner) && owner == ent.Owner &&
                _repair.TryPlanRepair(tool, grid, item.Target, true, out var current, checkTileSupport: false,
                    allowClearables: true) &&
                CanReachWork(ent, grid, current))
                _readyWork.Add(current);
        }

        if (_readyWork.Count < claimed.Work.Count)
            AskObstructingDronesToYield(ent, grid, queue, claimed);

        // Let a yielding drone leave before giving up. The navigation watchdog bounds this wait.
        if (_readyWork.Count == 0)
            return;

        // Do not spend a cycle rebuilding a machine whose prerequisite floor belongs to another cycle.
        for (var i = _readyWork.Count - 1; i >= 0; i--)
        {
            var item = _readyWork[i];
            if (item.Operation != ShipRepairOperation.Restore ||
                !_repair.NeedsSnapshotRepair(grid, new ShipRepairTarget(item.Target.Tile)))
                continue;
            var hasFloor = false;
            foreach (var floor in _readyWork)
            {
                if (floor.Operation == ShipRepairOperation.Tile && floor.Target.Tile == item.Target.Tile)
                {
                    hasFloor = true;
                    break;
                }
            }
            if (!hasFloor)
                _readyWork.RemoveAt(i);
        }
        if (_readyWork.Count == 0)
            return;

        var plan = new ShipRepairPlan { Grid = grid.Owner, Revision = grid.Comp.Revision };
        plan.Work.AddRange(_readyWork);
        _repair.PrepareConnectedRepairPlan(grid, plan);
        if (plan.Work.Count == 0)
            return;
        StopMoving(ent);
        ReleaseWorkReservations(ent, queue);
        ent.Comp.Plan = plan;
        foreach (var item in plan.Work)
            queue.Reservations[item.Target] = ent;

        if (PrepareWorkClearance(ent, grid, queue, plan))
            return;

        var duration = _repair.GetRepairDuration(plan, ent.Comp.RepairThroughput);
        var args = new DoAfterArgs(EntityManager, ent, duration,
            new ShipRepairDroneDoAfterEvent(), ent)
        {
            NeedHand = false,
            BreakOnMove = true,
            BreakOnDamage = true,
            MovementThreshold = 0.3f,
            CancelDuplicate = false,
        };
        if (!_doAfter.TryStartDoAfter(args, out var id))
        {
            FailJob(ent);
            return;
        }

        ent.Comp.RepairDoAfter = id;
        StartConstructionEffects(ent, tool, plan, duration);
        _audio.PlayPvs(tool.Comp.RepairSound, ent);
        SetVisual(ent);
    }

    private void OnRepairFinished(Entity<ShipRepairDroneComponent> ent, ref ShipRepairDroneDoAfterEvent args)
    {
        if (ent.Comp.RepairDoAfter != args.DoAfter.Id)
            return;

        ent.Comp.RepairDoAfter = null;
        if (args.Cancelled || args.Handled || !ent.Comp.Enabled || IsDisabledBody(ent) || !TryGetStation(ent, out var station) || !IsStationActive(station) ||
            ent.Comp.Command != ShipRepairDroneCommand.Repair || Transform(station).GridUid != ent.Comp.Grid ||
            ent.Comp.Plan is not { } plan || ent.Comp.Grid != plan.Grid || ent.Comp.Phased ||
            !TryComp<ShipRepairToolComponent>(ent, out var tool) ||
            !TryComp<ShipRepairDataComponent>(plan.Grid, out var data) || data.Revision != plan.Revision ||
            !_queueQuery.TryGetComponent(plan.Grid, out var queue) ||
            !CanServiceShip(ent, Transform(ent), plan.Grid, queue) || _containers.IsEntityInContainer(ent))
        {
            CancelJob(ent);
            ent.Comp.NextSearch = _timing.CurTime + ent.Comp.IdleInterval;
            return;
        }

        var completed = 0;
        // The shared planner orders connected floor, underfloor utilities, power, then other structures.
        foreach (var work in plan.Work)
        {
            if (!queue.Reservations.TryGetValue(work.Target, out var owner) || owner != ent.Owner ||
                !CanReachWork(ent, plan.Grid, work, allowClearables: false) ||
                !AreClearablesPrepared(ent, queue, work))
                continue;

            if (_repair.TryCompleteRepair((ent.Owner, tool), ent, plan, work, clearObstructions: true))
            {
                completed++;
                ent.Comp.FailedTargets.Remove(work.Target);
            }
            else
                ent.Comp.FailedTargets[work.Target] = _timing.CurTime + ent.Comp.RetryInterval;
        }
        args.Handled = true;
        if (completed == 0)
            FailJob(ent);
        else
        {
            var focus = ent.Comp.FocusTile;
            CancelJob(ent);
            ent.Comp.FocusTile = focus;
            ent.Comp.NextSearch = _timing.CurTime;
        }
    }

    private bool CanReachWork(Entity<ShipRepairDroneComponent> ent, EntityUid grid, ShipRepairWork work,
        Vector2? localOrigin = null, ShipRepairPathSearch? search = null,
        bool allowClearables = true, HashSet<EntityUid>? clearables = null)
    {
        var origin = localOrigin is { } point
            ? _transform.ToMapCoordinates(new EntityCoordinates(grid, point))
            : _transform.GetMapCoordinates(ent);
        var destination = _transform.ToMapCoordinates(new EntityCoordinates(grid, work.Position));
        var delta = destination.Position - origin.Position;
        var range = ent.Comp.RepairRange + ent.Comp.RepairRadius * 1.42f;
        if (origin.MapId != destination.MapId || delta.LengthSquared() > range * range)
            return false;
        if (work.Operation == ShipRepairOperation.Restore && !work.Underfloor &&
            delta.LengthSquared() < MathF.Pow(0.5f + ent.Comp.Clearance, 2))
            return false;
        if (delta.LengthSquared() < 0.0001f)
            return true;

        var rayLength = delta.Length();
        var local = _transform.ToCoordinates(grid, origin).Position;
        var ignoreSupport = work.Underfloor;
        if (work.WallMountArc is { } arc)
        {
            // Match normal wall-mount interaction, including directional mounts on rotated grids.
            var facing = Angle.FromWorldVec(local - work.Position);
            var difference = (work.WallMountDirection + work.Rotation - facing).Reduced().FlipPositive();
            ignoreSupport |= arc >= Math.Tau || difference < arc / 2 || Math.Tau - difference < arc / 2;
        }
        if (ignoreSupport && _mapGridQuery.TryGetComponent(grid, out var mapGrid))
        {
            // Service covered utilities and wall mounts at the target tile's edge, not through preceding walls.
            var lower = (Vector2) work.Target.Tile * mapGrid.TileSize;
            var bounds = new Box2(lower, lower + new Vector2(mapGrid.TileSize));
            if (bounds.Contains(local))
                return true;
            var direction = Vector2.Normalize(work.Position - local);
            if (new Ray(local, direction).Intersects(bounds, out var entry, out _))
                rayLength = Math.Max(0f, entry - 0.02f);
        }

        var ray = new CollisionRay(origin.Position, Vector2.Normalize(delta),
            (int) (CollisionGroup.Impassable | CollisionGroup.InteractImpassable));
        foreach (var hit in _physics.IntersectRay(origin.MapId, ray, rayLength, ent, returnOnFirstHit: false))
        {
            if (CanDroneClear(ent, grid, hit.HitEntity) &&
                (allowClearables || ent.Comp.PreparedClearables.Contains(hit.HitEntity)))
            {
                clearables?.Add(hit.HitEntity);
                continue;
            }
            if (hit.HitEntity == work.Original || work.WallMountArc == null &&
                _repair.IsRepairSnapshotNeighbour(grid, work.Target, hit.HitEntity))
                continue;
            TrackSearchObstruction(search, grid, hit.HitEntity);
            return false;
        }
        return true;
    }

    private void FailJob(Entity<ShipRepairDroneComponent> ent)
    {
        if (ent.Comp.Command == ShipRepairDroneCommand.Return)
        {
            CancelJob(ent);
            ent.Comp.ReturnBlocked = true;
            ent.Comp.NextSearch = _timing.CurTime + ent.Comp.RetryInterval;
            return;
        }
        // Do not immediately reacquire the same failed batch through a different center tile.
        if (ent.Comp.Plan is { } plan)
        {
            foreach (var work in plan.Work)
                ent.Comp.FailedTargets[work.Target] = _timing.CurTime + ent.Comp.RetryInterval;
        }
        if (ent.Comp.Target is { } target)
            ent.Comp.FailedTargets[target] = _timing.CurTime + ent.Comp.RetryInterval;
        if (ent.Comp.WorkTile is { } tile)
            ent.Comp.FailedPositions[tile] = _timing.CurTime + ent.Comp.RetryInterval;
        CancelJob(ent);
        ent.Comp.NextSearch = _timing.CurTime + ent.Comp.IdleInterval;
    }

    private void CancelJob(Entity<ShipRepairDroneComponent> ent)
    {
        CancelClearance(ent);
        ClearConstructionEffects(ent);
        // Clear IDs before cancellation, which can synchronously deliver completion events.
        var repair = ent.Comp.RepairDoAfter;
        var pry = ent.Comp.PryDoAfter;
        ent.Comp.RepairDoAfter = null;
        ent.Comp.PryDoAfter = null;
        ent.Comp.PryTarget = null;
        if (_doAfter.IsRunning(repair))
            _doAfter.Cancel(repair);
        if (_doAfter.IsRunning(pry))
            _doAfter.Cancel(pry);
        StopMoving(ent);

        if (ent.Comp.Grid is { } grid && _queueQuery.TryGetComponent(grid, out var queue))
        {
            ReleaseWorkReservations(ent, queue);
            ReleaseWorkPosition(ent, queue);
        }
        ent.Comp.Target = null;
        ent.Comp.FocusTile = null;
        ent.Comp.Plan = null;
        ent.Comp.Search = null;
        ent.Comp.Path.Clear();
        ent.Comp.PathIndex = 0;
        ent.Comp.Settling = false;
        ent.Comp.Yielding = false;
        ent.Comp.ClearanceState = ShipRepairClearanceState.None;
        ent.Comp.WorkTile = null;
        ent.Comp.BestWaypointDistance = float.PositiveInfinity;
        if (!TerminatingOrDeleted(ent))
            SetVisual(ent);
    }
}

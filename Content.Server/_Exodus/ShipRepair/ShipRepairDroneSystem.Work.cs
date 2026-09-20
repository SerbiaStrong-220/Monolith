using System.Numerics;
using Content.Shared._Exodus.ShipRepair;
using Content.Shared._Mono.ShipRepair.Components;
using Content.Shared.DoAfter;
using Content.Shared.Physics;
using Content.Shared.Tag;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics;
using Robust.Shared.Prototypes;

namespace Content.Server._Exodus.ShipRepair;

public sealed partial class ShipRepairDroneSystem
{
    private static readonly ProtoId<TagPrototype> WallTag = "Wall";
    private static readonly ProtoId<TagPrototype> WindowTag = "Window";

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
                queue.EntriesByTile.Clear();
                queue.TargetStages.Clear();
                queue.PaletteStages.Clear();
                queue.Stages.Clear();
                queue.Reservations.Clear();
                queue.ClearableReservations.Clear();
                queue.WorkPositions.Clear();
                queue.DirtyTargets.Clear();
                InvalidateNavigation(uid);
                queue.Chunks = data.Chunks.GetEnumerator();
                queue.Chunk = null;
                queue.Indexed = false;
                queue.ScanIndex = 0;
                queue.NextScan = _timing.CurTime;
                queue.Bounds = grid.LocalAABB;
            }

            if (_timing.CurTime >= queue.NextStageRetry)
            {
                queue.StageRetryRevision++;
                queue.NextStageRetry = _timing.CurTime + TimeSpan.FromSeconds(30);
            }

            if (!queue.Indexed)
            {
                IndexSnapshot((uid, data), grid, queue, quota);
                continue;
            }

            var remainingQuota = quota;
            while (remainingQuota > 0 && queue.DirtyTargets.Count > 0)
            {
                ShipRepairTarget dirtyTarget = default;
                foreach (var target in queue.DirtyTargets)
                {
                    dirtyTarget = target;
                    break;
                }

                queue.DirtyTargets.Remove(dirtyTarget);
                RefreshQueuedWork((uid, data), queue, dirtyTarget);
                remainingQuota--;
            }

            if (remainingQuota == 0 || _timing.CurTime < queue.NextScan || queue.Entries.Count == 0)
                continue;

            for (var i = 0; i < remainingQuota; i++)
            {
                var target = queue.Entries[queue.ScanIndex++];
                RefreshQueuedWork((uid, data), queue, target);

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
                    IndexWorkTarget(ent, queue, new ShipRepairTarget(tile), ShipRepairStage.Floor);
                }
                continue;
            }

            if (queue.Entities.MoveNext())
            {
                var (id, spec) = queue.Entities.Current;
                var tile = _map.LocalToTile(ent, grid, new EntityCoordinates(ent, spec.LocalPosition));
                if (!queue.PaletteStages.TryGetValue(spec.ProtoIndex, out var stage))
                {
                    stage = _repair.GetSnapshotRepairStage(ent.Comp, spec);
                    queue.PaletteStages.Add(spec.ProtoIndex, stage);
                }
                IndexWorkTarget(ent, queue, new ShipRepairTarget(tile, id), stage);
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
        var budget = 24;
        while (budget > 0 && GetCurrentRepairStage(ent, queue) is { } stage)
        {
            if (TryChooseGroupJob(ent, tool, grid, queue, position, stage, ref budget))
                return;
            if (GetStageProbe(ent.Comp, queue, stage).Remaining > 0)
                return;
        }
    }

    private bool TryChooseGroupJob(Entity<ShipRepairDroneComponent> ent, Entity<ShipRepairToolComponent> tool,
        Entity<ShipRepairDataComponent> grid, ShipRepairWorkQueueComponent queue, Vector2 position,
        ShipRepairStage stage, ref int budget)
    {
        if (ent.Comp.RepairRadius == 0 && ent.Comp.FocusTile is { } focus &&
            TryConsumeWorkSelectionBudget() &&
            TryPlanDroneWork(ent, tool, grid, queue, focus, position, stage, out var continuation))
        {
            AssignDroneWork(ent, grid, queue, focus, continuation);
            return true;
        }
        _batchCandidates.Clear();
        _batchCenters.Clear();
        if (ent.Comp.RepairRadius > 0 && ent.Comp.FocusTile is { } previous)
            AddBatchCenters(ent, grid, queue, stage, previous, position);
        ShipRepairWork? selected = null;
        var selectedDeferred = true;
        var bestDistance = float.PositiveInfinity;
        var pending = queue.Stages[stage];
        var probe = GetStageProbe(ent.Comp, queue, stage);
        while (budget > 0 && probe.Remaining > 0 && pending.Targets.Count > 0)
        {
            if (!TryConsumeWorkSelectionBudget())
                return false;

            budget--;
            probe.Cursor %= pending.Targets.Count;
            var target = pending.Targets[probe.Cursor++];
            probe.Remaining--;
            if (!_repair.NeedsSnapshotRepair(grid, target))
            {
                RemoveQueuedWork(queue, target);
                probe = GetStageProbe(ent.Comp, queue, stage);
                continue;
            }
            if (queue.Reservations.ContainsKey(target))
                continue;
            if (target.EntityId != null && (ent.Comp.RepairRadius == 0 || stage != ShipRepairStage.Floor) &&
                _repair.NeedsSnapshotRepair(grid, new ShipRepairTarget(target.Tile)) ||
                ent.Comp.FailedTargets.TryGetValue(target, out var retry) && _timing.CurTime < retry ||
                !_repair.TryPlanRepair(tool, grid, target, true, out var work, checkMobileObstructions: false,
                    allowClearables: true))
                continue;
            if (IsKnownUnreachable(ent, grid, queue, work, position) ||
                work.Operation == ShipRepairOperation.Restore && !work.Underfloor &&
                queue.WorkPositions.TryGetValue(target.Tile, out var worker) && worker != ent.Owner)
                continue;
            if (ent.Comp.RepairRadius > 0)
                AddBatchCenters(ent, grid, queue, stage, target.Tile, position);
            var distance = Vector2.DistanceSquared(position, work.Position);
            var deferred = ent.Comp.DeferredSearches.Contains(target);
            if (selected != null && (deferred && !selectedDeferred || deferred == selectedDeferred && distance >= bestDistance))
                continue;
            selected = work;
            selectedDeferred = deferred;
            bestDistance = distance;
        }
        if (ent.Comp.RepairRadius > 0 && TryAssignBatch(ent, tool, grid, queue, position, stage))
            return true;
        if (selected == null || !TryConsumeWorkSelectionBudget() ||
            !TryPlanDroneWork(ent, tool, grid, queue, selected.Target.Tile, position, stage, out var plan))
            return false;
        AssignDroneWork(ent, grid, queue, selected.Target.Tile, plan);
        return true;
    }

    private bool TryPlanDroneWork(Entity<ShipRepairDroneComponent> ent, Entity<ShipRepairToolComponent> tool,
        Entity<ShipRepairDataComponent> grid, ShipRepairWorkQueueComponent queue, Vector2i center,
        Vector2 position, ShipRepairStage stage, out ShipRepairPlan plan)
    {
        plan = new ShipRepairPlan { Grid = grid.Owner, Revision = grid.Comp.Revision };
        _repair.PlanRepairArea(tool, grid, center, ent.Comp.RepairRadius, true, plan,
            checkMobileObstructions: false, allowClearables: true);

        for (var i = plan.Work.Count - 1; i >= 0; i--)
        {
            var item = plan.Work[i];
            if (GetRepairGroup(item.Stage) != stage || queue.Reservations.ContainsKey(item.Target) || IsKnownUnreachable(ent, grid, queue, item, position) ||
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
        ShipRepairWorkQueueComponent queue, Vector2i center, ShipRepairPlan plan, Vector2? workPosition = null)
    {
        CancelJob(ent);
        var selected = plan.Work[0];
        ent.Comp.FocusTile = center;
        ent.Comp.Target = selected.Target;
        ent.Comp.Plan = plan;
        ent.Comp.BatchWorkPosition = workPosition;
        ent.Comp.NavigationDeadline = _timing.CurTime + ent.Comp.NavigationTimeout;
        ent.Comp.Repaths = 0;
        // Reserve the actual batch before travelling, not after another drone has started approaching it.
        foreach (var item in plan.Work)
            ReserveWork(queue, item.Target, ent);
        ent.Comp.AssignmentWorkRevision = queue.WorkRevision;
        if (!StartNavigation(ent, grid, queue, selected) && !HandleBatchApproachFailure(ent, grid, queue))
            FailJob(ent);
    }

    private void StartRepair(Entity<ShipRepairDroneComponent> ent, Entity<ShipRepairToolComponent> tool,
        Entity<ShipRepairDataComponent> grid, ShipRepairWorkQueueComponent queue)
    {
        if (ent.Comp.Plan is not { } claimed || ent.Comp.Phased)
            return;

        if (ShouldYieldRepairStage(ent, (grid.Owner, queue)))
        {
            CancelJob(ent);
            ent.Comp.NextSearch = _timing.CurTime;
            return;
        }

        _readyWork.Clear();
        // Another drone may have published part of this batch while we were travelling.
        // Drop those entries before checking approach and repair clearance; otherwise an
        // entirely stale batch can keep its reservations forever at the old work position.
        for (var i = claimed.Work.Count - 1; i >= 0; i--)
        {
            var item = claimed.Work[i];
            if (!_repair.NeedsSnapshotRepair(grid, item.Target))
            {
                ReleaseWorkReservation(ent, queue, item.Target);
                RefreshQueuedWork(grid, queue, item.Target);
                claimed.Work.RemoveAt(i);
                continue;
            }

            if (queue.Reservations.TryGetValue(item.Target, out var owner) && owner == ent.Owner &&
                _repair.TryPlanRepair(tool, grid, item.Target, true, out var current, checkTileSupport: false,
                    allowClearables: true) &&
                CanReachWork(ent, grid, current))
                _readyWork.Add(current);
        }

        if (claimed.Work.Count == 0)
        {
            CancelJob(ent);
            ent.Comp.NextSearch = _timing.CurTime;
            return;
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
            ReserveWork(queue, item.Target, ent);

        if (PrepareWorkClearance(ent, grid, queue, plan))
            return;

        // Audit while the tool runs; only publishing the construction needs final approval.
        // An unsafe combined closure can be split into independently checked operations on completion.
        CheckRepairClosure(ent, tool, grid, queue, plan, cancelOnFailure: false);
        if (ent.Comp.Plan == null)
            return;

        StartRepairTimer(ent, tool, grid, queue, plan);
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

        args.Handled = true;
        ent.Comp.RepairElapsed += args.DoAfter.Args.Delay;
        SetRepairReady(ent);
        FinishRepair(ent, (ent.Owner, tool), (plan.Grid, data), queue, plan);
    }

    private bool CanReachWork(Entity<ShipRepairDroneComponent> ent, EntityUid grid, ShipRepairWork work,
        Vector2? localOrigin = null, ShipRepairPathSearch? search = null,
        bool allowClearables = true, HashSet<EntityUid>? clearables = null, ShipRepairClosureCheck? closure = null,
        ShipRepairTarget? onlyClosureObstacle = null)
    {
        var origin = localOrigin is { } point
            ? _transform.ToMapCoordinates(new EntityCoordinates(grid, point))
            : _transform.GetMapCoordinates(ent);
        var destination = _transform.ToMapCoordinates(new EntityCoordinates(grid, work.Position));
        var delta = destination.Position - origin.Position;
        var range = ent.Comp.RepairRange + ent.Comp.RepairRadius * 1.42f;
        if (origin.MapId != destination.MapId)
            return false;
        var local = _transform.ToCoordinates(grid, origin).Position;
        var allowStructuralObstructedAccess = CanReachStructuralWorkThroughObstruction(ent, grid, work, local);
        if (delta.LengthSquared() > range * range && !allowStructuralObstructedAccess)
            return false;
        if (WorkOverlapsDrone(ent, work, local))
            return false;
        if (delta.LengthSquared() < 0.0001f)
            return true;

        var rayLength = delta.Length();
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
        if (closure != null && ClosureIntersects(closure, local,
                local + Vector2.Normalize(work.Position - local) * rayLength, 0f, work, onlyClosureObstacle))
            return false;
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
            if (allowStructuralObstructedAccess && IsStaticHullObstacle(grid, hit.HitEntity))
                continue;
            return false;
        }
        return true;
    }

    private bool CanReachStructuralWorkThroughObstruction(Entity<ShipRepairDroneComponent> ent, EntityUid grid,
        ShipRepairWork work, Vector2 localOrigin)
    {
        if (!work.AllowStructuralObstructedAccess || ent.Comp.StructuralRepairTileRange <= 0 ||
            !_mapGridQuery.TryGetComponent(grid, out var mapGrid))
            return false;

        var range = ent.Comp.StructuralRepairTileRange * mapGrid.TileSize;
        return Vector2.DistanceSquared(localOrigin, work.Position) <= range * range;
    }

    private bool IsStaticHullObstacle(EntityUid grid, EntityUid obstacle)
    {
        if (obstacle == grid)
            return true;
        if (_droneDoorQuery.HasComponent(obstacle) || _droneFirelockQuery.HasComponent(obstacle))
            return false;
        if (!_bodyQuery.TryGetComponent(obstacle, out var body) || body.BodyType != BodyType.Static ||
            !_xformQuery.TryGetComponent(obstacle, out var xform) || xform.GridUid != grid)
            return false;

        return _tags.HasTag(obstacle, WallTag) || _tags.HasTag(obstacle, WindowTag);
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
        ent.Comp.BatchWorkPosition = null;
        ent.Comp.ClosureCheck = null;
        ent.Comp.RepairReady = false;
        ent.Comp.RepairElapsed = TimeSpan.Zero;
        ent.Comp.RepairTimerDuration = TimeSpan.Zero;
        ent.Comp.PublicationPlan = null;
        ent.Comp.PublishIndividually = false;
        ent.Comp.RepairReposition = null;
        ent.Comp.NextRepairReposition = TimeSpan.Zero;
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

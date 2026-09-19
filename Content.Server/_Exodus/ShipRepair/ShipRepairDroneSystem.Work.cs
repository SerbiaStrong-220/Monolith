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

            if (queue.Revision != data.Revision)
            {
                queue.Revision = data.Revision;
                queue.Entries.Clear();
                queue.Pending.Clear();
                queue.PendingSet.Clear();
                queue.Reservations.Clear();
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
                !_repair.TryPlanRepair(tool, grid, target, true, out var work))
            {
                queue.Pending.Enqueue(target);
                queue.PendingSet.Add(target);
                continue;
            }

            ent.Comp.Target = target;
            queue.Reservations[target] = ent;
            ent.Comp.BlockedDoors.Clear();
            if (!StartNavigation(ent, grid, queue, work))
                FailJob(ent);
            return;
        }
    }

    private void StartRepair(Entity<ShipRepairDroneComponent> ent, Entity<ShipRepairToolComponent> tool,
        Entity<ShipRepairDataComponent> grid, ShipRepairWorkQueueComponent queue)
    {
        if (ent.Comp.Target is not { } target || ent.Comp.Phased)
            return;

        var plan = new ShipRepairPlan { Grid = grid.Owner, Revision = grid.Comp.Revision };
        if (ent.Comp.RepairRadius > 0)
            _repair.PlanRepairArea(tool, grid, target.Tile, ent.Comp.RepairRadius, true, plan);
        else if (_repair.TryPlanRepair(tool, grid, target, true, out var work))
            plan.Work.Add(work);

        // A fleet batch includes only accessible and unreserved work at the moment it starts.
        for (var i = plan.Work.Count - 1; i >= 0; i--)
        {
            var item = plan.Work[i];
            if (queue.Reservations.TryGetValue(item.Target, out var owner) && owner != ent.Owner ||
                !CanReachWork(ent, grid, item))
                plan.Work.RemoveAt(i);
        }

        if (plan.Work.Count == 0)
        {
            FailJob(ent);
            return;
        }

        StopMoving(ent);
        ent.Comp.Plan = plan;
        foreach (var item in plan.Work)
            queue.Reservations[item.Target] = ent;

        var args = new DoAfterArgs(EntityManager, ent, _repair.GetRepairDuration(plan, ent.Comp.RepairThroughput),
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
        _audio.PlayPvs(tool.Comp.RepairSound, ent);
        SetVisual(ent);
    }

    private void OnRepairFinished(Entity<ShipRepairDroneComponent> ent, ref ShipRepairDroneDoAfterEvent args)
    {
        if (ent.Comp.RepairDoAfter != args.DoAfter.Id)
            return;

        ent.Comp.RepairDoAfter = null;
        if (args.Cancelled || args.Handled || !ent.Comp.Enabled || IsDisabledBody(ent) || !HasLinks(ent) ||
            ent.Comp.Plan is not { } plan || ent.Comp.Grid != plan.Grid || ent.Comp.Phased ||
            !TryComp<ShipRepairToolComponent>(ent, out var tool) ||
            !TryComp<ShipRepairDataComponent>(plan.Grid, out var data) || data.Revision != plan.Revision ||
            !_queueQuery.TryGetComponent(plan.Grid, out var queue) ||
            !CanServiceShip(ent, Transform(ent), plan.Grid, queue) || _containers.IsEntityInContainer(ent))
        {
            CancelJob(ent);
            return;
        }

        // Complete floors first, then rebuild objects, then heal surviving structures.
        for (var operation = ShipRepairOperation.Tile; operation <= ShipRepairOperation.Heal; operation++)
        foreach (var work in plan.Work)
        {
            if (work.Operation != operation ||
                !queue.Reservations.TryGetValue(work.Target, out var owner) || owner != ent.Owner ||
                !CanReachWork(ent, plan.Grid, work))
                continue;

            _repair.TryCompleteRepair((ent.Owner, tool), ent, plan, work);
        }
        args.Handled = true;
        CancelJob(ent);
        ent.Comp.NextSearch = _timing.CurTime;
    }

    private bool CanReachWork(Entity<ShipRepairDroneComponent> ent, EntityUid grid, ShipRepairWork work,
        Vector2? localOrigin = null)
    {
        var origin = localOrigin is { } point
            ? _transform.ToMapCoordinates(new EntityCoordinates(grid, point))
            : _transform.GetMapCoordinates(ent);
        var destination = _transform.ToMapCoordinates(new EntityCoordinates(grid, work.Position));
        var delta = destination.Position - origin.Position;
        var range = ent.Comp.RepairRange + ent.Comp.RepairRadius * 1.42f;
        if (origin.MapId != destination.MapId || delta.LengthSquared() > range * range)
            return false;
        if (work.Operation == ShipRepairOperation.Restore &&
            delta.LengthSquared() < MathF.Pow(0.5f + ent.Comp.Clearance, 2))
            return false;
        if (delta.LengthSquared() < 0.0001f)
            return true;

        var ray = new CollisionRay(origin.Position, Vector2.Normalize(delta),
            (int) (CollisionGroup.Impassable | CollisionGroup.InteractImpassable));
        foreach (var hit in _physics.IntersectRay(origin.MapId, ray, delta.Length(), ent, returnOnFirstHit: false))
        {
            if (hit.HitEntity != work.Original)
                return false;
        }
        return true;
    }

    private void FailJob(Entity<ShipRepairDroneComponent> ent)
    {
        if (ent.Comp.Target is { } target)
            ent.Comp.FailedTargets[target] = _timing.CurTime + ent.Comp.RetryInterval;
        CancelJob(ent);
    }

    private void CancelJob(Entity<ShipRepairDroneComponent> ent)
    {
        // Clear IDs before cancellation, which can synchronously deliver completion events.
        var repair = ent.Comp.RepairDoAfter;
        var pry = ent.Comp.PryDoAfter;
        ent.Comp.RepairDoAfter = null;
        ent.Comp.PryDoAfter = null;
        if (_doAfter.IsRunning(repair))
            _doAfter.Cancel(repair);
        if (_doAfter.IsRunning(pry))
            _doAfter.Cancel(pry);
        StopMoving(ent);

        if (ent.Comp.Grid is { } grid && _queueQuery.TryGetComponent(grid, out var queue))
        {
            if (ent.Comp.Target is { } target && queue.Reservations.GetValueOrDefault(target) == ent.Owner)
                queue.Reservations.Remove(target);
            if (ent.Comp.Plan != null)
            {
                foreach (var work in ent.Comp.Plan.Work)
                {
                    if (queue.Reservations.GetValueOrDefault(work.Target) == ent.Owner)
                        queue.Reservations.Remove(work.Target);
                }
            }
        }
        ent.Comp.Target = null;
        ent.Comp.Plan = null;
        ent.Comp.Search = null;
        ent.Comp.Path.Clear();
        ent.Comp.PathIndex = 0;
        if (!TerminatingOrDeleted(ent))
            SetVisual(ent);
    }
}

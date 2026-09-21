using System.Numerics;
using Content.Shared._Exodus.ShipRepair;
using Content.Shared._Mono.ShipRepair.Components;
using Robust.Shared.Map;

namespace Content.Server._Exodus.ShipRepair;

public sealed partial class ShipRepairDroneSystem
{
    private readonly HashSet<EntityUid> _repairBlockers = new();

    private static void EnqueueWork(ShipRepairWorkQueueComponent queue, ShipRepairTarget target)
    {
        if (!queue.TargetStages.TryGetValue(target, out var stage))
            return;
        var pending = queue.Stages[stage];
        if (pending.Indices.TryAdd(target, pending.Targets.Count))
        {
            pending.Targets.Add(target);
            ChangeWorkTileCount(pending, target.Tile, 1);
            pending.Revision++;
            pending.DiscoveryRevision = ++queue.WorkRevision;
        }
        if (queue.Reservations.ContainsKey(target))
            pending.Reserved.Add(target);
    }

    private void ReleaseWorkReservations(Entity<ShipRepairDroneComponent> ent, ShipRepairWorkQueueComponent queue)
    {
        if (ent.Comp.Plan is not { } plan)
            return;
        _snapshotQuery.TryGetComponent(plan.Grid, out var data);
        foreach (var work in plan.Work)
        {
            if (!queue.Reservations.TryGetValue(work.Target, out var owner) || owner != ent.Owner)
                continue;
            queue.Reservations.Remove(work.Target);
            if (queue.TargetStages.TryGetValue(work.Target, out var stage) && queue.Stages[stage].Reserved.Remove(work.Target))
                queue.Stages[stage].Revision++;
            if (plan.Revision == queue.Revision && data != null)
                RefreshQueuedWork((plan.Grid, data), queue, work.Target);
        }
    }

    private void ReleaseWorkReservation(Entity<ShipRepairDroneComponent> ent, ShipRepairWorkQueueComponent queue,
        ShipRepairTarget target)
    {
        if (!queue.Reservations.TryGetValue(target, out var owner) || owner != ent.Owner)
            return;
        queue.Reservations.Remove(target);
        if (queue.TargetStages.TryGetValue(target, out var stage) && queue.Stages[stage].Reserved.Remove(target))
            queue.Stages[stage].Revision++;
    }

    private static void ReleaseWorkPosition(Entity<ShipRepairDroneComponent> ent, ShipRepairWorkQueueComponent queue)
    {
        if (ent.Comp.WorkTile is { } tile && queue.WorkPositions.TryGetValue(tile, out var owner) && owner == ent.Owner)
            queue.WorkPositions.Remove(tile);
        ent.Comp.WorkTile = null;
    }

    private bool IsWorkPositionAvailable(Entity<ShipRepairDroneComponent> ent, ShipRepairWorkQueueComponent queue,
        Vector2i tile, Vector2 position)
    {
        if (queue.WorkPositions.TryGetValue(tile, out var owner) && owner != ent.Owner ||
            ent.Comp.FailedPositions.TryGetValue(tile, out var retry) && _timing.CurTime < retry)
            return false;

        // A work position must not occupy another construction site (including this drone's own batch).
        foreach (var uid in queue.Drones)
        {
            if (!_droneQuery.TryGetComponent(uid, out var drone) || drone.Plan == null)
                continue;
            foreach (var work in drone.Plan.Work)
            {
                if (queue.Reservations.TryGetValue(work.Target, out var worker) && worker == uid &&
                    WorkOverlapsDrone(ent, work, position))
                    return false;
            }
        }
        return true;
    }

    private bool TryClaimWorkPosition(Entity<ShipRepairDroneComponent> ent, ShipRepairWorkQueueComponent queue,
        Vector2i tile, Vector2 position)
    {
        if (!IsWorkPositionAvailable(ent, queue, tile, position))
            return false;
        ReleaseWorkPosition(ent, queue);
        queue.WorkPositions[tile] = ent;
        ent.Comp.WorkTile = tile;
        return true;
    }

    private void AskObstructingDronesToYield(Entity<ShipRepairDroneComponent> ent,
        Entity<ShipRepairDataComponent> grid, ShipRepairWorkQueueComponent queue, ShipRepairPlan plan)
    {
        _repairBlockers.Clear();
        foreach (var work in plan.Work)
            _repair.GetRepairObstructions(grid, work, _repairBlockers);

        foreach (var uid in _repairBlockers)
        {
            if (uid == ent.Owner || TerminatingOrDeleted(uid) || !_droneQuery.TryGetComponent(uid, out var drone) ||
                !drone.Enabled || drone.Command != ShipRepairDroneCommand.Repair || drone.Grid != grid.Owner || drone.Target != null || drone.Yielding ||
                drone.ClearanceState != ShipRepairClearanceState.None ||
                drone.RepairDoAfter != null || _timing.CurTime < drone.NextYield ||
                IsDisabledBody(uid) || _containers.IsEntityInContainer(uid))
                continue;

            // Working drones keep their job. Only idle drones yield, and the first request owns the move.
            // This prevents two workers repeatedly cancelling each other's repairs or swapping destinations.
            TryYield((uid, drone), grid, queue);
        }
    }

    private bool TryYield(Entity<ShipRepairDroneComponent> ent, Entity<ShipRepairDataComponent> grid,
        ShipRepairWorkQueueComponent queue)
    {
        ent.Comp.NextYield = _timing.CurTime + ent.Comp.IdleInterval;
        if (!_mapGridQuery.TryGetComponent(grid, out var mapGrid))
            return false;

        var position = _transform.ToCoordinates(grid.Owner, _transform.GetMapCoordinates(ent)).Position;
        var center = _map.LocalToTile(grid, mapGrid, new EntityCoordinates(grid, position));
        Vector2i? destination = null;
        var bestDistance = float.PositiveInfinity;
        var bounds = queue.Bounds.Enlarged(ent.Comp.ExteriorMargin);
        for (var y = -3; y <= 3; y++)
        for (var x = -3; x <= 3; x++)
        {
            var tile = center + new Vector2i(x, y);
            var point = _map.TileCenterToVector(grid, mapGrid, tile);
            var distance = Vector2.DistanceSquared(position, point);
            if (distance < ent.Comp.ArrivalRange * ent.Comp.ArrivalRange || distance >= bestDistance ||
                !bounds.Contains(point) ||
                !IsWorkPositionAvailable(ent, queue, tile, point) || !IsClear(ent, grid, point) ||
                !ent.Comp.CanPhase && !IsSegmentClear(ent, grid, position, point, false, out _))
                continue;
            destination = tile;
            bestDistance = distance;
        }

        if (destination is not { } goal)
            return false;
        var end = _map.TileCenterToVector(grid, mapGrid, goal);
        if (!TryClaimWorkPosition(ent, queue, goal, end))
            return false;

        StopMoving(ent);
        ent.Comp.Yielding = true;
        ent.Comp.Settling = false;
        ent.Comp.Search = null;
        ent.Comp.NavigationDeadline = _timing.CurTime + ent.Comp.StuckTimeout * 2;
        SetDirectPath(ent, position, end);
        return true;
    }
}

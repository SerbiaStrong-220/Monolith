using System.Numerics;
using Content.Shared._Exodus.ShipRepair;
using Content.Shared._Mono.ShipRepair.Components;
using Robust.Shared.Map;

namespace Content.Server._Exodus.ShipRepair;

public sealed partial class ShipRepairDroneSystem
{
    /// <summary>A short physical move keeps the completed timer, quotes and reservations.</summary>
    private bool TryRepositionRepair(Entity<ShipRepairDroneComponent> ent, Entity<ShipRepairDataComponent> grid,
        ShipRepairWorkQueueComponent queue, ShipRepairPlan plan)
    {
        if (_timing.CurTime < ent.Comp.NextRepairReposition || ent.Comp.RepairReposition != null ||
            !_mapGridQuery.TryGetComponent(grid, out var mapGrid) || ent.Comp.ClearanceRecoveryStep <= 0)
            return false;
        ent.Comp.NextRepairReposition = _timing.CurTime + ent.Comp.ClearanceRecoveryRetry;
        var origin = _transform.ToCoordinates(grid.Owner, _transform.GetMapCoordinates(ent)).Position;
        Vector2? best = null;
        var bestCount = 0;
        foreach (var work in plan.Work)
        {
            if (CanReachWork(ent, grid, work, origin, allowClearables: false))
                bestCount++;
        }
        // Moving cannot fix a foreign obstruction or an unsafe closure if every target is already reachable.
        if (bestCount == plan.Work.Count)
            return false;
        var steps = Math.Min(8, (int) MathF.Ceiling(ent.Comp.ClearanceRecoveryDistance / ent.Comp.ClearanceRecoveryStep));
        for (var step = 1; step <= steps; step++)
        for (var direction = 0; direction < 8; direction++)
        {
            var point = origin + new Angle(direction * Math.Tau / 8).ToVec() * (step * ent.Comp.ClearanceRecoveryStep);
            var tile = _map.LocalToTile(grid, mapGrid, new EntityCoordinates(grid, point));
            if (!queue.Bounds.Enlarged(ent.Comp.ExteriorMargin).Contains(point) ||
                !IsWorkPositionAvailable(ent, queue, tile, point) || !IsClear(ent, grid, point) ||
                !IsSegmentClear(ent, grid, origin, point, false, out _, recovering: true))
                continue;
            var count = 0;
            foreach (var work in plan.Work)
            {
                if (CanReachWork(ent, grid, work, point, allowClearables: false))
                    count++;
            }
            if (count <= bestCount)
                continue;
            bestCount = count;
            best = point;
        }
        if (best is not { } destination || !TryClaimWorkPosition(ent, queue,
                _map.LocalToTile(grid, mapGrid, new EntityCoordinates(grid, destination)), destination))
            return false;
        ent.Comp.RepairReposition = destination;
        ent.Comp.RepairRepositionDeadline = _timing.CurTime + ent.Comp.ClearanceRecoveryTimeout;
        ent.Comp.Settling = false;
        ent.Comp.PublicationPlan = null;
        ent.Comp.ClosureCheck = null;
        SetMovementTarget(ent, grid, destination, ent.Comp.ClearanceRecoveryRange, ent.Comp.RepairSpeedLimit);
        return true;
    }

    private bool UpdateRepairReposition(Entity<ShipRepairDroneComponent> ent, Entity<ShipRepairDataComponent> grid,
        ShipRepairWorkQueueComponent queue, ShipRepairPlan plan)
    {
        if (ent.Comp.RepairReposition is not { } destination)
            return false;
        var position = _transform.ToCoordinates(grid.Owner, _transform.GetMapCoordinates(ent)).Position;
        if (_timing.CurTime >= ent.Comp.RepairRepositionDeadline ||
            !IsSegmentClear(ent, grid, position, destination, false, out _, recovering: true))
        {
            StopMoving(ent);
            ent.Comp.RepairReposition = null;
            ent.Comp.Settling = false;
            ReleaseWorkPosition(ent, queue);
            return true;
        }
        if (Vector2.DistanceSquared(position, destination) > MathF.Pow(ent.Comp.ClearanceRecoveryRange, 2))
        {
            SetMovementTarget(ent, grid, destination, ent.Comp.ClearanceRecoveryRange, ent.Comp.RepairSpeedLimit);
            return true;
        }
        StopMoving(ent);
        if (!ent.Comp.Settling)
        {
            ent.Comp.Settling = true;
            ent.Comp.SettlePosition = position;
            ent.Comp.SettleTime = _timing.CurTime;
            return true;
        }
        var elapsed = (_timing.CurTime - ent.Comp.SettleTime).TotalSeconds;
        var movement = Vector2.DistanceSquared(position, ent.Comp.SettlePosition);
        ent.Comp.SettlePosition = position;
        ent.Comp.SettleTime = _timing.CurTime;
        if (elapsed <= 0 || movement > Math.Pow(ent.Comp.RepairSpeedLimit * elapsed, 2))
            return true;
        ent.Comp.RepairReposition = null;
        ent.Comp.Settling = false;
        ent.Comp.ClosureCheck = null;
        foreach (var work in plan.Work)
            ent.Comp.FailedTargets.Remove(work.Target);
        return false;
    }
}

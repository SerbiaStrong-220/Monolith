using System.Numerics;
using Content.Shared._Exodus.ShipRepair;
using Content.Shared._Mono.ShipRepair.Components;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using PhysicsTransform = Robust.Shared.Physics.Transform;

namespace Content.Server._Exodus.ShipRepair;

public sealed partial class ShipRepairDroneSystem
{
    // Assigned once per update across active checks. An audit resumes rather than blocking the server tick.
    private int _closureQuota = 64;
    private int _closureBudget;

    private bool CheckRepairClosure(Entity<ShipRepairDroneComponent> ent, Entity<ShipRepairToolComponent> tool,
        Entity<ShipRepairDataComponent> grid, ShipRepairWorkQueueComponent queue, ShipRepairPlan plan, bool cancelOnFailure = true)
    {
        if (!_mapGridQuery.TryGetComponent(grid, out var mapGrid))
            return false;

        // Hull reconstruction is allowed to enclose the base or the repairer. The station's
        // emergency recall is the recovery path; only the final per-wall collision check remains.
        if (IsEnclosureOnlyPlan(plan))
            return ApproveEnclosureRepair(ent, grid, queue, plan);

        var position = _transform.ToCoordinates(grid.Owner, _transform.GetMapCoordinates(ent)).Position;
        var check = ent.Comp.ClosureCheck;
        if (check != null && _timing.CurTime >= ent.Comp.ClosureDeadline)
        {
            check.Blocked = true;
            if (cancelOnFailure)
                FailJob(ent);
            return false;
        }
        if (check == null || check.Revision != queue.NavigationRevision || check.WorkRevision != queue.WorkRevision ||
            !SameClosurePlan(check.Plan, plan) || Vector2.DistanceSquared(position, check.Origin) > 0.09f)
        {
            check = new ShipRepairClosureCheck
            {
                Plan = plan,
                Revision = queue.NavigationRevision,
                WorkRevision = queue.WorkRevision,
                Origin = position,
                SkipQueuedTargets = IsEnclosureOnlyPlan(plan),
            };
            CollectClosureShapes(ent, check);
            if (check.Shapes.Count == 0)
            {
                ent.Comp.ClosureCheck = null;
                return true;
            }
            if (ent.Comp.ClosureCheck == null)
                ent.Comp.ClosureDeadline = _timing.CurTime + ent.Comp.ExtendedSearchTimeout +
                    _repair.GetRepairDuration(plan, ent.Comp.RepairThroughput);
            ent.Comp.ClosureCheck = check;
            if (!OrderClosureWork(ent, grid, check))
            {
                check.Blocked = true;
                if (cancelOnFailure)
                    FailJob(ent);
                return false;
            }
            var bounds = new Box2(position, position);
            foreach (var shape in check.Shapes)
            {
                for (var child = 0; child < shape.Shape.ChildCount; child++)
                    bounds = bounds.Union(shape.Shape.ComputeAABB(new PhysicsTransform(shape.Work.Position, shape.Work.Rotation), child));
            }
            ResetClosureFlood(ent, grid, mapGrid, check, bounds.Enlarged(mapGrid.TileSize * 2));
        }
        if (!check.Blocked && !check.Approved)
            AdvanceClosureCheck(ent, tool, grid, mapGrid, queue, check);
        if (!check.Blocked && check.Approved && !ClosurePreservesDrones(ent, grid, mapGrid, queue, check))
            check.Blocked = true;
        if (check.Blocked)
        {
            // A different approach or completing the interior can make this wall safe later.
            if (cancelOnFailure)
                FailJob(ent);
            return false;
        }
        StopMoving(ent);
        ent.Comp.ProgressDeadline = _timing.CurTime + ent.Comp.StuckTimeout;
        return check.Approved;
    }

    private bool ApproveEnclosureRepair(Entity<ShipRepairDroneComponent> ent,
        Entity<ShipRepairDataComponent> grid, ShipRepairWorkQueueComponent queue, ShipRepairPlan plan)
    {
        var position = _transform.ToCoordinates(grid.Owner, _transform.GetMapCoordinates(ent)).Position;
        var check = ent.Comp.ClosureCheck;
        if (check == null || check.Revision != queue.NavigationRevision || check.WorkRevision != queue.WorkRevision ||
            !SameClosurePlan(check.Plan, plan) || Vector2.DistanceSquared(position, check.Origin) > 0.09f)
        {
            check = new ShipRepairClosureCheck
            {
                Plan = plan,
                Revision = queue.NavigationRevision,
                WorkRevision = queue.WorkRevision,
                Origin = position,
                SkipQueuedTargets = true,
                Approved = true,
            };
            CollectClosureShapes(ent, check);
            if (!OrderClosureWork(ent, grid, check))
            {
                check.Order.Clear();
                check.Order.AddRange(plan.Work);
            }
            ent.Comp.ClosureCheck = check;
        }
        else
        {
            check.Blocked = false;
            check.Approved = true;
        }

        StopMoving(ent);
        ent.Comp.ProgressDeadline = _timing.CurTime + ent.Comp.StuckTimeout;
        return true;
    }

    private static bool SameClosurePlan(ShipRepairPlan first, ShipRepairPlan second)
    {
        if (first.Grid != second.Grid || first.Revision != second.Revision || first.Work.Count != second.Work.Count)
            return false;
        for (var i = 0; i < first.Work.Count; i++)
        {
            if (first.Work[i].Target != second.Work[i].Target || first.Work[i].Operation != second.Work[i].Operation ||
                first.Work[i].Prototype != second.Work[i].Prototype)
                return false;
        }
        return true;
    }

    private void ResetClosureFlood(Entity<ShipRepairDroneComponent> ent, EntityUid grid, MapGridComponent mapGrid,
        ShipRepairClosureCheck check, Box2 bounds)
    {
        check.Bounds = bounds;
        check.Before.Clear();
        check.After.Clear();
        check.Frontier.Clear();
        check.Phase = 0;
        var origin = _map.LocalToTile(grid, mapGrid, new EntityCoordinates(grid, check.Origin));
        var bestDistance = float.PositiveInfinity;
        Vector2i? start = null;
        for (var y = -1; y <= 1; y++)
        for (var x = -1; x <= 1; x++)
        {
            var tile = origin + new Vector2i(x, y);
            var point = _map.TileCenterToVector(grid, mapGrid, tile);
            var distance = Vector2.DistanceSquared(check.Origin, point);
            if (distance >= bestDistance || !bounds.Contains(point) ||
                ClosureIntersects(check, check.Origin, point, GetNavigationShape(ent).Radius) ||
                !IsSegmentClear(ent, grid, check.Origin, point, true, out _, staticOnly: true))
                continue;
            bestDistance = distance;
            start = tile;
        }
        if (start is not { } root)
        {
            check.Blocked = true;
            return;
        }
        check.Start = root;
        check.Before.Add(root);
        check.Frontier.Enqueue(root);
    }

    private void AdvanceClosureCheck(Entity<ShipRepairDroneComponent> ent, Entity<ShipRepairToolComponent> tool,
        Entity<ShipRepairDataComponent> grid, MapGridComponent mapGrid, ShipRepairWorkQueueComponent queue,
        ShipRepairClosureCheck check)
    {
        var budget = _closureQuota;
        var cheapSteps = 0;
        while (budget > 0 && _closureBudget > 0 && !check.Blocked && !check.Approved && cheapSteps++ < 2048)
        {
            if (check.Phase < 2)
            {
                if (!check.Frontier.TryDequeue(out var current))
                {
                    if (check.Phase++ == 0)
                    {
                        check.After.Add(check.Start);
                        check.Frontier.Enqueue(check.Start);
                    }
                    else
                        check.Boundary = check.Before.GetEnumerator();
                    continue;
                }
                budget--;
                _closureBudget--;
                var reached = check.Phase == 0 ? check.Before : check.After;
                if (reached.Count >= ent.Comp.PathNodeLimit)
                {
                    check.Blocked = true;
                    continue;
                }
                var from = _map.TileCenterToVector(grid, mapGrid, current);
                foreach (var offset in Neighbours)
                {
                    var next = current + offset;
                    var to = _map.TileCenterToVector(grid, mapGrid, next);
                    if (reached.Contains(next) || !check.Bounds.Contains(to))
                        continue;
                    if (!check.Edges.TryGetValue((current, next), out var passable))
                    {
                        passable = IsSegmentClear(ent, grid, from, to, true, out _, staticOnly: true);
                        check.Edges[(current, next)] = passable;
                        check.Edges[(next, current)] = passable;
                    }
                    if (!passable || check.Phase == 1 && ClosureIntersects(check, from, to, GetNavigationShape(ent).Radius))
                        continue;
                    reached.Add(next);
                    check.Frontier.Enqueue(next);
                }
                continue;
            }
            if (check.Phase == 2)
            {
                if (!check.Expanded && check.Boundary.MoveNext())
                {
                    var tile = check.Boundary.Current;
                    if (check.After.Contains(tile))
                        continue;
                    var point = _map.TileCenterToVector(grid, mapGrid, tile);
                    var inner = check.Bounds.Enlarged(-mapGrid.TileSize);
                    if (inner.Contains(point))
                        continue;
                    // A local cut may have a longer route around it. Escalate only this ambiguous case.
                    check.Expanded = true;
                    ResetClosureFlood(ent, grid, mapGrid, check, queue.Bounds.Enlarged(ent.Comp.ExteriorMargin).Union(check.Bounds));
                    continue;
                }
                check.Phase = 3;
                var range = ent.Comp.RepairRange + ent.Comp.RepairRadius * 1.42f;
                check.ScanMin = _map.LocalToTile(grid, mapGrid, new EntityCoordinates(grid, check.Bounds.BottomLeft - new Vector2(range)));
                check.ScanMax = _map.LocalToTile(grid, mapGrid, new EntityCoordinates(grid, check.Bounds.TopRight + new Vector2(range)));
                check.Scan = check.ScanMin;
                check.EntryIndex = 0;
            }
            if (check.SkipQueuedTargets)
            {
                check.Approved = true;
                continue;
            }
            if (queue.EntriesByTile.TryGetValue(check.Scan, out var entries) && check.EntryIndex < entries.Count)
            {
                var target = entries[check.EntryIndex++];
                var included = false;
                foreach (var own in check.Plan.Work)
                    included |= own.Target == target;
                if (included || !_repair.NeedsSnapshotRepair(grid, target))
                    continue;
                budget--;
                _closureBudget--;
                if (!_repair.TryPlanRepair(tool, grid, target, true, out var work, checkMobileObstructions: false,
                        checkTileSupport: false, checkObstructions: false))
                    continue;
                var before = false;
                var after = false;
                var radius = (int) MathF.Ceiling((ent.Comp.RepairRange + ent.Comp.RepairRadius * 1.42f) / mapGrid.TileSize);
                for (var y = -radius; y <= radius && !after; y++)
                for (var x = -radius; x <= radius && !after; x++)
                {
                    var tile = target.Tile + new Vector2i(x, y);
                    if (!check.Before.Contains(tile))
                        continue;
                    var point = _map.TileCenterToVector(grid, mapGrid, tile);
                    if (!CanReachWork(ent, grid, work, point))
                        continue;
                    before = true;
                    after = check.After.Contains(tile) && CanReachWork(ent, grid, work, point, closure: check);
                }
                if (before && !after)
                    check.Blocked = true;
                continue;
            }
            check.EntryIndex = 0;
            if (check.Scan.X < check.ScanMax.X)
                check.Scan = new Vector2i(check.Scan.X + 1, check.Scan.Y);
            else if (check.Scan.Y < check.ScanMax.Y)
                check.Scan = new Vector2i(check.ScanMin.X, check.Scan.Y + 1);
            else
                check.Approved = true;
        }
    }

    private static bool IsEnclosureOnlyPlan(ShipRepairPlan plan)
    {
        if (plan.Work.Count == 0)
            return false;

        foreach (var work in plan.Work)
        {
            if (work.Operation != ShipRepairOperation.Restore || work.Stage != ShipRepairStage.Enclosure)
                return false;
        }

        return true;
    }

    private bool ClosurePreservesDrones(Entity<ShipRepairDroneComponent> ent, EntityUid grid, MapGridComponent mapGrid,
        ShipRepairWorkQueueComponent queue, ShipRepairClosureCheck check)
    {
        var map = Transform(grid).MapID;
        foreach (var uid in queue.Drones)
        {
            if (!_droneQuery.TryGetComponent(uid, out var drone) || !drone.Enabled || drone.CanPhase ||
                !_xformQuery.TryGetComponent(uid, out var transform) || transform.MapID != map || _containers.IsEntityInContainer(uid))
                continue;
            var position = _transform.ToCoordinates(grid, _transform.GetMapCoordinates(transform)).Position;
            if (ClosureLosesPosition(ent, grid, mapGrid, check, position))
                return false;
            // An enclosure-only repair is allowed to finish the hull around its own station.
            // The station is the intended interior base, so requiring an exterior route to it
            // would prevent rebuilding the hull around the very thing that owns the drones.
            if (!check.SkipQueuedTargets && drone.Station is { } station &&
                _xformQuery.TryGetComponent(station, out var home) && home.GridUid == grid &&
                ClosureLosesStation(grid, mapGrid, check, station))
                return false;
        }
        return true;
    }

    private bool ClosureLosesStation(EntityUid grid, MapGridComponent mapGrid, ShipRepairClosureCheck check, EntityUid station)
    {
        // The station can itself have a solid fixture. Protect a docking approach, not its occupied center.
        var position = _transform.ToCoordinates(grid, _transform.GetMapCoordinates(station)).Position;
        var center = _map.LocalToTile(grid, mapGrid, new EntityCoordinates(grid, position));
        var radius = (int) MathF.Ceiling(1.5f / mapGrid.TileSize);
        var before = false;
        for (var y = -radius; y <= radius; y++)
        for (var x = -radius; x <= radius; x++)
        {
            var tile = center + new Vector2i(x, y);
            if (!check.Before.Contains(tile))
                continue;
            var point = _map.TileCenterToVector(grid, mapGrid, tile);
            if (!_interaction.InRangeUnobstructed(_transform.ToMapCoordinates(new EntityCoordinates(grid, point)), station, 1.5f))
                continue;
            before = true;
            if (check.After.Contains(tile) && !ClosureIntersects(check, point, position, 0f, rayOnly: true))
                return false;
        }
        return before;
    }

    private bool ClosureLosesPosition(Entity<ShipRepairDroneComponent> ent, EntityUid grid, MapGridComponent mapGrid,
        ShipRepairClosureCheck check, Vector2 position)
    {
        var center = _map.LocalToTile(grid, mapGrid, new EntityCoordinates(grid, position));
        var before = false;
        for (var y = -1; y <= 1; y++)
        for (var x = -1; x <= 1; x++)
        {
            var tile = center + new Vector2i(x, y);
            if (!check.Before.Contains(tile))
                continue;
            var point = _map.TileCenterToVector(grid, mapGrid, tile);
            if (!IsSegmentClear(ent, grid, position, point, true, out _, staticOnly: true))
                continue;
            before = true;
            if (check.After.Contains(tile) && !ClosureIntersects(check, position, point, GetNavigationShape(ent).Radius))
                return false;
        }
        return before;
    }
}

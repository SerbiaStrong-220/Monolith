using System.Numerics;
using Content.Shared._Exodus.ShipRepair;
using Content.Shared._Mono.ShipRepair.Components;
using Robust.Shared.Map;

namespace Content.Server._Exodus.ShipRepair;

public sealed partial class ShipRepairDroneSystem
{
    private readonly HashSet<Vector2i> _batchCenters = new();
    private readonly List<(Vector2i Center, float Score)> _batchCandidates = new();

    private void AddBatchCenters(Entity<ShipRepairDroneComponent> ent, Entity<ShipRepairDataComponent> grid,
        ShipRepairWorkQueueComponent queue, ShipRepairStage stage, Vector2i target, Vector2 origin)
    {
        if (!_mapGridQuery.TryGetComponent(grid, out var mapGrid))
            return;
        var radius = ent.Comp.RepairRadius;
        var pending = queue.Stages[stage];
        for (var y = -radius; y <= radius; y++)
        for (var x = -radius; x <= radius; x++)
        {
            var center = target + new Vector2i(x, y);
            if (!_batchCenters.Add(center))
                continue;
            var count = 0;
            for (var dy = -radius; dy <= radius; dy++)
            for (var dx = -radius; dx <= radius; dx++)
                count += pending.TileCounts.GetValueOrDefault(center + new Vector2i(dx, dy));
            foreach (var reserved in pending.Reserved)
            {
                var delta = reserved.Tile - center;
                if (Math.Abs(delta.X) <= radius && Math.Abs(delta.Y) <= radius)
                    count--;
            }
            foreach (var (failed, retry) in ent.Comp.FailedTargets)
            {
                var delta = failed.Tile - center;
                if (_timing.CurTime < retry && pending.Indices.ContainsKey(failed) && !pending.Reserved.Contains(failed) &&
                    Math.Abs(delta.X) <= radius && Math.Abs(delta.Y) <= radius)
                    count--;
            }
            if (count <= 0)
                continue;
            var distance = Vector2.Distance(origin, _map.TileCenterToVector(grid, mapGrid, center));
            var score = count / (1f + distance * 0.15f);
            if (ent.Comp.FocusTile == center)
                score *= 1.05f;
            var index = 0;
            while (index < _batchCandidates.Count && _batchCandidates[index].Score >= score)
                index++;
            // Spatial planning is limited to the two best cheap estimates, not every damaged entity.
            if (index >= 2)
                continue;
            _batchCandidates.Insert(index, (center, score));
            if (_batchCandidates.Count > 2)
                _batchCandidates.RemoveAt(2);
        }
    }

    private bool TryAssignBatch(Entity<ShipRepairDroneComponent> ent, Entity<ShipRepairToolComponent> tool,
        Entity<ShipRepairDataComponent> grid, ShipRepairWorkQueueComponent queue, Vector2 origin, ShipRepairStage stage)
    {
        ShipRepairPlan? best = null;
        var bestScore = 0d;
        var bestPosition = Vector2.Zero;
        var bestCenter = Vector2i.Zero;
        foreach (var (center, _) in _batchCandidates)
        {
            if (!TryConsumeWorkSelectionBudget())
                return false;

            if (!TryPlanDroneWork(ent, tool, grid, queue, center, origin, stage, out var plan) ||
                !TryFindBatchPosition(ent, grid, queue, plan, center, origin, out var position, out var score) ||
                score <= bestScore)
                continue;
            best = plan;
            bestScore = score;
            bestCenter = center;
            bestPosition = position;
        }
        if (best == null)
            return false;
        for (var i = best.Work.Count - 1; i >= 0; i--)
        {
            if (!TryConsumeWorkSelectionBudget())
                return false;

            if (!IsCurrentRepairWork(tool, grid, best.Work[i]) ||
                !CanReachWork(ent, grid, best.Work[i], bestPosition))
                best.Work.RemoveAt(i);
        }
        _repair.PrepareConnectedRepairPlan(grid, best);
        for (var i = best.Work.Count - 1; i >= 0; i--)
        {
            if (!IsCurrentRepairWork(tool, grid, best.Work[i]) ||
                !CanReachWork(ent, grid, best.Work[i], bestPosition))
                best.Work.RemoveAt(i);
        }
        if (best.Work.Count == 0)
            return false;
        AssignDroneWork(ent, grid, queue, bestCenter, best, bestPosition);
        return true;
    }

    private bool TryFindBatchPosition(Entity<ShipRepairDroneComponent> ent, Entity<ShipRepairDataComponent> grid,
        ShipRepairWorkQueueComponent queue, ShipRepairPlan plan, Vector2i center, Vector2 origin,
        out Vector2 position, out double score)
    {
        position = default;
        score = 0;
        if (!_mapGridQuery.TryGetComponent(grid, out var mapGrid))
            return false;
        var radius = ent.Comp.RepairRadius + 1;
        for (var y = -radius; y <= radius; y++)
        for (var x = -radius; x <= radius; x++)
        {
            var tile = center + new Vector2i(x, y);
            var point = _map.TileCenterToVector(grid, mapGrid, tile);
            if (!queue.Bounds.Enlarged(ent.Comp.ExteriorMargin).Contains(point) ||
                !IsWorkPositionAvailable(ent, queue, tile, point))
                continue;
            if (!TryConsumeWorkSelectionBudget())
                return false;
            if (!IsClear(ent, grid, point, allowClearables: true))
                continue;
            var useful = 0d;
            var occupied = false;
            foreach (var work in plan.Work)
            {
                occupied |= WorkOverlapsDrone(ent, work, point);
                if (!TryConsumeWorkSelectionBudget())
                    return false;
                if (CanReachWork(ent, grid, work, point))
                    useful += work.Duration.TotalSeconds;
            }
            if (occupied || useful <= 0)
                continue;
            // Estimated travel plus a small per-cycle overhead; actual repair duration stays unchanged.
            var candidate = useful / (useful / Math.Max(0.01f, ent.Comp.RepairThroughput) +
                Vector2.Distance(origin, point) / 2f + 0.5f);
            if (candidate <= score)
                continue;
            score = candidate;
            position = point;
        }
        return score > 0;
    }
}

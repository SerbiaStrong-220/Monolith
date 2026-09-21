using Content.Shared._Exodus.ShipRepair;
using Content.Shared._Mono.ShipRepair.Components;

namespace Content.Server._Exodus.ShipRepair;

public sealed partial class ShipRepairDroneSystem
{
    // Queue groups retain the shared planner's finer ordering inside each batch.
    private static readonly ShipRepairStage[] RepairStages =
        { ShipRepairStage.Floor, ShipRepairStage.Power, ShipRepairStage.Enclosure };

    private static ShipRepairStage GetRepairGroup(ShipRepairStage stage)
    {
        return stage <= ShipRepairStage.Underfloor ? ShipRepairStage.Floor :
            stage < ShipRepairStage.Enclosure ? ShipRepairStage.Power : ShipRepairStage.Enclosure;
    }

    private void IndexWorkTarget(Entity<ShipRepairDataComponent> grid, ShipRepairWorkQueueComponent queue,
        ShipRepairTarget target, ShipRepairStage stage)
    {
        stage = GetRepairGroup(stage);
        queue.Entries.Add(target);
        if (!queue.EntriesByTile.TryGetValue(target.Tile, out var entries))
            queue.EntriesByTile.Add(target.Tile, entries = new List<ShipRepairTarget>());
        entries.Add(target);
        queue.TargetStages.Add(target, stage);
        if (!queue.Stages.ContainsKey(stage))
            queue.Stages.Add(stage, new ShipRepairStageQueue());
        // Discover damage during indexing, before allowing any drone to start a later stage.
        RefreshQueuedWork(grid, queue, target);
    }

    private void RefreshQueuedWork(Entity<ShipRepairDataComponent> grid, ShipRepairWorkQueueComponent queue,
        ShipRepairTarget target)
    {
        if (queue.Revision != grid.Comp.Revision || !queue.TargetStages.ContainsKey(target))
            return;
        if (_repair.NeedsSnapshotRepair(grid, target))
            EnqueueWork(queue, target);
        else
            RemoveQueuedWork(queue, target);
    }

    private static void RemoveQueuedWork(ShipRepairWorkQueueComponent queue, ShipRepairTarget target)
    {
        if (!queue.TargetStages.TryGetValue(target, out var stage) ||
            !queue.Stages[stage].Indices.Remove(target, out var index))
            return;
        var pending = queue.Stages[stage];
        var last = pending.Targets[^1];
        pending.Targets[index] = last;
        pending.Targets.RemoveAt(pending.Targets.Count - 1);
        if (index < pending.Targets.Count)
            pending.Indices[last] = index;
        pending.Reserved.Remove(target);
        pending.Revision++;
        ChangeWorkTileCount(pending, target.Tile, -1);
    }

    private static bool ReserveWork(ShipRepairWorkQueueComponent queue, ShipRepairTarget target, EntityUid drone)
    {
        if (!queue.Reservations.TryAdd(target, drone))
            return false;
        EnqueueWork(queue, target);
        return true;
    }

    private static ShipRepairStageProbe GetStageProbe(ShipRepairDroneComponent drone, ShipRepairWorkQueueComponent queue,
        ShipRepairStage stage)
    {
        if (!drone.StageProbes.TryGetValue(stage, out var probe))
        {
            probe = new ShipRepairStageProbe();
            drone.StageProbes.Add(stage, probe);
        }
        var pending = queue.Stages[stage];
        if (probe.Revision != pending.Revision || probe.SnapshotRevision != queue.Revision ||
            probe.RetryRevision != queue.StageRetryRevision && probe.Remaining == 0)
        {
            probe.Revision = pending.Revision;
            probe.SnapshotRevision = queue.Revision;
            probe.RetryRevision = queue.StageRetryRevision;
            probe.Remaining = pending.Targets.Count;
        }
        // Periodic retries must not restart an unfinished large scan every 30 seconds.
        probe.RetryRevision = queue.StageRetryRevision;
        return probe;
    }

    /// <summary>
    /// Each idle drone chooses its earliest free group. Another drone's reservation is not a barrier.
    /// </summary>
    private ShipRepairStage? GetCurrentRepairStage(Entity<ShipRepairDroneComponent> drone, ShipRepairWorkQueueComponent queue)
    {
        if (!queue.Indexed)
            return null;
        foreach (var stage in RepairStages)
        {
            if (!queue.Stages.TryGetValue(stage, out var pending) || pending.Targets.Count <= pending.Reserved.Count)
                continue;
            if (GetStageProbe(drone.Comp, queue, stage).Remaining > 0)
                return stage;
        }
        return null;
    }

    private bool ShouldYieldRepairStage(Entity<ShipRepairDroneComponent> drone,
        Entity<ShipRepairWorkQueueComponent> queue)
    {
        return !drone.Comp.Yielding && drone.Comp.AssignmentWorkRevision != queue.Comp.WorkRevision &&
               drone.Comp.Plan is { Work.Count: > 0 } plan &&
               GetCurrentRepairStage(drone, queue.Comp) is { } stage && GetRepairGroup(plan.Work[0].Stage) > stage &&
               queue.Comp.Stages[stage].DiscoveryRevision > drone.Comp.AssignmentWorkRevision;
    }

    private static void ChangeWorkTileCount(ShipRepairStageQueue stage, Vector2i tile, int change)
    {
        var count = stage.TileCounts.GetValueOrDefault(tile) + change;
        if (count <= 0)
            stage.TileCounts.Remove(tile);
        else
            stage.TileCounts[tile] = count;
    }
}

using Content.Shared._Exodus.ShipRepair;
using Content.Shared._Mono.ShipRepair.Components;

namespace Content.Server._Exodus.ShipRepair;

public sealed partial class ShipRepairDroneSystem
{
    private void FinishRepair(Entity<ShipRepairDroneComponent> ent, Entity<ShipRepairToolComponent> tool,
        Entity<ShipRepairDataComponent> grid, ShipRepairWorkQueueComponent queue, ShipRepairPlan plan)
    {
        PruneFinishedWork(ent, grid, queue, plan);
        if (ent.Comp.Plan == null)
            return;
        if (_timing.CurTime >= ent.Comp.RepairPublishDeadline)
        {
            FailJob(ent);
            return;
        }
        if (UpdateRepairReposition(ent, grid, queue, plan))
            return;

        // Keep the paid batch intact. Every entry is still rechecked against the live snapshot
        // before it is spawned, while a failed wall does not cancel unrelated walls in the batch.
        var publication = ent.Comp.PublicationPlan ??= new ShipRepairPlan { Grid = plan.Grid, Revision = plan.Revision };
        if (publication.Work.Count == 0)
        {
            foreach (var work in plan.Work)
            {
                if (ent.Comp.FailedTargets.TryGetValue(work.Target, out var retry) && _timing.CurTime < retry ||
                    !CanPublishRepair(ent, tool, grid, queue, work))
                    continue;
                publication.Work.Add(work);
                if (ent.Comp.PublishIndividually)
                    break;
            }
        }
        if (publication.Work.Count == 0)
        {
            AskObstructingDronesToYield(ent, grid, queue, plan);
            TryRepositionRepair(ent, grid, queue, plan);
            return;
        }
        if (!CheckRepairClosure(ent, tool, grid, queue, publication, cancelOnFailure: false))
        {
            if (ent.Comp.ClosureCheck is not { Blocked: true })
                return;
            // A combined closure may be unsafe even when some of its members are useful and safe.
            // Check those individually, still protecting every other outstanding snapshot entry.
            if (publication.Work.Count == 1)
                ent.Comp.FailedTargets[publication.Work[0].Target] = _timing.CurTime + ent.Comp.RetryInterval;
            ent.Comp.PublishIndividually = true;
            ent.Comp.PublicationPlan = null;
            ent.Comp.ClosureCheck = null;
            TryRepositionRepair(ent, grid, queue, plan);
            return;
        }

        var completed = 0;
        var changesPassage = ent.Comp.ClosureCheck is { Shapes.Count: > 0 };
        var abortOnPassageFailure = changesPassage && !IsEnclosureOnlyPlan(publication);
        foreach (var work in ent.Comp.ClosureCheck?.Order ?? publication.Work)
        {
            if (!CanPublishRepair(ent, tool, grid, queue, work) ||
                !_repair.TryCompleteRepair(tool, ent, publication, work, clearObstructions: true))
            {
                // Re-audit after any failure: later walls must not hide a skipped predecessor.
                if (abortOnPassageFailure)
                    break;
                continue;
            }
            completed++;
            ent.Comp.FailedTargets.Remove(work.Target);
            ReleaseWorkReservation(ent, queue, work.Target);
            RefreshQueuedWork(grid, queue, work.Target);
            plan.Work.Remove(work);
            if (work.Operation == ShipRepairOperation.Tile)
            {
                foreach (var pending in queue.Stages.Values)
                    pending.Revision++;
            }
        }
        ent.Comp.PublicationPlan = null;
        ent.Comp.ClosureCheck = null;
        if (completed > 0 && changesPassage)
            InvalidateNavigation(grid);
        if (completed > 0)
        {
            foreach (var remaining in plan.Work)
                ent.Comp.FailedTargets.Remove(remaining.Target);
        }
        if (plan.Work.Count == 0)
            CompleteRepairJob(ent);
        else
        {
            ent.Comp.Target = plan.Work[0].Target;
            if (completed == 0)
                TryRepositionRepair(ent, grid, queue, plan);
        }
    }

    private bool CanPublishRepair(Entity<ShipRepairDroneComponent> ent, Entity<ShipRepairToolComponent> tool,
        Entity<ShipRepairDataComponent> grid, ShipRepairWorkQueueComponent queue, ShipRepairWork work)
    {
        return queue.Reservations.TryGetValue(work.Target, out var owner) && owner == ent.Owner &&
            CanReachWork(ent, grid, work, allowClearables: false) && AreClearablesPrepared(ent, queue, work) &&
            _repair.TryPlanRepair(tool, grid, work.Target, work.Operation == ShipRepairOperation.Heal,
                out var current, checkTileSupport: false, allowClearables: true) &&
            current.Operation == work.Operation && current.Original == work.Original &&
            (current.Clearables == null || work.Clearables != null && current.Clearables.IsSubsetOf(work.Clearables));
    }
}

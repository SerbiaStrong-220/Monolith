using Content.Shared._Exodus.ShipRepair;
using Content.Shared._Mono.ShipRepair.Components;
using Content.Shared.DoAfter;

namespace Content.Server._Exodus.ShipRepair;

public sealed partial class ShipRepairDroneSystem
{
    private void StartRepairTimer(Entity<ShipRepairDroneComponent> ent, Entity<ShipRepairToolComponent> tool,
        Entity<ShipRepairDataComponent> grid, ShipRepairWorkQueueComponent queue, ShipRepairPlan plan)
    {
        var duration = _repair.GetRepairDuration(plan, ent.Comp.RepairThroughput) - ent.Comp.RepairElapsed;
        if (duration <= TimeSpan.Zero)
        {
            SetRepairReady(ent);
            FinishRepair(ent, tool, grid, queue, plan);
            return;
        }
        var args = new DoAfterArgs(EntityManager, ent, duration, new ShipRepairDroneDoAfterEvent(), ent)
        {
            NeedHand = false,
            BreakOnMove = true,
            BreakOnDamage = true,
            MovementThreshold = 0.5f,
            CancelDuplicate = false,
            // The quoted SRD time already includes the tool multiplier and drone throughput.
            MultiplyDelay = false,
        };
        ent.Comp.RepairStartedAt = _timing.CurTime;
        ent.Comp.RepairTimerDuration = duration;
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

    private void UpdateRepairTimer(Entity<ShipRepairDroneComponent> ent, Entity<ShipRepairToolComponent> tool,
        Entity<ShipRepairDataComponent> grid, ShipRepairWorkQueueComponent queue)
    {
        if (ent.Comp.Plan is not { } plan || ent.Comp.RepairDoAfter is not { } timer ||
            !PruneFinishedWork(ent, grid, queue, plan) || ent.Comp.Plan == null)
            return;
        var elapsed = _timing.CurTime - ent.Comp.RepairStartedAt;
        ent.Comp.RepairElapsed += elapsed < ent.Comp.RepairTimerDuration ? elapsed : ent.Comp.RepairTimerDuration;
        // Cancellation can synchronously raise completion; the old timer must no longer own this job.
        ent.Comp.RepairDoAfter = null;
        _doAfter.Cancel(timer);
        StartRepairTimer(ent, tool, grid, queue, plan);
    }

    private void SetRepairReady(Entity<ShipRepairDroneComponent> ent)
    {
        ent.Comp.RepairReady = true;
        ent.Comp.RepairPublishDeadline = _timing.CurTime + ent.Comp.RetryInterval + ent.Comp.ExtendedSearchTimeout;
        ent.Comp.PublicationPlan = null;
        // Reuse an audit performed during the timer when the ready subset is unchanged.
        if (ent.Comp.ClosureCheck is { Blocked: true })
            ent.Comp.ClosureCheck = null;
        ClearConstructionEffects(ent);
        SetVisual(ent);
    }

    /// <summary>Drop satisfied or superseded quotes; new work requires a new paid cycle.</summary>
    private bool PruneFinishedWork(Entity<ShipRepairDroneComponent> ent, Entity<ShipRepairDataComponent> grid,
        ShipRepairWorkQueueComponent queue, ShipRepairPlan plan)
    {
        var changed = false;
        for (var i = plan.Work.Count - 1; i >= 0; i--)
        {
            var work = plan.Work[i];
            if (queue.Reservations.TryGetValue(work.Target, out var owner) && owner == ent.Owner &&
                _repair.IsQuotedRepairPending(grid, work))
                continue;
            ReleaseWorkReservation(ent, queue, work.Target);
            RefreshQueuedWork(grid, queue, work.Target);
            plan.Work.RemoveAt(i);
            changed = true;
        }
        if (!changed)
            return false;
        ent.Comp.PublicationPlan = null;
        ent.Comp.ClosureCheck = null;
        if (plan.Work.Count == 0)
            CompleteRepairJob(ent);
        else
            ent.Comp.Target = plan.Work[0].Target;
        return true;
    }

    private void CompleteRepairJob(Entity<ShipRepairDroneComponent> ent)
    {
        var focus = ent.Comp.FocusTile;
        CancelJob(ent);
        ent.Comp.FocusTile = focus;
        ent.Comp.NextSearch = _timing.CurTime;
    }
}

using System.Numerics;
using Content.Shared._Exodus.ShipRepair;
using Content.Shared._Mono.ShipRepair.Components;
using Content.Shared.DoAfter;
using Content.Shared.Physics;
using Content.Shared.RCD.Components;
using Robust.Shared.Physics;

namespace Content.Server._Exodus.ShipRepair;

public sealed partial class ShipRepairDroneSystem
{
    private EntityQuery<ShipRepairClearableComponent> _clearableQuery;
    private EntityQuery<RCDDeconstructableComponent> _deconstructableQuery;
    private readonly HashSet<EntityUid> _neededClearables = new();

    private void InitializeClearing()
    {
        _clearableQuery = GetEntityQuery<ShipRepairClearableComponent>();
        _deconstructableQuery = GetEntityQuery<RCDDeconstructableComponent>();
        SubscribeLocalEvent<ShipRepairDroneComponent, ShipRepairDroneClearDoAfterEvent>(OnClearFinished);
    }

    private bool CanDroneClear(Entity<ShipRepairDroneComponent> ent, EntityUid grid, EntityUid obstacle)
    {
        return _clearableQuery.HasComponent(obstacle) && _repair.CanRepairGrid(ent, grid) &&
               _repair.CanClearRepairObstruction(grid, obstacle);
    }

    private bool CanReachClearable(Entity<ShipRepairDroneComponent> ent, EntityUid grid, EntityUid target)
    {
        if (!CanDroneClear(ent, grid, target))
            return false;
        var origin = _transform.GetMapCoordinates(ent);
        var destination = _transform.GetMapCoordinates(target);
        var delta = destination.Position - origin.Position;
        var range = ent.Comp.RepairRange + ent.Comp.RepairRadius * 1.42f;
        if (origin.MapId != destination.MapId || delta.LengthSquared() > range * range)
            return false;
        if (delta.LengthSquared() < 0.0001f)
            return false;
        var ray = new CollisionRay(origin.Position, Vector2.Normalize(delta),
            (int) (CollisionGroup.Impassable | CollisionGroup.InteractImpassable));
        foreach (var hit in _physics.IntersectRay(origin.MapId, ray, delta.Length(), ent, returnOnFirstHit: false))
        {
            if (hit.HitEntity == target)
                continue;
            // Overlapping temporary patches are dismantled individually, rather than blocking each other.
            if (CanDroneClear(ent, grid, hit.HitEntity) &&
                Vector2.DistanceSquared(_transform.GetWorldPosition(hit.HitEntity), destination.Position) < 0.01f)
                continue;
            return false;
        }
        return true;
    }

    private bool IsClearanceReservedByOther(Entity<ShipRepairDroneComponent> ent,
        ShipRepairWorkQueueComponent queue, EntityUid target)
    {
        if (!queue.ClearableReservations.TryGetValue(target, out var owner) || owner == ent.Owner)
            return false;
        if (!TerminatingOrDeleted(owner) && _droneQuery.TryGetComponent(owner, out var drone) &&
            drone.Enabled && drone.ClearableReservations.Contains(target))
            return true;
        queue.ClearableReservations.Remove(target);
        return false;
    }

    /// <summary>Returns true when clearance owns the job, including waiting for another drone.</summary>
    private bool PrepareWorkClearance(Entity<ShipRepairDroneComponent> ent, Entity<ShipRepairDataComponent> grid,
        ShipRepairWorkQueueComponent queue, ShipRepairPlan plan)
    {
        _neededClearables.Clear();
        foreach (var work in plan.Work)
        {
            if (work.Clearables != null)
                _neededClearables.UnionWith(work.Clearables);
            CanReachWork(ent, grid, work, clearables: _neededClearables);
        }
        // Reserve the whole batch together, so two drones cannot each hold half of the same set.
        foreach (var uid in _neededClearables)
        {
            if (IsClearanceReservedByOther(ent, queue, uid))
                return true;
        }
        foreach (var uid in _neededClearables)
        {
            queue.ClearableReservations[uid] = ent;
            ent.Comp.ClearableReservations.Add(uid);
        }
        var pending = false;
        EntityUid? next = null;
        var nearest = float.PositiveInfinity;
        var position = _transform.GetWorldPosition(ent);
        foreach (var uid in _neededClearables)
        {
            if (ent.Comp.PreparedClearables.Contains(uid))
                continue;
            pending = true;
            if (!CanReachClearable(ent, grid, uid))
                continue;
            // Keep the selected obstruction while braking, even if replanning changes set order.
            if (ent.Comp.ClearTarget == uid)
            {
                next = uid;
                break;
            }
            var distance = Vector2.DistanceSquared(position, _transform.GetWorldPosition(uid));
            if (distance < nearest)
            {
                nearest = distance;
                next = uid;
            }
        }
        if (next is { } target)
        {
            var replacement = false;
            foreach (var work in plan.Work)
                replacement |= work.Clearables?.Contains(target) == true;
            if (TryStartClearance(ent, grid, queue, target, replacement))
                return true;
        }
        if (!pending)
            return false;

        // An intact prepared patch can hide another patch in a fleet batch. Finish the accessible
        // replacements first instead of repeatedly preparing and abandoning the entire batch.
        for (var i = plan.Work.Count - 1; i >= 0; i--)
        {
            var work = plan.Work[i];
            if (AreClearablesPrepared(ent, queue, work) && CanReachWork(ent, grid, work, allowClearables: false))
                continue;
            if (queue.Reservations.TryGetValue(work.Target, out var owner) && owner == ent.Owner)
            {
                ReleaseWorkReservation(ent, queue, work.Target);
                EnqueueWork(queue, work.Target);
            }
            plan.Work.RemoveAt(i);
        }
        if (plan.Work.Count > 0)
            return false;
        FailJob(ent);
        return true;
    }

    private bool TryStartClearance(Entity<ShipRepairDroneComponent> ent, EntityUid grid,
        ShipRepairWorkQueueComponent queue, EntityUid target, bool forReplacement)
    {
        if (!CanReachClearable(ent, grid, target) || !TryLeavePhase(ent, eject: false) ||
            !_deconstructableQuery.TryGetComponent(target, out var dismantle) || !float.IsFinite(dismantle.Delay))
            return false;
        StopMoving(ent);
        if (IsClearanceReservedByOther(ent, queue, target))
        {
            ent.Comp.ProgressDeadline = _timing.CurTime + ent.Comp.StuckTimeout;
            return true;
        }
        queue.ClearableReservations[target] = ent;
        ent.Comp.ClearableReservations.Add(target);
        // Stop before starting the DoAfter. Residual flight velocity must not immediately cancel it.
        var position = _transform.ToCoordinates(grid, _transform.GetMapCoordinates(ent)).Position;
        if (ent.Comp.ClearTarget != target)
        {
            ent.Comp.ClearTarget = target;
            ent.Comp.ClearSettlePosition = position;
            ent.Comp.ClearSettleTime = _timing.CurTime;
            ent.Comp.ProgressDeadline = _timing.CurTime + ent.Comp.StuckTimeout;
            return true;
        }
        var elapsed = (_timing.CurTime - ent.Comp.ClearSettleTime).TotalSeconds;
        var movement = Vector2.DistanceSquared(position, ent.Comp.ClearSettlePosition);
        ent.Comp.ClearSettlePosition = position;
        ent.Comp.ClearSettleTime = _timing.CurTime;
        if (elapsed <= 0 || movement > Math.Pow(ent.Comp.RepairSpeedLimit * elapsed, 2))
            return true;
        var duration = TimeSpan.FromSeconds(Math.Max(0.1f, dismantle.Delay));
        var args = new DoAfterArgs(EntityManager, ent, duration,
            new ShipRepairDroneClearDoAfterEvent(), ent)
        {
            NeedHand = false,
            BreakOnMove = true,
            BreakOnDamage = true,
            MovementThreshold = 0.3f,
            CancelDuplicate = false,
            // Keep the target in ClearTarget. DoAfter always applies its generic unobstructed-target
            // check when Target is set, even with RequireCanInteract = false. That rejects overlapping
            // patches which CanReachClearable deliberately permits. Recheck our own reach on completion.
            RequireCanInteract = false,
        };
        if (!_doAfter.TryStartDoAfter(args, out var id))
            return false;
        ent.Comp.ClearTarget = target;
        ent.Comp.ClearForReplacement = forReplacement;
        ent.Comp.ClearDoAfter = id;
        if (dismantle.Effect is { } effect)
            Spawn(effect, Transform(target).Coordinates);
        SetVisual(ent);
        return true;
    }

    private void OnClearFinished(Entity<ShipRepairDroneComponent> ent, ref ShipRepairDroneClearDoAfterEvent args)
    {
        if (ent.Comp.ClearDoAfter != args.DoAfter.Id)
            return;
        var target = ent.Comp.ClearTarget;
        var replacement = ent.Comp.ClearForReplacement;
        ent.Comp.ClearDoAfter = null;
        ent.Comp.ClearTarget = null;
        ent.Comp.ClearForReplacement = false;
        if (args.Cancelled || args.Handled || !ent.Comp.Enabled || IsDisabledBody(ent) || ent.Comp.Phased ||
            ent.Comp.Command == ShipRepairDroneCommand.Idle || !TryGetStation(ent, out var station) ||
            !IsStationActive(station) || ent.Comp.Grid is not { } grid || Transform(station).GridUid != grid ||
            !_snapshotQuery.TryGetComponent(grid, out var data) || !_queueQuery.TryGetComponent(grid, out var queue) ||
            !CanServiceShip(ent, Transform(ent), grid, queue) || _containers.IsEntityInContainer(ent) ||
            target is not { } uid || !CanReachClearable(ent, grid, uid) ||
            !queue.ClearableReservations.TryGetValue(uid, out var owner) || owner != ent.Owner ||
            ent.Comp.Plan is { } plan && plan.Revision != data.Revision)
        {
            FailJob(ent);
            return;
        }
        if (replacement)
        {
            // Actual removal is coupled to successful reconstruction in the shared repair system.
            ent.Comp.PreparedClearables.Add(uid);
        }
        else
        {
            QueueDel(uid);
            queue.ClearableReservations.Remove(uid);
            ent.Comp.ClearableReservations.Remove(uid);
        }
        args.Handled = true;
        ent.Comp.ProgressDeadline = _timing.CurTime + ent.Comp.StuckTimeout;
        ent.Comp.NextUpdate = _timing.CurTime + ent.Comp.UpdateInterval;
        SetVisual(ent);
    }

    private bool AreClearablesPrepared(Entity<ShipRepairDroneComponent> ent, ShipRepairWorkQueueComponent queue,
        ShipRepairWork work)
    {
        if (work.Clearables == null)
            return true;
        foreach (var uid in work.Clearables)
        {
            if (TerminatingOrDeleted(uid) || EntityManager.IsQueuedForDeletion(uid))
                continue;
            if (!ent.Comp.PreparedClearables.Contains(uid) ||
                !queue.ClearableReservations.TryGetValue(uid, out var owner) || owner != ent.Owner)
                return false;
        }
        return true;
    }

    private void CancelClearance(Entity<ShipRepairDroneComponent> ent)
    {
        var id = ent.Comp.ClearDoAfter;
        ent.Comp.ClearDoAfter = null;
        ent.Comp.ClearTarget = null;
        ent.Comp.ClearForReplacement = false;
        if (_doAfter.IsRunning(id))
            _doAfter.Cancel(id);
        if (ent.Comp.Grid is { } grid && _queueQuery.TryGetComponent(grid, out var queue))
        {
            foreach (var uid in ent.Comp.ClearableReservations)
            {
                if (queue.ClearableReservations.TryGetValue(uid, out var owner) && owner == ent.Owner)
                    queue.ClearableReservations.Remove(uid);
            }
        }
        ent.Comp.ClearableReservations.Clear();
        ent.Comp.PreparedClearables.Clear();
        ent.Comp.ClearingRoute = false;
    }
}

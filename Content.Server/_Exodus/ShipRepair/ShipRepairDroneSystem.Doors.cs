using Content.Shared.Doors.Components;
using Content.Shared.Prying.Components;
using Content.Shared.DoAfter;
using Robust.Shared.Physics;
using PhysicsTransform = Robust.Shared.Physics.Transform;

namespace Content.Server._Exodus.ShipRepair;

public sealed partial class ShipRepairDroneSystem
{
    // Snapshot the passage before opening a door: state-change events can invalidate navigation.
    private readonly List<EntityUid> _passageObstacles = new();
    private EntityQuery<DoorComponent> _droneDoorQuery;
    private EntityQuery<FirelockComponent> _droneFirelockQuery;
    private EntityQuery<PryingComponent> _dronePryingQuery;

    private bool CanDroneOpenDoor(Entity<ShipRepairDroneComponent> drone, Entity<DoorComponent> door)
    {
        if (door.Comp.State is DoorState.Open or DoorState.Opening || _doors.CanOpen(door, door.Comp, drone))
            return true;
        return CanDronePryDoor(drone, door);
    }

    private bool CanDronePryDoor(Entity<ShipRepairDroneComponent> drone, Entity<DoorComponent> door)
    {
        if (!_dronePryingQuery.TryGetComponent(drone, out var prying) || !prying.Enabled)
            return false;
        var attempt = new BeforePryEvent(drone, prying.PryPowered, prying.Force, true);
        RaiseLocalEvent(door.Owner, ref attempt);
        return !attempt.Cancelled;
    }

    private bool OpenDronePassage(Entity<ShipRepairDroneComponent> drone, EntityUid grid, ShipRepairWorkQueueComponent queue)
    {
        // Open reachable airlocks normally. Pry firelocks even when access allows normal opening:
        // only PriedEvent grants the emergency-close cooldown needed to cross a decompressed passage.
        foreach (var uid in _passageObstacles)
        {
            if (TerminatingOrDeleted(uid) || !_droneDoorQuery.TryGetComponent(uid, out var door) ||
                door.State is DoorState.Open or DoorState.Opening ||
                _droneFirelockQuery.HasComponent(uid) && CanDronePryDoor(drone, (uid, door)) ||
                !_interaction.InRangeUnobstructed(drone.Owner, uid) || !_doors.TryOpen(uid, door, drone, quiet: true))
                continue;
            drone.Comp.ProgressDeadline = _timing.CurTime + drone.Comp.StuckTimeout;
            return true;
        }

        foreach (var uid in _passageObstacles)
        {
            if (TerminatingOrDeleted(uid))
                continue;
            if (CanDroneClear(drone, grid, uid))
            {
                if (TryStartClearance(drone, grid, queue, uid, forReplacement: false))
                    return true;
                continue;
            }
            if (!_droneDoorQuery.TryGetComponent(uid, out var door) ||
                door.State is DoorState.Open or DoorState.Opening ||
                !_interaction.InRangeUnobstructed(drone.Owner, uid))
                continue;

            EnsureComp<ShipRepairDronePryTargetComponent>(uid);
            _prying.TryPry(uid, drone, out var id, drone);
            drone.Comp.PryDoAfter = id;
            drone.Comp.PryTarget = id != null ? uid : null;
            // Instant prying completes synchronously and has no DoAfter ID.
            if (id != null || door.State is DoorState.Open or DoorState.Opening)
            {
                drone.Comp.ProgressDeadline = _timing.CurTime + drone.Comp.StuckTimeout;
                return true;
            }
        }
        // A rejected DoAfter may mean overlap, motion or temporary obstruction, not an impassable door.
        // Wait for an opening/next attempt; the progress watchdog still bounds this wait.
        return false;
    }

    /// <summary>Door interaction owns movement before clearance recovery, even without a repair assignment.</summary>
    private bool UpdateNearbyDoors(Entity<ShipRepairDroneComponent> ent, EntityUid grid, ShipRepairWorkQueueComponent queue)
    {
        if (ent.Comp.PryDoAfter is { } pry)
        {
            var running = _doAfter.GetStatus(pry) == DoAfterStatus.Running;
            if (running && ent.Comp.PryTarget is { } target && !TerminatingOrDeleted(target) &&
                _droneDoorQuery.TryGetComponent(target, out var door) &&
                door.State is not (DoorState.Open or DoorState.Opening))
            {
                StopMoving(ent);
                return true;
            }
            ent.Comp.PryDoAfter = null;
            ent.Comp.PryTarget = null;
            if (running)
                _doAfter.Cancel(pry);
            ent.Comp.ProgressDeadline = _timing.CurTime + ent.Comp.StuckTimeout;
            ent.Comp.DoorRecoveryDeadline = _timing.CurTime + ent.Comp.StuckTimeout;
        }
        if (ent.Comp.CanPhase || _timing.CurTime < ent.Comp.NextDoorRecovery)
            return false;
        var position = _transform.GetMapCoordinates(ent);
        _intersections.Clear();
        _lookup.GetEntitiesIntersecting(position.MapId, GetNavigationShape(ent),
            new PhysicsTransform(position.Position, Angle.Zero), _intersections, LookupFlags.Static | LookupFlags.Dynamic);
        _passageObstacles.Clear();
        var opening = false;
        foreach (var uid in _intersections)
        {
            if (!BlocksDrone(ent, uid) || !_droneDoorQuery.TryGetComponent(uid, out var door) ||
                !CanDroneOpenDoor(ent, (uid, door)))
                continue;
            _passageObstacles.Add(uid);
            opening |= door.State is DoorState.Open or DoorState.Opening;
        }
        if (_passageObstacles.Count == 0)
        {
            ent.Comp.RecoveringDoor = false;
            return false;
        }
        if (!ent.Comp.RecoveringDoor)
        {
            ent.Comp.RecoveringDoor = true;
            ent.Comp.DoorRecoveryDeadline = _timing.CurTime + ent.Comp.StuckTimeout;
        }
        if (_timing.CurTime >= ent.Comp.DoorRecoveryDeadline)
        {
            ent.Comp.RecoveringDoor = false;
            ent.Comp.NextDoorRecovery = _timing.CurTime + ent.Comp.RetryInterval;
            return false;
        }
        if (!opening && !OpenDronePassage(ent, grid, queue))
            return false;
        StopMoving(ent);
        ent.Comp.ProgressDeadline = _timing.CurTime + ent.Comp.StuckTimeout;
        return true;
    }
}

using Content.Server.NPC.Components;
using Content.Server.NPC.Systems;
using Content.Shared._Exodus.ShipRepair;
using Content.Shared._Mono.ShipRepair;
using Content.Shared._Mono.ShipRepair.Components;
using Content.Shared.DoAfter;
using Content.Shared.Doors.Systems;
using Content.Shared.Examine;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.NPC;
using Content.Shared.Popups;
using Content.Shared.Prying.Systems;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Containers;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Timing;

namespace Content.Server._Exodus.ShipRepair;

/// <summary>Station-controlled, bounded autonomous repair of an assigned ship snapshot.</summary>
public sealed partial class ShipRepairDroneSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SharedShipRepairSystem _repair = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private SharedPhysicsSystem _physics = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private NPCSteeringSystem _steering = default!;
    [Dependency] private SharedDoorSystem _doors = default!;
    [Dependency] private PryingSystem _prying = default!;
    [Dependency] private SharedDoAfterSystem _doAfter = default!;
    [Dependency] private SharedContainerSystem _containers = default!;
    [Dependency] private SharedAppearanceSystem _appearance = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedAudioSystem _audio = default!;

    private EntityQuery<ShipRepairWorkQueueComponent> _queueQuery;
    private EntityQuery<TransformComponent> _xformQuery;
    private EntityQuery<PhysicsComponent> _bodyQuery;
    private EntityQuery<FixturesComponent> _fixturesQuery;
    private EntityQuery<MobStateComponent> _mobQuery;
    private EntityQuery<ShipRepairDataComponent> _snapshotQuery;
    private EntityQuery<MapGridComponent> _mapGridQuery;
    private EntityQuery<ShipRepairDroneComponent> _droneQuery;
    private EntityQuery<NPCSteeringComponent> _steeringQuery;
    private EntityQuery<ShipRepairStationComponent> _stationQuery;

    public override void Initialize()
    {
        base.Initialize();
        UpdatesBefore.Add(typeof(NPCSteeringSystem));
        _queueQuery = GetEntityQuery<ShipRepairWorkQueueComponent>();
        _xformQuery = GetEntityQuery<TransformComponent>();
        _bodyQuery = GetEntityQuery<PhysicsComponent>();
        _fixturesQuery = GetEntityQuery<FixturesComponent>();
        _mobQuery = GetEntityQuery<MobStateComponent>();
        _snapshotQuery = GetEntityQuery<ShipRepairDataComponent>();
        _mapGridQuery = GetEntityQuery<MapGridComponent>();
        _droneQuery = GetEntityQuery<ShipRepairDroneComponent>();
        _steeringQuery = GetEntityQuery<NPCSteeringComponent>();
        _stationQuery = GetEntityQuery<ShipRepairStationComponent>();
        InitializeReachability();
        InitializeStations();
        InitializeClearing();

        SubscribeLocalEvent<ShipRepairDroneComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<ShipRepairDroneComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<ShipRepairDroneComponent, MobStateChangedEvent>(OnMobStateChanged);
        SubscribeLocalEvent<ShipRepairDroneComponent, ExaminedEvent>(OnExamined);
        SubscribeLocalEvent<ShipRepairDroneComponent, ShipRepairDroneDoAfterEvent>(OnRepairFinished);
    }

    private void OnMapInit(Entity<ShipRepairDroneComponent> ent, ref MapInitEvent args)
    {
        ent.Comp.NextUpdate = _timing.CurTime;
        SetVisual(ent);
    }

    private bool TryEnable(Entity<ShipRepairDroneComponent> ent)
    {
        if (ent.Comp.Enabled)
            return true;

        if (IsDisabledBody(ent) || ent.Comp.Phased || !TryGetStation(ent, out var station) || !IsStationActive(station) ||
            _containers.IsEntityInContainer(ent) && !IsDocked(ent, station))
            return false;
        if (Transform(station).GridUid is not { } grid || !_snapshotQuery.HasComponent(grid))
        {
            _popup.PopupEntity(Loc.GetString("ship-repair-drone-no-snapshot"), station);
            return false;
        }
        if (!_repair.CanRepairGrid(ent, grid))
        {
            _popup.PopupEntity(Loc.GetString("ship-repair-drone-incompatible"), station);
            return false;
        }
        ClearAssignment(ent);
        ent.Comp.Enabled = true;
        SetVisual(ent);
        return true;
    }

    private void Disable(Entity<ShipRepairDroneComponent> ent)
    {
        ent.Comp.Enabled = false;
        ClearAssignment(ent);
        if (!TerminatingOrDeleted(ent))
        {
            TryLeavePhase(ent, eject: true);
            SetVisual(ent);
        }
    }

    private void OnShutdown(Entity<ShipRepairDroneComponent> ent, ref ComponentShutdown args)
    {
        Disable(ent);
        UnregisterDrone(ent);
    }

    private void OnMobStateChanged(Entity<ShipRepairDroneComponent> ent, ref MobStateChangedEvent args)
    {
        if (args.NewMobState != MobState.Alive)
            Disable(ent);
        SetVisual(ent);
    }

    private bool IsDisabledBody(EntityUid uid)
    {
        return !_mobQuery.TryGetComponent(uid, out var mob) || mob.CurrentState != MobState.Alive;
    }

    private void OnExamined(Entity<ShipRepairDroneComponent> ent, ref ExaminedEvent args)
    {
        var key = IsDisabledBody(ent) ? "ship-repair-drone-status-destroyed" :
            !ent.Comp.Enabled ? "ship-repair-drone-status-off" :
            ent.Comp.WaitingForShip ? "ship-repair-drone-status-waiting" :
            ent.Comp.ClearDoAfter != null ? "ship-repair-station-status-clearing" :
            ent.Comp.RepairDoAfter != null ? "ship-repair-drone-status-repairing" : "ship-repair-drone-status-active";
        args.PushMarkup(Loc.GetString(key));
        if (!ent.Comp.Enabled && !IsDisabledBody(ent))
            args.PushMarkup(Loc.GetString("ship-repair-drone-station-hint"));
    }

    private void SetVisual(Entity<ShipRepairDroneComponent> ent)
    {
        var state = IsDisabledBody(ent) ? ShipRepairDroneState.Dead :
            ent.Comp.Phased ? ShipRepairDroneState.Phased :
            !ent.Comp.Enabled ? ShipRepairDroneState.Off :
            ent.Comp.RepairDoAfter != null || ent.Comp.ClearDoAfter != null ? ShipRepairDroneState.Repairing : ShipRepairDroneState.Idle;
        _appearance.SetData(ent, ShipRepairDroneVisuals.State, state);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        UpdateStations();
        UpdateQueues();

        // Split expensive path expansion across drones and ticks instead of a synchronous full-ship search.
        var searches = 0;
        var searchQuery = EntityQueryEnumerator<ShipRepairDroneComponent>();
        while (searchQuery.MoveNext(out _, out var searching))
        {
            if (searching.Enabled && searching.Search != null && _timing.CurTime >= searching.NextUpdate)
                searches++;
        }
        var pathQuota = Math.Max(1, 256 / Math.Max(1, searches));
        var query = EntityQueryEnumerator<ShipRepairDroneComponent, ShipRepairToolComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var drone, out var tool, out var xform))
        {
            var ent = new Entity<ShipRepairDroneComponent>(uid, drone);
            if (_timing.CurTime < drone.NextUpdate)
            {
                // Hand off a reached waypoint before steering brakes or turns back towards it.
                // Keep periodic work on its existing schedule; only this distance check runs every tick.
                if (!HasReachedWaypoint(ent, xform) && !HasReachedClearanceDestination(ent, xform))
                    continue;
            }
            else
            {
                drone.NextUpdate += drone.UpdateInterval;
                if (drone.NextUpdate < _timing.CurTime)
                    drone.NextUpdate = _timing.CurTime + drone.UpdateInterval;
            }

            if (!drone.Enabled)
            {
                if (drone.Phased)
                    TryLeavePhase(ent, eject: true);
                continue;
            }

            if (IsDisabledBody(uid) || !TryGetStation(ent, out var station) || !IsStationActive(station))
            {
                Disable(ent);
                continue;
            }
            if (IsDocked(ent, station))
                continue;
            if (_containers.IsEntityInContainer(uid))
            {
                Disable(ent);
                continue;
            }
            if (drone.Command == ShipRepairDroneCommand.Idle)
            {
                if (drone.Phased)
                    TryLeavePhase(ent, eject: true);
                continue;
            }
            if (drone.Command == ShipRepairDroneCommand.Return)
            {
                UpdateReturn(ent, (uid, tool), station, xform, pathQuota);
                continue;
            }
            if (drone.Grid is not { } grid || Transform(station).GridUid != grid || TerminatingOrDeleted(grid) ||
                !_snapshotQuery.TryGetComponent(grid, out var data) || !_repair.CanRepairGrid(uid, grid))
            {
                // Losing the repair snapshot stops work, but must not disable the return controls.
                ClearAssignment(ent);
                continue;
            }

            if (drone.Revision != data.Revision)
            {
                CancelJob(ent);
                drone.Revision = data.Revision;
                drone.FailedTargets.Clear();
                drone.FailedPositions.Clear();
            }

            if (!_queueQuery.TryGetComponent(grid, out var queue) || !queue.Indexed)
                continue;

            if (!CanServiceShip(ent, xform, grid, queue))
            {
                if (!drone.WaitingForShip)
                {
                    CancelJob(ent);
                    drone.WaitingForShip = true;
                }
                TryLeavePhase(ent, eject: true);
                continue;
            }

            drone.WaitingForShip = false;
            if (drone.RepairDoAfter != null || drone.ClearDoAfter != null)
                continue;

            if (drone.PryDoAfter == null && UpdateClearanceRecovery(ent, grid, queue, xform))
                continue;

            if (drone.Target == null && !drone.Yielding)
            {
                if (!TryLeavePhase(ent, eject: true))
                    continue;
                if (_timing.CurTime >= drone.NextSearch)
                {
                    drone.NextSearch = _timing.CurTime + drone.IdleInterval;
                    TryChooseJob(ent, (uid, tool), (grid, data), queue);
                }
                continue;
            }

            UpdateNavigation(ent, (uid, tool), grid, queue, pathQuota);
        }
    }

    private void StopMoving(Entity<ShipRepairDroneComponent> ent)
    {
        _steering.Unregister(ent);
        RemComp<ActiveNPCComponent>(ent);
    }
}

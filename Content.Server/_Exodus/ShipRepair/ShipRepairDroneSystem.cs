using Content.Server.DeviceLinking.Systems;
using Content.Server.NPC.Systems;
using Content.Shared._Exodus.ShipRepair;
using Content.Shared._Mono.ShipRepair;
using Content.Shared._Mono.ShipRepair.Components;
using Content.Shared.DeviceLinking;
using Content.Shared.DeviceLinking.Events;
using Content.Shared.DeviceNetwork;
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

/// <summary>Signal-controlled, bounded autonomous repair of an assigned ship snapshot.</summary>
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
    [Dependency] private DeviceLinkSystem _links = default!;
    [Dependency] private SharedContainerSystem _containers = default!;
    [Dependency] private SharedAppearanceSystem _appearance = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedAudioSystem _audio = default!;

    private EntityQuery<ShipRepairWorkQueueComponent> _queueQuery;
    private EntityQuery<TransformComponent> _xformQuery;
    private EntityQuery<PhysicsComponent> _bodyQuery;
    private EntityQuery<FixturesComponent> _fixturesQuery;
    private EntityQuery<MobStateComponent> _mobQuery;
    private EntityQuery<DeviceLinkSinkComponent> _sinkQuery;
    private EntityQuery<ShipRepairDataComponent> _snapshotQuery;
    private EntityQuery<MapGridComponent> _mapGridQuery;

    public override void Initialize()
    {
        base.Initialize();
        UpdatesBefore.Add(typeof(NPCSteeringSystem));
        _queueQuery = GetEntityQuery<ShipRepairWorkQueueComponent>();
        _xformQuery = GetEntityQuery<TransformComponent>();
        _bodyQuery = GetEntityQuery<PhysicsComponent>();
        _fixturesQuery = GetEntityQuery<FixturesComponent>();
        _mobQuery = GetEntityQuery<MobStateComponent>();
        _sinkQuery = GetEntityQuery<DeviceLinkSinkComponent>();
        _snapshotQuery = GetEntityQuery<ShipRepairDataComponent>();
        _mapGridQuery = GetEntityQuery<MapGridComponent>();

        SubscribeLocalEvent<ShipRepairDroneComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<ShipRepairDroneComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<ShipRepairDroneComponent, SignalReceivedEvent>(OnSignal);
        SubscribeLocalEvent<ShipRepairDroneComponent, MobStateChangedEvent>(OnMobStateChanged);
        SubscribeLocalEvent<ShipRepairDroneComponent, ExaminedEvent>(OnExamined);
        SubscribeLocalEvent<ShipRepairDroneComponent, ShipRepairDroneDoAfterEvent>(OnRepairFinished);
    }

    private void OnMapInit(Entity<ShipRepairDroneComponent> ent, ref MapInitEvent args)
    {
        _links.EnsureSinkPorts(ent, ent.Comp.OnPort, ent.Comp.OffPort, ent.Comp.TogglePort);
        ent.Comp.NextUpdate = _timing.CurTime;
        SetVisual(ent);
    }

    private void OnSignal(Entity<ShipRepairDroneComponent> ent, ref SignalReceivedEvent args)
    {
        if (args.Trigger is not { } source ||
            !TryComp<DeviceLinkSourceComponent>(source, out var links))
            return;

        var linked = false;
        foreach (var (_, sink) in _links.GetLinks(source, ent, links))
        {
            if (sink == args.Port)
            {
                linked = true;
                break;
            }
        }
        if (!linked)
            return;

        // Pulses and the rising edge of logic devices are commands; low is not another toggle.
        if (args.Data != null && args.Data.TryGetValue(DeviceNetworkConstants.LogicState, out SignalState state) &&
            state == SignalState.Low)
            return;

        if (args.Port == ent.Comp.OffPort || args.Port == ent.Comp.TogglePort && ent.Comp.Enabled)
            Disable(ent);
        else if (args.Port == ent.Comp.OnPort || args.Port == ent.Comp.TogglePort)
            TryEnable(ent);
    }

    private bool TryEnable(Entity<ShipRepairDroneComponent> ent)
    {
        if (ent.Comp.Enabled)
            return true;

        if (IsDisabledBody(ent) || _containers.IsEntityInContainer(ent) || ent.Comp.Phased)
            return false;

        if (Transform(ent).GridUid is not { } grid || !TryComp<ShipRepairDataComponent>(grid, out var data))
        {
            _popup.PopupEntity(Loc.GetString("ship-repair-drone-no-snapshot"), ent, PopupType.SmallCaution);
            return false;
        }

        if (!_repair.CanRepairGrid(ent, grid))
        {
            _popup.PopupEntity(Loc.GetString("ship-repair-drone-incompatible"), ent, PopupType.SmallCaution);
            return false;
        }

        ent.Comp.Enabled = true;
        ent.Comp.Grid = grid;
        ent.Comp.Revision = data.Revision;
        ent.Comp.WaitingForShip = false;
        ent.Comp.NextUpdate = _timing.CurTime;
        ent.Comp.NextSearch = _timing.CurTime;
        ent.Comp.FailedTargets.Clear();
        EnsureComp<ShipRepairWorkQueueComponent>(grid).Drones.Add(ent);
        SetVisual(ent);
        return true;
    }

    private void Disable(Entity<ShipRepairDroneComponent> ent)
    {
        ent.Comp.Enabled = false;
        CancelJob(ent);
        if (ent.Comp.Grid is { } grid && _queueQuery.TryGetComponent(grid, out var queue))
        {
            queue.Drones.Remove(ent);
        }
        ent.Comp.Grid = null;
        ent.Comp.WaitingForShip = false;
        ent.Comp.FailedTargets.Clear();
        if (!TerminatingOrDeleted(ent))
        {
            TryLeavePhase(ent, eject: true);
            SetVisual(ent);
        }
    }

    private void OnShutdown(Entity<ShipRepairDroneComponent> ent, ref ComponentShutdown args)
    {
        Disable(ent);
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

    private bool HasLinks(EntityUid uid)
    {
        return _sinkQuery.TryGetComponent(uid, out var sink) && sink.LinkedSources.Count > 0;
    }

    private void OnExamined(Entity<ShipRepairDroneComponent> ent, ref ExaminedEvent args)
    {
        var key = IsDisabledBody(ent) ? "ship-repair-drone-status-destroyed" :
            !ent.Comp.Enabled ? "ship-repair-drone-status-off" :
            ent.Comp.WaitingForShip ? "ship-repair-drone-status-waiting" :
            ent.Comp.RepairDoAfter != null ? "ship-repair-drone-status-repairing" : "ship-repair-drone-status-active";
        args.PushMarkup(Loc.GetString(key));
        if (!ent.Comp.Enabled && !IsDisabledBody(ent))
            args.PushMarkup(Loc.GetString("ship-repair-drone-link-hint"));
    }

    private void SetVisual(Entity<ShipRepairDroneComponent> ent)
    {
        var state = IsDisabledBody(ent) ? ShipRepairDroneState.Dead :
            ent.Comp.Phased ? ShipRepairDroneState.Phased :
            !ent.Comp.Enabled ? ShipRepairDroneState.Off :
            ent.Comp.RepairDoAfter != null ? ShipRepairDroneState.Repairing : ShipRepairDroneState.Idle;
        _appearance.SetData(ent, ShipRepairDroneVisuals.State, state);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
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
            if (_timing.CurTime < drone.NextUpdate)
                continue;
            drone.NextUpdate += drone.UpdateInterval;
            if (drone.NextUpdate < _timing.CurTime)
                drone.NextUpdate = _timing.CurTime + drone.UpdateInterval;

            var ent = new Entity<ShipRepairDroneComponent>(uid, drone);
            if (!drone.Enabled)
            {
                if (drone.Phased)
                    TryLeavePhase(ent, eject: true);
                continue;
            }

            if (IsDisabledBody(uid) || _containers.IsEntityInContainer(uid) || !HasLinks(uid) ||
                drone.Grid is not { } grid || TerminatingOrDeleted(grid) ||
                !_snapshotQuery.TryGetComponent(grid, out var data) || !_repair.CanRepairGrid(uid, grid))
            {
                Disable(ent);
                continue;
            }

            if (drone.Revision != data.Revision)
            {
                CancelJob(ent);
                drone.Revision = data.Revision;
                drone.FailedTargets.Clear();
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
            if (drone.RepairDoAfter != null)
                continue;

            if (drone.Target == null)
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

            UpdateNavigation(ent, (uid, tool), (grid, data), queue, pathQuota);
        }
    }

    private void StopMoving(Entity<ShipRepairDroneComponent> ent)
    {
        _steering.Unregister(ent);
        RemComp<ActiveNPCComponent>(ent);
    }
}

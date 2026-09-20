using Content.Server.Power.Components;
using Content.Shared._Exodus.ShipRepair;
using Content.Shared.Climbing.Systems;
using Content.Shared.Construction;
using Content.Shared.DragDrop;
using Content.Shared.Interaction;
using Content.Shared.Power;
using Content.Shared.Power.Components;
using Content.Shared.Power.EntitySystems;
using Content.Shared.Verbs;
using Robust.Server.GameObjects;
using Robust.Shared.Containers;
using Robust.Shared.Map;
using Robust.Shared.Random;

namespace Content.Server._Exodus.ShipRepair;

public sealed partial class ShipRepairDroneSystem
{
    [Dependency] private UserInterfaceSystem _ui = default!;
    [Dependency] private SharedInteractionSystem _interaction = default!;
    [Dependency] private MetaDataSystem _metadata = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private SharedPowerReceiverSystem _stationPower = default!;

    private EntityQuery<ApcPowerReceiverComponent> _stationReceiverQuery;

    private void InitializeStations()
    {
        _stationReceiverQuery = GetEntityQuery<ApcPowerReceiverComponent>();

        SubscribeLocalEvent<ShipRepairStationComponent, MapInitEvent>(OnStationInit);
        SubscribeLocalEvent<ShipRepairStationComponent, ComponentShutdown>(OnStationShutdown);
        SubscribeLocalEvent<ShipRepairStationComponent, AnchorStateChangedEvent>(OnStationAnchor);
        SubscribeLocalEvent<ShipRepairStationComponent, PowerChangedEvent>(OnStationPowerChanged);
        SubscribeLocalEvent<ShipRepairStationComponent, DragDropTargetEvent>(OnStationDrop, before: new[] { typeof(ClimbSystem) });
        SubscribeLocalEvent<ShipRepairStationComponent, GetVerbsEvent<Verb>>(OnStationLoadVerbs);
        SubscribeLocalEvent<ShipRepairStationComponent, ContainerIsInsertingAttemptEvent>(OnStationInsertAttempt);
        SubscribeLocalEvent<ShipRepairStationComponent, EntInsertedIntoContainerMessage>(OnStationInserted);
        SubscribeLocalEvent<ShipRepairStationComponent, BoundUIOpenedEvent>(OnStationUiOpened);
        SubscribeLocalEvent<ShipRepairStationComponent, ShipRepairStationMessage>(OnStationCommand);
    }

    private void OnStationInit(Entity<ShipRepairStationComponent> ent, ref MapInitEvent args)
    {
        _stationPower.SetPowerDisabled(ent, !Transform(ent).Anchored);
        var container = _containers.EnsureContainer<Container>(ent, ent.Comp.ContainerId);
        foreach (var uid in container.ContainedEntities)
        {
            if (_droneQuery.TryGetComponent(uid, out var drone))
                RegisterDrone(ent, (uid, drone));
        }
        ent.Comp.WasActive = IsStationActive(ent);
        _appearance.SetData(ent, ShipRepairStationVisuals.Active, ent.Comp.WasActive);
        ent.Comp.LastGrid = Transform(ent).GridUid;
        ent.Comp.NextUiUpdate = _timing.CurTime;
    }

    private bool IsStationActive(Entity<ShipRepairStationComponent> station)
    {
        return !TerminatingOrDeleted(station) && !EntityManager.IsQueuedForDeletion(station) &&
               _xformQuery.TryGetComponent(station, out var xform) &&
               xform.Anchored && xform.GridUid is { } grid && !TerminatingOrDeleted(grid) &&
               _mapGridQuery.HasComponent(grid) &&
               _stationReceiverQuery.TryGetComponent(station, out var receiver) &&
               !receiver.PowerDisabled && receiver.Powered;
    }

    private bool TryGetStation(Entity<ShipRepairDroneComponent> drone, out Entity<ShipRepairStationComponent> station)
    {
        station = default;
        if (drone.Comp.Station is not { } uid || TerminatingOrDeleted(uid) ||
            !_stationQuery.TryGetComponent(uid, out var comp) || !comp.Drones.Contains(drone))
            return false;
        station = (uid, comp);
        return true;
    }

    private bool IsDocked(Entity<ShipRepairDroneComponent> drone, Entity<ShipRepairStationComponent> station)
    {
        return _containers.TryGetContainer(station, station.Comp.ContainerId, out var container) && container.Contains(drone);
    }

    private void OnStationDrop(Entity<ShipRepairStationComponent> ent, ref DragDropTargetEvent args)
    {
        if (args.Handled)
            return;
        args.Handled = true;
        LoadDrone(ent.Owner, args.User, args.Dragged);
    }

    private void OnStationInsertAttempt(Entity<ShipRepairStationComponent> ent, ref ContainerIsInsertingAttemptEvent args)
    {
        if (args.Container.ID != ent.Comp.ContainerId)
            return;
        if (TerminatingOrDeleted(ent) || EntityManager.IsQueuedForDeletion(ent) ||
            TerminatingOrDeleted(args.EntityUid) || EntityManager.IsQueuedForDeletion(args.EntityUid) ||
            !_droneQuery.TryGetComponent(args.EntityUid, out var drone) ||
            drone.Enabled && drone.Station != ent.Owner ||
            !ent.Comp.Drones.Contains(args.EntityUid) && ent.Comp.Drones.Count >= ent.Comp.Capacity)
            args.Cancel();
    }

    private void OnStationInserted(Entity<ShipRepairStationComponent> ent, ref EntInsertedIntoContainerMessage args)
    {
        if (args.Container.ID == ent.Comp.ContainerId && _droneQuery.TryGetComponent(args.Entity, out var drone))
        {
            RegisterDrone(ent, (args.Entity, drone));
            ClearAssignment((args.Entity, drone));
            TryLeavePhase((args.Entity, drone), eject: false);
            SetVisual((args.Entity, drone));
        }
    }

    private void RegisterDrone(Entity<ShipRepairStationComponent> station, Entity<ShipRepairDroneComponent> drone)
    {
        if (drone.Comp.Station is { } previous && previous != station.Owner &&
            TryComp<ShipRepairStationComponent>(previous, out var old))
            old.Drones.Remove(drone);
        drone.Comp.Station = station;
        if (!station.Comp.Drones.Contains(drone))
            station.Comp.Drones.Add(drone);
        if (drone.Comp.HasWorkingName)
            return;

        string name;
        bool duplicate;
        do
        {
            name = Loc.GetString("ship-repair-drone-working-name", ("id", _random.Next(0x100000).ToString("x5")));
            duplicate = false;
            foreach (var uid in station.Comp.Drones)
            {
                if (uid != drone.Owner && !TerminatingOrDeleted(uid) && Name(uid) == name)
                {
                    duplicate = true;
                    break;
                }
            }
        } while (duplicate);
        _metadata.SetEntityName(drone, name);
        drone.Comp.HasWorkingName = true;
    }

    private void UnregisterDrone(Entity<ShipRepairDroneComponent> drone)
    {
        if (drone.Comp.Station is { } uid && TryComp<ShipRepairStationComponent>(uid, out var station))
            station.Drones.Remove(drone);
        drone.Comp.Station = null;
    }

    private void OnStationShutdown(Entity<ShipRepairStationComponent> ent, ref ComponentShutdown args)
    {
        foreach (var uid in ent.Comp.Drones)
        {
            if (!_droneQuery.TryGetComponent(uid, out var drone) || drone.Station != ent.Owner)
                continue;
            drone.Station = null;
            Disable((uid, drone));
        }
        ent.Comp.Drones.Clear();
    }

    private void OnStationAnchor(Entity<ShipRepairStationComponent> ent, ref AnchorStateChangedEvent args)
    {
        // An unanchored station is switched off, preserving its charge during transport.
        _stationPower.SetPowerDisabled(ent, !args.Anchored);
        RefreshStation(ent);
        UpdateStationUi(ent);
    }

    private void OnStationPowerChanged(Entity<ShipRepairStationComponent> ent, ref PowerChangedEvent args)
    {
        RefreshStation(ent);
        UpdateStationUi(ent);
    }

    private void RefreshStation(Entity<ShipRepairStationComponent> station)
    {
        var active = IsStationActive(station);
        var grid = Transform(station).GridUid;
        if (!active || station.Comp.LastGrid != grid)
        {
            foreach (var uid in station.Comp.Drones)
            {
                if (_droneQuery.TryGetComponent(uid, out var drone) && drone.Station == station.Owner && drone.Enabled)
                    Disable((uid, drone));
            }
        }
        if (station.Comp.WasActive != active)
            _appearance.SetData(station, ShipRepairStationVisuals.Active, active);
        station.Comp.WasActive = active;
        station.Comp.LastGrid = grid;
    }

    private void OnStationUiOpened(Entity<ShipRepairStationComponent> ent, ref BoundUIOpenedEvent args)
    {
        RefreshStation(ent);
        UpdateStationUi(ent, force: true);
    }

    private void OnStationCommand(Entity<ShipRepairStationComponent> station, ref ShipRepairStationMessage args)
    {
        if (!_ui.IsUiOpen(station.Owner, ShipRepairStationUiKey.Key, args.Actor) ||
            !_interaction.InRangeUnobstructed(args.Actor, station.Owner))
            return;
        RefreshStation(station);
        var affected = 0;
        // Reverse iteration permits ejection to remove the current entry without skipping the next.
        for (var i = station.Comp.Drones.Count - 1; i >= 0; i--)
        {
            var uid = station.Comp.Drones[i];
            if (args.Drone is { } selected && selected != GetNetEntity(uid) ||
                TerminatingOrDeleted(uid) || !_droneQuery.TryGetComponent(uid, out var drone) || drone.Station != station.Owner)
                continue;
            if (ApplyStationCommand(station, (uid, drone), args.Action))
                affected++;
        }
        if (affected == 0)
            _popup.PopupEntity(Loc.GetString("ship-repair-station-command-failed"), station, args.Actor);
        UpdateStationUi(station);
    }

    private bool ApplyStationCommand(Entity<ShipRepairStationComponent> station, Entity<ShipRepairDroneComponent> drone,
        ShipRepairStationAction action)
    {
        if (action == ShipRepairStationAction.Eject)
            return TryReleaseDrone(station, drone);
        if (action == ShipRepairStationAction.Disable)
        {
            Disable(drone);
            return true;
        }
        if (!IsStationActive(station) || IsDisabledBody(drone))
            return false;
        if (action == ShipRepairStationAction.Enable)
            return TryEnable(drone);
        if (!drone.Comp.Enabled)
            return false;

        switch (action)
        {
            case ShipRepairStationAction.Repair:
                return TryBeginRepairMission(station, drone);
            case ShipRepairStationAction.Return:
                if (drone.Comp.Command == ShipRepairDroneCommand.Return)
                    return true;
                ClearAssignment(drone);
                if (!IsDocked(drone, station))
                    drone.Comp.Command = ShipRepairDroneCommand.Return;
                return true;
            case ShipRepairStationAction.Recall:
                if (IsDocked(drone, station) || _timing.CurTime < drone.Comp.NextRecall ||
                    !_containers.TryGetContainer(station, station.Comp.ContainerId, out var container))
                    return false;
                // Insertion preserves the entity and invokes OnStationInserted to cancel all pending work.
                if (!_containers.Insert(drone.Owner, container))
                    return false;
                drone.Comp.NextRecall = _timing.CurTime + drone.Comp.RecallCooldown;
                return true;
            default:
                return false;
        }
    }

    private bool TryReleaseDrone(Entity<ShipRepairStationComponent> station, Entity<ShipRepairDroneComponent> drone)
    {
        if (drone.Comp.Enabled || !IsDocked(drone, station) || !TryLaunchDrone(station, drone))
            return false;
        UnregisterDrone(drone);
        return true;
    }

    private bool TryLaunchDrone(Entity<ShipRepairStationComponent> station, Entity<ShipRepairDroneComponent> drone)
    {
        if (!_containers.TryGetContainer(station, station.Comp.ContainerId, out var container) || !container.Contains(drone))
            return false;
        var origin = _transform.GetMapCoordinates(station);
        // Keep removal usable while unanchored; find a free adjacent position on the current map.
        for (var i = 0; i < 8; i++)
        {
            var point = new MapCoordinates(origin.Position + new Angle(i * Math.Tau / 8).ToVec() * 1.1f, origin.MapId);
            if (!IsWorldClear(drone, point) || !_interaction.InRangeUnobstructed(point, station.Owner, range: 1.5f))
                continue;
            if (!_containers.Remove(drone.Owner, container, destination: _transform.ToCoordinates(point)))
                return false;
            _transform.AttachToGridOrMap(drone);
            drone.Comp.ExitBlocked = false;
            return true;
        }
        drone.Comp.ExitBlocked = true;
        return false;
    }

    private bool TryBeginRepairMission(Entity<ShipRepairStationComponent> station, Entity<ShipRepairDroneComponent> drone)
    {
        if (Transform(station).GridUid is not { } grid || !_snapshotQuery.TryGetComponent(grid, out var data) ||
            !_repair.CanRepairGrid(drone, grid) ||
            _containers.IsEntityInContainer(drone) && !IsDocked(drone, station))
            return false;
        if (drone.Comp.Command == ShipRepairDroneCommand.Repair && drone.Comp.Grid == grid && !IsDocked(drone, station))
            return true;
        if (IsDocked(drone, station) && !TryLaunchDrone(station, drone))
            return false;
        ClearAssignment(drone);
        drone.Comp.Command = ShipRepairDroneCommand.Repair;
        drone.Comp.Grid = grid;
        drone.Comp.Revision = data.Revision;
        EnsureComp<ShipRepairWorkQueueComponent>(grid).Drones.Add(drone);
        return true;
    }

    private void ClearAssignment(Entity<ShipRepairDroneComponent> drone)
    {
        CancelJob(drone);
        if (drone.Comp.Grid is { } grid && _queueQuery.TryGetComponent(grid, out var queue))
            queue.Drones.Remove(drone);
        drone.Comp.Grid = null;
        drone.Comp.Command = ShipRepairDroneCommand.Idle;
        drone.Comp.WaitingForShip = false;
        drone.Comp.ReturnBlocked = false;
        drone.Comp.ExitBlocked = false;
        drone.Comp.FailedTargets.Clear();
        drone.Comp.FailedPositions.Clear();
        drone.Comp.DeferredSearches.Clear();
        drone.Comp.StageProbes.Clear();
        drone.Comp.RecoveringDoor = false;
        drone.Comp.NavigationIssue = ShipRepairNavigationIssue.None;
        drone.Comp.FailureOrigin = null;
        drone.Comp.NextSearch = _timing.CurTime;
        drone.Comp.NextUpdate = _timing.CurTime;
    }

    private void UpdateStations()
    {
        var query = EntityQueryEnumerator<ShipRepairStationComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (_timing.CurTime < comp.NextUiUpdate)
                continue;
            comp.NextUiUpdate += TimeSpan.FromSeconds(1);
            if (comp.NextUiUpdate < _timing.CurTime)
                comp.NextUiUpdate = _timing.CurTime + TimeSpan.FromSeconds(1);
            RefreshStation((uid, comp));
            if (_ui.IsUiOpen(uid, ShipRepairStationUiKey.Key))
                UpdateStationUi((uid, comp));
        }
    }

    private ShipRepairDroneStatus GetStationDroneStatus(Entity<ShipRepairDroneComponent> drone, bool docked)
    {
        var comp = drone.Comp;
        if (IsDisabledBody(drone))
            return ShipRepairDroneStatus.Destroyed;
        if (!comp.Enabled)
            return ShipRepairDroneStatus.Off;
        if (comp.ExitBlocked)
            return ShipRepairDroneStatus.ExitBlocked;
        if (docked)
            return ShipRepairDroneStatus.Docked;
        if (comp.WaitingForShip)
            return ShipRepairDroneStatus.WaitingForShip;
        if (comp.ClearDoAfter != null)
            return ShipRepairDroneStatus.Clearing;
        if (comp.RepairReposition != null)
            return ShipRepairDroneStatus.Moving;
        if (comp.RepairDoAfter != null || comp.RepairReady)
            return ShipRepairDroneStatus.Repairing;
        if (comp.PryDoAfter != null)
            return ShipRepairDroneStatus.Prying;
        if (comp.ClearanceState != ShipRepairClearanceState.None)
            return ShipRepairDroneStatus.Stuck;
        if (comp.Search != null)
            return ShipRepairDroneStatus.Pathfinding;
        if (comp.Command == ShipRepairDroneCommand.Return)
            return comp.ReturnBlocked ? ShipRepairDroneStatus.NoReturnPath : ShipRepairDroneStatus.Returning;
        if (comp.Command == ShipRepairDroneCommand.Idle)
            return ShipRepairDroneStatus.Idle;
        return comp.Target != null ? ShipRepairDroneStatus.Moving : ShipRepairDroneStatus.Searching;
    }

    private void UpdateStationUi(Entity<ShipRepairStationComponent> station, bool force = false)
    {
        if (!_ui.IsUiOpen(station.Owner, ShipRepairStationUiKey.Key))
            return;
        var drones = new List<ShipRepairStationDroneInfo>(station.Comp.Drones.Count);
        foreach (var uid in station.Comp.Drones)
        {
            if (TerminatingOrDeleted(uid) || !_droneQuery.TryGetComponent(uid, out var comp))
                continue;
            var drone = new Entity<ShipRepairDroneComponent>(uid, comp);
            var docked = IsDocked(drone, station);
            var compatible = Transform(station).GridUid is { } grid && _snapshotQuery.HasComponent(grid) &&
                             _repair.CanRepairGrid(drone, grid);
            drones.Add(new ShipRepairStationDroneInfo(GetNetEntity(uid), MetaData(uid).EntityPrototype?.ID,
                Name(uid), GetStationDroneStatus(drone, docked), comp.Enabled, docked, !IsDisabledBody(uid), compatible,
                Math.Max(0, (int) Math.Ceiling((comp.NextRecall - _timing.CurTime).TotalSeconds))));
        }
        var active = IsStationActive(station);
        var anchored = Transform(station).Anchored;
        var batteryPowered = active && TryComp<ApcPowerReceiverBatteryComponent>(station, out var backup) && backup.Enabled;
        var batteryPercent = 0;
        if (TryComp<BatteryComponent>(station, out var battery) && battery.MaxCharge > 0)
            batteryPercent = (int) Math.Clamp(Math.Ceiling(battery.CurrentCharge / battery.MaxCharge * 100), 0, 100);
        var old = station.Comp.LastUiState;
        if (!force && old != null && old.Active == active && old.Anchored == anchored &&
            old.BatteryPowered == batteryPowered && old.BatteryPercent == batteryPercent &&
            old.Capacity == station.Comp.Capacity && old.Drones.Count == drones.Count)
        {
            var same = true;
            for (var i = 0; i < drones.Count; i++)
                same &= drones[i] == old.Drones[i];
            if (same)
                return;
        }
        var state = new ShipRepairStationUiState(active, anchored, batteryPowered, batteryPercent, station.Comp.Capacity, drones);
        station.Comp.LastUiState = state;
        _ui.SetUiState(station.Owner, ShipRepairStationUiKey.Key, state);
    }
}

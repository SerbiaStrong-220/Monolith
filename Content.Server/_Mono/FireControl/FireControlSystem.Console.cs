// Copyright Rane (elijahrane@gmail.com) 2025
// All rights reserved. Relicensed under AGPL with permission

using Content.Server._Mono.Ships.Systems;
using Content.Server.Administration.Logs;
using Content.Server.Shuttles.Systems;
using Content.Shared._Mono.FireControl;
using Content.Shared.Database;
using Content.Shared._Mono.Ships.Components;
using Content.Shared.Popups;
using Content.Shared.Power;
using Content.Shared.Shuttles.BUIStates;
using Content.Shared.UserInterface;
using Content.Shared.Weapons.Ranged;
using Content.Shared.Weapons.Ranged.Components;
using Robust.Server.GameObjects;
using Robust.Shared.Containers;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Timing;
using System.Numerics;
using Content.Server._Crescent.ShipShields; // Exodus
using Content.Server._Exodus.Territory; // Exodus territory fire logs
using Content.Shared._Exodus.FireControl; // Exodus fire-control cursor optimization

namespace Content.Server._Mono.FireControl;

public sealed partial class FireControlSystem : EntitySystem
{
    [Dependency] private UserInterfaceSystem _ui = default!;
    [Dependency] private ShuttleConsoleSystem _shuttleConsoleSystem = default!;
    [Dependency] private TransformSystem _transform = default!;
    [Dependency] private CrewedShuttleSystem _crewedShuttle = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedContainerSystem _containers = default!;
    [Dependency] private IAdminLogManager _adminLogger = default!;
    [Dependency] private IMapManager _mapMan = default!;
    [Dependency] private ShipShieldsSystem _shields = default!; // Exodus
    [Dependency] private GridTerritorySystem _territory = default!; // Exodus territory fire logs

    private void InitializeConsole()
    {
        // Exodus: Component lifecycle events replace the previous player-spawn-wide refresh.
        SubscribeLocalEvent<FireControlConsoleComponent, PowerChangedEvent>(OnPowerChanged);
        SubscribeLocalEvent<FireControlConsoleComponent, ComponentShutdown>(OnComponentShutdown);
        SubscribeLocalEvent<FireControlConsoleComponent, FireControlConsoleRefreshServerMessage>(OnRefreshServer);
        SubscribeLocalEvent<FireControlConsoleComponent, FireControlConsoleFireMessage>(OnFire);
        SubscribeLocalEvent<FireControlConsoleComponent, FireControlConsoleCursorPositionMessage>(OnCursorPosition); // Exodus fire-control cursor optimization
        SubscribeLocalEvent<FireControlConsoleComponent, BoundUIOpenedEvent>(OnUIOpened);
        SubscribeLocalEvent<FireControlConsoleComponent, ActivatableUIOpenAttemptEvent>(OnConsoleUIOpenAttempt);
    }

    private void OnPowerChanged(EntityUid uid, FireControlConsoleComponent component, PowerChangedEvent args)
    {
        if (args.Powered)
            TryRegisterConsole(uid, component);
        else
            UnregisterConsole(uid, component);

        QueueConsoleUiUpdate(uid); // Exodus fire-control event-driven UI updates
    }

    private void OnComponentShutdown(EntityUid uid, FireControlConsoleComponent component, ComponentShutdown args)
    {
        UnregisterConsole(uid, component);
        _pendingUiConsoles.Remove(uid); // Exodus fire-control event-driven UI updates
        _nextUiUpdates.Remove(uid); // Exodus fire-control event-driven UI updates
    }

    private void DoRefreshServer(EntityUid uid, FireControlConsoleComponent component)
    {
        // First, clean up any invalid server references across all grids
        CleanupInvalidServerReferences();

        // Get the console's grid to force server reconnection on it
        var consoleGrid = _xform.GetGrid(uid);
        if (consoleGrid != null)
        {
            // Force all servers on this grid to attempt reconnection
            ForceServerReconnectionOnGrid((EntityUid)consoleGrid);
        }

        TryRegisterConsole(uid, component); // Exodus fire-control event-driven UI updates

        // Refresh controllables if we have a valid server connection
        if (component.ConnectedServer != null &&
            TryComp<FireControlServerComponent>(component.ConnectedServer, out var server) &&
            server.ConnectedGrid != null)
        {
            RefreshControllables((EntityUid)server.ConnectedGrid, immediateConsole: uid); // Exodus fire-control event-driven UI updates
        }

        // Always update UI to reflect current state
        UpdateUi(uid, component);
        MarkConsoleUiUpdated(uid); // Exodus fire-control event-driven UI updates
    }

    private void OnRefreshServer(EntityUid uid, FireControlConsoleComponent component, FireControlConsoleRefreshServerMessage args)
    {
        DoRefreshServer(uid, component);
    }

    private void OnFire(EntityUid uid, FireControlConsoleComponent component, FireControlConsoleFireMessage args)
    {
        if (component.ConnectedServer == null
            || !TryComp<FireControlServerComponent>(component.ConnectedServer, out var server)
            || !server.Consoles.Contains(uid))
            return;

        var xform = Transform(uid);
        var grid = xform.GridUid;
        if (grid == null || server.ConnectedGrid != grid) // Exodus fire-control cursor optimization
            return;

        var coordinates = GetCoordinates(args.Coordinates); // Exodus fire-control cursor optimization
        if (!coordinates.IsValid(EntityManager)) // Exodus fire-control cursor optimization
            return;

        // Exodus-begin fire-control cursor optimization
        // Keep legacy empty fire messages lightweight as well as using the dedicated cursor message.
        if (args.Selected.Count == 0)
        {
            RaiseCursorPosition(uid, coordinates);
            return;
        }
        // Exodus-end

        // Fire the actual weapons
        FireWeapons((EntityUid)component.ConnectedServer, args.Selected, args.Coordinates, args.Actor, server, uid); // Exodus fire-control cursor optimization

        if (component.NextLog == null || component.NextLog < _timing.CurTime)
        {
            var fireCoordinates = _transform.ToMapCoordinates(coordinates); // Exodus territory fire logs
            var firePos = fireCoordinates.Position; // Exodus territory fire logs
            var ourPos = _transform.GetWorldPosition(grid.Value);
            var grids = new List<Entity<MapGridComponent>>();
            var adjust = new Vector2(component.LogGridLookupRange, component.LogGridLookupRange);
            _mapMan.FindGridsIntersecting(xform.MapID, new Box2(firePos - adjust, firePos + adjust), ref grids, approx: true, includeMap: false);
            grids.RemoveAll(g => g == grid);
            EntityUid? closest = null;
            foreach (var gridUid in grids)
            {
                var newPos = _transform.GetWorldPosition(gridUid);
                if (closest == null || (newPos - firePos).LengthSquared() < (_transform.GetWorldPosition(closest.Value) - firePos).LengthSquared())
                    closest = gridUid;
            }

            // Exodus start - territory info in ship gun logs
            if (_territory.TryGetTerritoryAt(fireCoordinates, out var territory))
            {
                var territoryOwner = territory.Comp.ControllingFaction?.Id ?? "unclaimed";

                _adminLogger.Add(LogType.ShipgunFired, LogImpact.High,
                    $"{ToPrettyString(args.Actor):user} fired weaponry of ship {ToPrettyString(grid):entity} from ({ourPos}) to ({firePos}), closest grid: {ToPrettyString(closest)}, in territory {ToPrettyString(territory.Owner):entity}, territory owner: {territoryOwner}");
            }
            else
            {
                _adminLogger.Add(LogType.ShipgunFired, LogImpact.High,
                    $"{ToPrettyString(args.Actor):user} fired weaponry of ship {ToPrettyString(grid):entity} from ({ourPos}) to ({firePos}), closest grid: {ToPrettyString(closest)}");
            }
            // Exodus end - territory info in ship gun logs

            component.NextLog = _timing.CurTime + component.LogSpacing;
        }

        // Exodus-begin fire-control cursor optimization
        RaiseCursorPosition(uid, coordinates);
        // Exodus-end
    }

    // Exodus-begin fire-control cursor optimization
    private void OnCursorPosition(Entity<FireControlConsoleComponent> ent, ref FireControlConsoleCursorPositionMessage args)
    {
        if (!_consoleMousePositions.ContainsKey(ent))
            return;

        var grid = Transform(ent).GridUid;
        if (grid == null
            || ent.Comp.ConnectedServer == null
            || !TryComp<FireControlServerComponent>(ent.Comp.ConnectedServer, out var server)
            || !server.Consoles.Contains(ent)
            || server.ConnectedGrid != grid)
        {
            return;
        }

        var coordinates = GetCoordinates(args.Coordinates);
        if (!coordinates.IsValid(EntityManager))
            return;

        RaiseCursorPosition(ent, coordinates);
    }

    private void RaiseCursorPosition(EntityUid console, EntityCoordinates coordinates)
    {
        var cursorEvent = new FireControlConsoleCursorPositionEvent(coordinates);
        RaiseLocalEvent(console, ref cursorEvent);
    }
    // Exodus-end

    public void OnUIOpened(EntityUid uid, FireControlConsoleComponent component, BoundUIOpenedEvent args)
    {
        DoRefreshServer(uid, component); // Exodus fire-control event-driven UI updates
    }

    private void OnConsoleUIOpenAttempt(
        EntityUid uid,
        FireControlConsoleComponent component,
        ActivatableUIOpenAttemptEvent args)
    {
        var shuttle = _transform.GetParentUid(uid);
        var uiOpen = _crewedShuttle.AnyShuttleConsoleActiveByPlayer(shuttle, args.User);
        var forceOne = HasComp<CrewedShuttleComponent>(shuttle) && !HasComp<AdvancedPilotComponent>(args.User);

        // Crewed shuttles should not allow people to have both gunnery and shuttle consoles open.
        if (uiOpen && forceOne)
        {
            args.Cancel();
            _popup.PopupClient(Loc.GetString("shuttle-console-crewed"), args.User);
        }
    }

    private void UnregisterConsole(EntityUid console, FireControlConsoleComponent? component = null)
    {
        if (!Resolve(console, ref component))
            return;

        if (component.ConnectedServer == null)
            return;

        // Check if server still exists before trying to unregister
        if (Exists(component.ConnectedServer) && TryComp<FireControlServerComponent>(component.ConnectedServer, out var server))
        {
            server.Consoles.Remove(console);
        }

        component.ConnectedServer = null;
    }

    private bool CanRegister((EntityUid? ServerUid, FireControlServerComponent? ServerComponent) gridServer)
    {
        if (gridServer.ServerComponent == null)
            return false;

        if (gridServer.ServerComponent.EnforceMaxConsoles
            && gridServer.ServerComponent.Consoles.Count >= gridServer.ServerComponent.MaxConsoles)
            return false;

        return true;
    }

    private bool TryRegisterConsole(EntityUid console, FireControlConsoleComponent? consoleComponent = null)
    {
        if (!Resolve(console, ref consoleComponent))
            return false;

        // Exodus-begin fire-control event-driven UI updates
        var canOperate = Transform(console).Anchored && _power.IsPowered(console);
        var gridServer = TryGetGridServer(console);

        if (consoleComponent.ConnectedServer is { } connectedServer)
        {
            var connectionValid = canOperate
                && gridServer.ServerUid == connectedServer
                && gridServer.ServerComponent != null
                && gridServer.ServerComponent.Consoles.Contains(console);

            if (connectionValid)
                return true;

            if (TryComp<FireControlServerComponent>(connectedServer, out var previousServer))
                previousServer.Consoles.Remove(console);

            consoleComponent.ConnectedServer = null;
        }

        if (!canOperate || gridServer.ServerUid == null || gridServer.ServerComponent == null)
            return false;
        // Exodus-end

        var canRegister = CanRegister(gridServer);

        if (canRegister && gridServer.ServerComponent.Consoles.Add(console))
        {
            consoleComponent.ConnectedServer = gridServer.ServerUid;
            return true;
        }

        return false;
    }

    private void UpdateUi(EntityUid uid, FireControlConsoleComponent? component = null)
    {
        if (!Resolve(uid, ref component))
            return;

        if (!_ui.IsUiOpen(uid, FireControlConsoleUiKey.Key)) // Exodus fire-control event-driven UI updates
            return;

        NavInterfaceState navState = _shuttleConsoleSystem.GetNavState(uid, _shuttleConsoleSystem.GetAllDocks(), _shuttleConsoleSystem.GetAllGrapLinks()); // Exodus - ShuttleHooks

        List<FireControllableEntry> controllables = new();
        if (component.ConnectedServer != null && TryComp<FireControlServerComponent>(component.ConnectedServer, out var server))
        {
            if (!server.Consoles.Contains(uid))
                return;

            foreach (var controllable in server.Controlled)
            {
                var controlled = new FireControllableEntry();
                controlled.NetEntity = EntityManager.GetNetEntity(controllable);
                controlled.Coordinates = GetNetCoordinates(Transform(controllable).Coordinates);
                controlled.Name = MetaData(controllable).EntityName;

                var (ammoCount, hasManualReload) = GetWeaponAmmunitionInfo(controllable);
                controlled.AmmoCount = ammoCount;
                controlled.HasManualReload = hasManualReload;

                // Exodus-Start
                if (TryComp<GunComponent>(controllable, out var gun))
                    controlled.NextFire = gun.NextFire;
                // Exodus-End

                controllables.Add(controlled);
            }
        }

        var array = controllables.ToArray();

        // Exodus | add shield state
        var gridUid = Transform(uid).GridUid;
        var shieldState = gridUid == null ? null : _shields.GetShieldState(gridUid.Value);

        var state = new FireControlConsoleBoundInterfaceState(component.ConnectedServer != null, array, navState, shieldState);
        _ui.SetUiState(uid, FireControlConsoleUiKey.Key, state);
    }

    /// <summary>
    /// Gets ammo information for a weapon to determine if it has manual reload.
    /// </summary>
    private (int? ammoCount, bool hasManualReload) GetWeaponAmmunitionInfo(EntityUid weaponEntity)
    {
        if (TryComp<BasicEntityAmmoProviderComponent>(weaponEntity, out var basicAmmo))
        {
            var hasRecharge = HasComp<RechargeBasicEntityAmmoComponent>(weaponEntity);

            return (basicAmmo.Count, !hasRecharge);
        }

        if (TryComp<BallisticAmmoProviderComponent>(weaponEntity, out var ballisticAmmo))
        {
            // if we're InfiniteUnspawned consider us to be non-reloading when at 0 ammo
            return (ballisticAmmo.Count, ballisticAmmo.Cycleable && (ballisticAmmo.Count != 0 || !ballisticAmmo.InfiniteUnspawned));
        }

        if (TryComp<MagazineAmmoProviderComponent>(weaponEntity, out var magazineAmmo))
        {
            var magazineEntity = GetMagazineEntity(weaponEntity);
            if (magazineEntity != null)
            {
                if (TryComp<BallisticAmmoProviderComponent>(magazineEntity, out var magazineBallisticAmmo))
                {
                    var hasAmmo = magazineBallisticAmmo.Cycleable
                             && (magazineBallisticAmmo.Count != 0 || !magazineBallisticAmmo.InfiniteUnspawned);
                    return (magazineBallisticAmmo.Count, hasAmmo);
                }

                if (TryComp<BasicEntityAmmoProviderComponent>(magazineEntity, out var magazineBasicAmmo))
                {
                    var hasRecharge = HasComp<RechargeBasicEntityAmmoComponent>(magazineEntity);
                    return (magazineBasicAmmo.Count, !hasRecharge);
                }
            }
        }

        return (null, false);
    }

    /// <summary>
    /// Gets the magazine entity from a weapon's magazine slot.
    /// </summary>
    private EntityUid? GetMagazineEntity(EntityUid weaponEntity)
    {
        if (!_containers.TryGetContainer(weaponEntity, "gun_magazine", out var container) ||
            container is not ContainerSlot slot)
        {
            return null;
        }

        return slot.ContainedEntity;
    }
}

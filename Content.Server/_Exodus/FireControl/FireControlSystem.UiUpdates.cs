using Content.Server._Crescent.ShipShields;
using Content.Server._Exodus.SpaceArtillery;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Events;
using Content.Shared._Mono.FireControl;
using Content.Shared.Weapons.Ranged.Events;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Shared.Containers;

namespace Content.Server._Mono.FireControl;

public sealed partial class FireControlSystem
{
    private static readonly TimeSpan UiUpdateInterval = TimeSpan.FromMilliseconds(250);

    private readonly HashSet<EntityUid> _pendingUiServers = [];
    private readonly HashSet<EntityUid> _pendingUiConsoles = [];
    private readonly HashSet<EntityUid> _pendingUiGrids = [];
    private readonly Dictionary<EntityUid, TimeSpan> _nextUiUpdates = [];
    private readonly List<EntityUid> _processedUiConsoles = [];
    private bool _queueAllUiConsoles;

    private void InitializeUiUpdates()
    {
        SubscribeLocalEvent<FireControlConsoleComponent, ComponentStartup>(OnConsoleStartup);
        SubscribeLocalEvent<FireControlConsoleComponent, AnchorStateChangedEvent>(OnConsoleAnchorChanged);
        SubscribeLocalEvent<FireControlConsoleComponent, EntParentChangedMessage>(OnConsoleParentChanged);

        SubscribeLocalEvent<FireControlServerComponent, ComponentStartup>(OnServerStartup);
        SubscribeLocalEvent<FireControlServerComponent, AnchorStateChangedEvent>(OnServerAnchorChanged);
        SubscribeLocalEvent<FireControlServerComponent, EntParentChangedMessage>(OnServerParentChanged);

        SubscribeLocalEvent<FireControllableComponent, AnchorStateChangedEvent>(OnControllableAnchorChanged);
        SubscribeLocalEvent<FireControllableComponent, ComponentStartup>(OnControllableStartup);
        SubscribeLocalEvent<FireControllableComponent, EntityRenamedEvent>(OnControllableRenamed);
        SubscribeLocalEvent<FireControllableComponent, GunShotEvent>(OnControllableGunShot);
        SubscribeLocalEvent<FireControllableComponent, GunCycledEvent>(OnControllableGunCycled);
        SubscribeLocalEvent<FireControllableComponent, OnEmptyGunShotEvent>(OnControllableEmptyShot);

        SubscribeLocalEvent<FireControllableComponent, EntInsertedIntoContainerMessage>(OnControllableAmmoInserted);
        SubscribeLocalEvent<FireControllableComponent, EntRemovedFromContainerMessage>(OnControllableAmmoRemoved);

        SubscribeLocalEvent<DockingComponent, ComponentRemove>(OnDockingPortRemoved);
        SubscribeLocalEvent<DockingComponent, EntParentChangedMessage>(OnDockingPortParentChanged);
        SubscribeLocalEvent<DockingComponent, EntityRenamedEvent>(OnDockingPortRenamed);

        SubscribeLocalEvent<ShipShieldStateChangedEvent>(OnShieldStateChanged);
        SubscribeLocalEvent<DockEvent>(OnDockChanged);
        SubscribeLocalEvent<UndockEvent>(OnUndockChanged);
        SubscribeLocalEvent<ShipGrappleEvent>(OnShipGrappleChanged);
        SubscribeLocalEvent<ShipUngrappleEvent>(OnShipUngrappleChanged);
    }

    private void OnConsoleStartup(Entity<FireControlConsoleComponent> ent, ref ComponentStartup args)
    {
        TryRegisterConsole(ent, ent.Comp);
        QueueConsoleUiUpdate(ent);
    }

    private void OnConsoleAnchorChanged(Entity<FireControlConsoleComponent> ent, ref AnchorStateChangedEvent args)
    {
        ReconnectConsole(ent);
    }

    private void OnConsoleParentChanged(Entity<FireControlConsoleComponent> ent, ref EntParentChangedMessage args)
    {
        ReconnectConsole(ent);
    }

    private void ReconnectConsole(Entity<FireControlConsoleComponent> ent)
    {
        UnregisterConsole(ent, ent.Comp);
        TryRegisterConsole(ent, ent.Comp);
        QueueConsoleUiUpdate(ent);
    }

    private void OnServerStartup(Entity<FireControlServerComponent> ent, ref ComponentStartup args)
    {
        TryConnect(ent, ent.Comp);
    }

    private void OnServerAnchorChanged(Entity<FireControlServerComponent> ent, ref AnchorStateChangedEvent args)
    {
        if (args.Anchored)
            TryConnect(ent, ent.Comp);
        else
            Disconnect(ent, ent.Comp);
    }

    private void OnServerParentChanged(Entity<FireControlServerComponent> ent, ref EntParentChangedMessage args)
    {
        Disconnect(ent, ent.Comp);
        TryConnect(ent, ent.Comp);
    }

    private void OnControllableStartup(Entity<FireControllableComponent> ent, ref ComponentStartup args)
    {
        if (Transform(ent).Anchored && _power.IsPowered(ent) && TryRegister(ent, ent.Comp))
            QueueServerUiUpdate(ent.Comp.ControllingServer);
    }

    private void OnControllableAnchorChanged(Entity<FireControllableComponent> ent, ref AnchorStateChangedEvent args)
    {
        var previousServer = ent.Comp.ControllingServer;

        if (args.Anchored && _power.IsPowered(ent))
        {
            if (TryRegister(ent, ent.Comp))
                QueueServerUiUpdate(ent.Comp.ControllingServer);

            return;
        }

        Unregister(ent, ent.Comp);
        QueueServerUiUpdate(previousServer);
    }

    private void OnControllableRenamed(Entity<FireControllableComponent> ent, ref EntityRenamedEvent args)
    {
        QueueServerUiUpdate(ent.Comp.ControllingServer);
    }

    private void OnControllableGunShot(Entity<FireControllableComponent> ent, ref GunShotEvent args)
    {
        QueueServerUiUpdate(ent.Comp.ControllingServer);
    }

    private void OnControllableGunCycled(Entity<FireControllableComponent> ent, ref GunCycledEvent args)
    {
        QueueServerUiUpdate(ent.Comp.ControllingServer);
    }

    private void OnControllableEmptyShot(Entity<FireControllableComponent> ent, ref OnEmptyGunShotEvent args)
    {
        QueueServerUiUpdate(ent.Comp.ControllingServer);
    }

    private void OnControllableAmmoInserted(Entity<FireControllableComponent> ent, ref EntInsertedIntoContainerMessage args)
    {
        QueueServerUiUpdate(ent.Comp.ControllingServer);
    }

    private void OnControllableAmmoRemoved(Entity<FireControllableComponent> ent, ref EntRemovedFromContainerMessage args)
    {
        QueueServerUiUpdate(ent.Comp.ControllingServer);
    }

    private void OnDockingPortRemoved(Entity<DockingComponent> ent, ref ComponentRemove args)
    {
        QueueAllConsoleUiUpdates();
    }

    private void OnDockingPortParentChanged(Entity<DockingComponent> ent, ref EntParentChangedMessage args)
    {
        QueueAllConsoleUiUpdates();
    }

    private void OnDockingPortRenamed(Entity<DockingComponent> ent, ref EntityRenamedEvent args)
    {
        QueueAllConsoleUiUpdates();
    }

    private void OnShieldStateChanged(ref ShipShieldStateChangedEvent args)
    {
        QueueGridConsoleUiUpdates(args.Grid);
    }

    private void OnDockChanged(DockEvent args)
    {
        QueueAllConsoleUiUpdates();
    }

    private void OnUndockChanged(UndockEvent args)
    {
        QueueAllConsoleUiUpdates();
    }

    private void OnShipGrappleChanged(ShipGrappleEvent args)
    {
        QueueAllConsoleUiUpdates();
    }

    private void OnShipUngrappleChanged(ShipUngrappleEvent args)
    {
        QueueAllConsoleUiUpdates();
    }

    private void QueueAllConsoleUiUpdates()
    {
        _queueAllUiConsoles = true;
    }

    private void QueueGridConsoleUiUpdates(EntityUid grid)
    {
        _pendingUiGrids.Add(grid);
    }

    private void QueueServerUiUpdate(EntityUid? server)
    {
        if (server is { } uid)
            _pendingUiServers.Add(uid);
    }

    private void QueueConsoleUiUpdate(EntityUid console)
    {
        if (!TerminatingOrDeleted(console))
            _pendingUiConsoles.Add(console);
    }

    private void MarkConsoleUiUpdated(EntityUid console)
    {
        _pendingUiConsoles.Remove(console);
        if (_ui.IsUiOpen(console, FireControlConsoleUiKey.Key))
            _nextUiUpdates[console] = _timing.CurTime + UiUpdateInterval;
        else
            _nextUiUpdates.Remove(console);
    }

    private void ProcessPendingUiUpdates()
    {
        if (_pendingUiServers.Count == 0
            && _pendingUiConsoles.Count == 0
            && _pendingUiGrids.Count == 0
            && !_queueAllUiConsoles)
        {
            return;
        }

        if (_queueAllUiConsoles)
        {
            var allConsoles = EntityQueryEnumerator<FireControlConsoleComponent>();
            while (allConsoles.MoveNext(out var uid, out _))
            {
                if (_ui.IsUiOpen(uid, FireControlConsoleUiKey.Key))
                    _pendingUiConsoles.Add(uid);
            }

            _queueAllUiConsoles = false;
            _pendingUiGrids.Clear();
        }
        else if (_pendingUiGrids.Count != 0)
        {
            var gridConsoles = EntityQueryEnumerator<FireControlConsoleComponent, TransformComponent>();
            while (gridConsoles.MoveNext(out var uid, out _, out var xform))
            {
                if (xform.GridUid is { } grid
                    && _pendingUiGrids.Contains(grid)
                    && _ui.IsUiOpen(uid, FireControlConsoleUiKey.Key))
                {
                    _pendingUiConsoles.Add(uid);
                }
            }

            _pendingUiGrids.Clear();
        }

        foreach (var serverUid in _pendingUiServers)
        {
            if (!TryComp<FireControlServerComponent>(serverUid, out var server))
                continue;

            foreach (var console in server.Consoles)
                _pendingUiConsoles.Add(console);
        }

        _pendingUiServers.Clear();

        var curTime = _timing.CurTime;
        _processedUiConsoles.Clear();
        foreach (var console in _pendingUiConsoles)
        {
            if (!TryComp<FireControlConsoleComponent>(console, out var component)
                || !_ui.IsUiOpen(console, FireControlConsoleUiKey.Key))
            {
                _nextUiUpdates.Remove(console);
                _processedUiConsoles.Add(console);
                continue;
            }

            if (_nextUiUpdates.TryGetValue(console, out var nextUpdate) && nextUpdate > curTime)
                continue;

            UpdateUi(console, component);
            _nextUiUpdates[console] = curTime + UiUpdateInterval;
            _processedUiConsoles.Add(console);
        }

        foreach (var console in _processedUiConsoles)
            _pendingUiConsoles.Remove(console);
    }

    private void RegisterPoweredConsolesOnGrid(EntityUid grid)
    {
        var query = EntityQueryEnumerator<FireControlConsoleComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var console, out var xform))
        {
            if (xform.GridUid != grid)
                continue;

            var previousServer = console.ConnectedServer;
            TryRegisterConsole(uid, console);
            if (previousServer != console.ConnectedServer)
                QueueConsoleUiUpdate(uid);
        }
    }
}

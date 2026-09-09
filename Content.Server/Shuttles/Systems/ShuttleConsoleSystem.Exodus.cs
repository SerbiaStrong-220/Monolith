using Content.Server._Crescent.ShipShields;
using Content.Server._Exodus.Nebula;
using Content.Server.Shuttles.Components;
using Content.Shared.Exodus.ShipShields;
using Content.Shared.Shuttles.Components;
using Robust.Server.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Timing;

namespace Content.Server.Shuttles.Systems;

public sealed partial class ShuttleConsoleSystem
{
    private static readonly TimeSpan ShieldUiUpdateInterval = TimeSpan.FromMilliseconds(250);

    [Dependency] private NebulaSystem _nebula = default!;
    [Dependency] private ShipShieldsSystem _shields = default!;
    [Dependency] private IGameTiming _timing = default!;

    private readonly HashSet<EntityUid> _pendingShieldUiGrids = [];
    private readonly Dictionary<EntityUid, ShipShieldState?> _shieldUiStateCache = [];
    private TimeSpan _nextShieldUiUpdate;

    private void InitializeShieldUi()
    {
        SubscribeLocalEvent<ShipShieldStateChangedEvent>(OnShieldStateChanged);
        SubscribeLocalEvent<ShuttleConsoleComponent, BoundUIOpenedEvent>(OnShuttleConsoleUiOpened);
    }

    private void OnShieldStateChanged(ref ShipShieldStateChangedEvent args)
    {
        _pendingShieldUiGrids.Add(args.Grid);
    }

    private void OnShuttleConsoleUiOpened(Entity<ShuttleConsoleComponent> ent, ref BoundUIOpenedEvent args)
    {
        if (!args.UiKey.Equals(ShuttleConsoleUiKey.Key))
            return;

        var grid = Transform(ent.Owner).GridUid;
        var state = grid is { } gridUid
            ? _shields.GetShieldState(gridUid)
            : null;
        _ui.ServerSendUiMessage(
            ent.Owner,
            ShuttleConsoleUiKey.Key,
            new ShuttleShieldStateMessage(state),
            args.Actor);
    }

    private void ProcessShieldUiUpdates()
    {
        if (_pendingShieldUiGrids.Count == 0 || _timing.CurTime < _nextShieldUiUpdate)
            return;

        _shieldUiStateCache.Clear();
        var query = EntityQueryEnumerator<ShuttleConsoleComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out _, out var xform))
        {
            if (xform.GridUid is not { } grid
                || !_pendingShieldUiGrids.Contains(grid)
                || !_ui.IsUiOpen(uid, ShuttleConsoleUiKey.Key))
            {
                continue;
            }

            if (!_shieldUiStateCache.TryGetValue(grid, out var state))
            {
                state = _shields.GetShieldState(grid);
                _shieldUiStateCache.Add(grid, state);
            }

            _ui.ServerSendUiMessage(
                uid,
                ShuttleConsoleUiKey.Key,
                new ShuttleShieldStateMessage(state));
        }

        _shieldUiStateCache.Clear();
        _pendingShieldUiGrids.Clear();
        _nextShieldUiUpdate = _timing.CurTime + ShieldUiUpdateInterval;
    }

    private bool CanFTLToNebula(EntityUid shuttleUid, EntityCoordinates targetCoordinates, Angle targetAngle, out string rejection)
    {
        return _nebula.CanFTL(shuttleUid, targetCoordinates, targetAngle, out rejection);
    }
}

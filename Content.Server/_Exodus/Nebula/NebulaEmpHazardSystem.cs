using Content.Server.Emp;
using Content.Shared._Exodus.Nebula;
using Content.Shared.GameTicking;
using Content.Shared.Maps;
using Robust.Server.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._Exodus.Nebula;

/// <summary>
/// Drives nebula EMP effects. Iterates only entities the coordinator has tagged with
/// <see cref="NebulaEmpGridHazardComponent"/> (grids inside an EMP nebula) and
/// <see cref="NebulaSpaceEmpTargetComponent"/> (free EVA entities). Static configuration is
/// pulled from the marker prototype's
/// <see cref="Content.Shared._Exodus.Nebula.NebulaEmpHazardComponent"/>.
/// </summary>
public sealed class NebulaEmpHazardSystem : EntitySystem
{
    private static readonly TimeSpan UpdateInterval = TimeSpan.FromSeconds(1);
    private const string SparksPrototype = "EffectSparks";

    [Dependency] private readonly EmpSystem _emp = default!;
    [Dependency] private readonly IComponentFactory _componentFactory = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IPrototypeManager _prototype = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly MapSystem _map = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;

    private TimeSpan _nextUpdate;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestart);
    }

    public override void Update(float frameTime)
    {
        if (_timing.CurTime < _nextUpdate)
            return;

        _nextUpdate = _timing.CurTime + UpdateInterval;

        UpdateGrids();
        UpdateSpaceTargets();
    }

    private void OnRoundRestart(RoundRestartCleanupEvent args)
    {
        var grids = EntityQueryEnumerator<NebulaEmpGridHazardComponent>();
        while (grids.MoveNext(out var uid, out _))
            RemCompDeferred<NebulaEmpGridHazardComponent>(uid);

        var players = EntityQueryEnumerator<NebulaSpaceEmpTargetComponent>();
        while (players.MoveNext(out var uid, out _))
            RemCompDeferred<NebulaSpaceEmpTargetComponent>(uid);
    }

    private void UpdateGrids()
    {
        var query = EntityQueryEnumerator<NebulaEmpGridHazardComponent, MapGridComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var hazard, out var grid, out var xform))
        {
            if (!TryComp<NebulaPresenceComponent>(uid, out var presence) || presence.Marker != hazard.Marker)
            {
                RemCompDeferred<NebulaEmpGridHazardComponent>(uid);
                continue;
            }

            if (!NebulaQueryHelper.TryGetMarkerComponent<NebulaEmpHazardComponent>(_prototype, _componentFactory, hazard.Marker, out var config))
                continue;

            InitializeGridTimers(hazard, config);

            if (_timing.CurTime < hazard.NextPulse)
                continue;

            ScheduleNextPulse(hazard, config);

            if (TryPulseGrid((uid, grid, xform), hazard.Marker, config))
                RecordPulse(hazard);
        }
    }

    private void UpdateSpaceTargets()
    {
        var query = EntityQueryEnumerator<NebulaSpaceEmpTargetComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var target, out var xform))
        {
            // Resolve marker via presence each tick so transitions between nebula kinds
            // (e.g. death-zone inner -> outer at the 90k boundary) take effect immediately.
            if (!TryComp<NebulaPresenceComponent>(uid, out var presence) ||
                !NebulaQueryHelper.TryGetMarkerComponent<NebulaSpaceEmpHazardComponent>(_prototype, _componentFactory, presence.Marker, out var config))
            {
                RemCompDeferred<NebulaSpaceEmpTargetComponent>(uid);
                continue;
            }

            if (IsOnNonEmptyTile(xform))
                continue;

            if (target.NextPulse == TimeSpan.Zero)
            {
                ScheduleNextSpacePulse(target, config);
                continue;
            }

            if (_timing.CurTime < target.NextPulse)
                continue;

            PulseSpaceTarget((uid, target, xform), _transform.GetMapCoordinates(uid, xform), config);
            ScheduleNextSpacePulse(target, config);
        }
    }

    // Marker config / position checks moved to NebulaQueryHelper.

    private void InitializeGridTimers(NebulaEmpGridHazardComponent hazard, NebulaEmpHazardComponent config)
    {
        if (hazard.TimersInitialized)
            return;

        hazard.TimersInitialized = true;
        ScheduleNextPulse(hazard, config);
    }

    private void ScheduleNextPulse(NebulaEmpGridHazardComponent hazard, NebulaEmpHazardComponent config)
    {
        var (min, max) = GetDelayRange(config.MinDelaySeconds, config.MaxDelaySeconds);
        hazard.NextPulse = _timing.CurTime + TimeSpan.FromSeconds(_random.Next(min, max + 1));
    }

    private void ScheduleNextSpacePulse(NebulaSpaceEmpTargetComponent target, NebulaSpaceEmpHazardComponent config)
    {
        var (min, max) = GetDelayRange(config.MinStrikeDelaySeconds, config.MaxStrikeDelaySeconds);
        target.NextPulse = _timing.CurTime + TimeSpan.FromSeconds(_random.Next(min, max + 1));
    }

    private static (int Min, int Max) GetDelayRange(int first, int second)
    {
        var min = Math.Max(1, Math.Min(first, second));
        var max = Math.Max(min, Math.Max(first, second));
        return (min, max);
    }

    private void RecordPulse(NebulaEmpGridHazardComponent hazard)
    {
        if (hazard.LastPulse != TimeSpan.Zero)
            hazard.LastPulseDelta = _timing.CurTime - hazard.LastPulse;
        hazard.LastPulse = _timing.CurTime;
        hazard.PulseCount++;
    }

    private bool TryPulseGrid(
        Entity<MapGridComponent, TransformComponent> grid,
        EntProtoId marker,
        NebulaEmpHazardComponent config)
    {
        if (!TrySelectPulseTile(grid, marker, out _, out var targetCoords))
            return false;

        _emp.EmpPulse(targetCoords, config.Range, config.EnergyConsumption, TimeSpan.FromSeconds(config.DisableDuration));
        Spawn(SparksPrototype, targetCoords);
        return true;
    }

    private bool TrySelectPulseTile(
        Entity<MapGridComponent, TransformComponent> grid,
        EntProtoId marker,
        out TileRef selected,
        out MapCoordinates selectedCoords)
    {
        selected = default;
        selectedCoords = default;
        var candidates = 0;

        var mapId = grid.Comp2.MapID;
        if (!TryGetNebulaMapComponent(mapId, out var mapComponent))
            return false;

        var tiles = _map.GetAllTilesEnumerator(grid.Owner, grid.Comp1, true);
        while (tiles.MoveNext(out var tile))
        {
            if (tile is not { } tileRef)
                continue;
            if (tileRef.Tile.IsEmpty)
                continue;

            var gridCoords = _map.GridTileToLocal(grid.Owner, grid.Comp1, tileRef.GridIndices);
            var coords = _transform.ToMapCoordinates(gridCoords);
            if (coords.MapId != mapId)
                continue;

            if (!NebulaQueryHelper.IsPositionInsideMarkerNebula(mapComponent, coords.Position, marker))
                continue;

            candidates++;
            if (_random.Next(candidates) != 0)
                continue;

            selected = tileRef;
            selectedCoords = coords;
        }

        return candidates > 0;
    }

    private bool TryGetNebulaMapComponent(MapId mapId, out NebulaMapComponent component)
    {
        component = default!;
        return _map.TryGetMap(mapId, out var mapUid) && TryComp(mapUid, out component!);
    }

    private void PulseSpaceTarget(
        Entity<NebulaSpaceEmpTargetComponent, TransformComponent> player,
        MapCoordinates mapCoords,
        NebulaSpaceEmpHazardComponent config)
    {
        var target = player.Comp1;
        Spawn(SparksPrototype, player.Comp2.Coordinates);
        _emp.EmpPulse(mapCoords, config.Range, config.EnergyConsumption, TimeSpan.FromSeconds(config.DisableDuration));

        target.LastPulse = _timing.CurTime;
        target.PulseCount++;
    }

    private bool IsOnNonEmptyTile(TransformComponent xform)
    {
        if (xform.GridUid is not { Valid: true } gridUid)
            return false;

        if (!TryComp<MapGridComponent>(gridUid, out var grid))
            return false;

        return _map.TryGetTileRef(gridUid, grid, xform.Coordinates, out var tileRef) &&
               !tileRef.Tile.IsEmpty;
    }
}

using System.Numerics;
using Content.Server._Exodus.Nebula.Components;
using Content.Server._Exodus.Nebula.Presence;
using Content.Server.Emp;
using Content.Shared._Exodus.Nebula.Components;
using Content.Shared._Exodus.Nebula.Generation;
using Content.Shared._Exodus.Nebula.Hazards;
using Content.Shared.GameTicking;
using Content.Shared.Maps;
using Robust.Server.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._Exodus.Nebula.Hazards;

/// <summary>
/// Drives nebula EMP effects. Iterates only entities the coordinator has tagged with
/// <see cref="NebulaEmpGridHazardComponent"/> (grids inside an EMP nebula) and
/// <see cref="NebulaSpaceEmpTargetComponent"/> (free EVA entities). Static configuration is
/// pulled from the marker prototype's
/// <see cref="Content.Shared._Exodus.Nebula.Hazards.NebulaEmpHazardComponent"/>.
/// </summary>
public sealed partial class NebulaEmpHazardSystem : EntitySystem
{
    private static readonly TimeSpan UpdateInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan PulseTileCacheRefreshInterval = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan EmptyPulseTileCacheRetryInterval = TimeSpan.FromSeconds(5);
    private const string SparksPrototype = "EffectSparks";

    [Dependency] private EmpSystem _emp = default!;
    [Dependency] private IComponentFactory _componentFactory = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPrototypeManager _prototype = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private MapSystem _map = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

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

            if (TryPulseGrid((uid, grid, xform), hazard, presence.NebulaIndex, config))
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
        NebulaEmpGridHazardComponent hazard,
        int nebulaIndex,
        NebulaEmpHazardComponent config)
    {
        if (!TrySelectPulseTile(grid, hazard, nebulaIndex, out _, out var targetCoords))
            return false;

        _emp.EmpPulse(targetCoords, config.Range, config.EnergyConsumption, TimeSpan.FromSeconds(config.DisableDuration));
        Spawn(SparksPrototype, targetCoords);
        return true;
    }

    private bool TrySelectPulseTile(
        Entity<MapGridComponent, TransformComponent> grid,
        NebulaEmpGridHazardComponent hazard,
        int nebulaIndex,
        out TileRef selected,
        out MapCoordinates selectedCoords)
    {
        selected = default;
        selectedCoords = default;

        var mapId = grid.Comp2.MapID;
        if (!TryGetNebulaMapComponent(mapId, out var mapComponent))
            return false;

        var hasPulseNebulaTarget = TryGetPulseNebulaTarget(
            mapComponent,
            hazard.Marker,
            nebulaIndex,
            out var pulseNebulaTarget);

        var rebuilt = false;
        if (!hazard.PulseTileCacheInitialized || _timing.CurTime >= hazard.NextPulseTileCacheRefresh)
        {
            RebuildPulseTileCache(
                grid,
                hazard,
                mapId,
                mapComponent,
                hasPulseNebulaTarget,
                in pulseNebulaTarget);
            rebuilt = true;
        }

        if (TryPickPulseTileFromCache(
                grid,
                hazard,
                mapId,
                mapComponent,
                hasPulseNebulaTarget,
                in pulseNebulaTarget,
                out selected,
                out selectedCoords))
            return true;

        if (rebuilt)
            return false;

        RebuildPulseTileCache(
            grid,
            hazard,
            mapId,
            mapComponent,
            hasPulseNebulaTarget,
            in pulseNebulaTarget);
        return TryPickPulseTileFromCache(
            grid,
            hazard,
            mapId,
            mapComponent,
            hasPulseNebulaTarget,
            in pulseNebulaTarget,
            out selected,
            out selectedCoords);
    }

    private void RebuildPulseTileCache(
        Entity<MapGridComponent, TransformComponent> grid,
        NebulaEmpGridHazardComponent hazard,
        MapId mapId,
        NebulaMapComponent mapComponent,
        bool hasPulseNebulaTarget,
        in PulseNebulaTarget pulseNebulaTarget)
    {
        hazard.CachedPulseTiles.Clear();

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

            if (!IsPositionInsidePulseNebula(
                    mapComponent,
                    coords.Position,
                    hazard.Marker,
                    hasPulseNebulaTarget,
                    in pulseNebulaTarget))
                continue;

            hazard.CachedPulseTiles.Add(tileRef.GridIndices);
        }

        hazard.PulseTileCacheInitialized = true;
        hazard.NextPulseTileCacheRefresh = _timing.CurTime +
            (hazard.CachedPulseTiles.Count == 0
                ? EmptyPulseTileCacheRetryInterval
                : PulseTileCacheRefreshInterval);
    }

    private bool TryPickPulseTileFromCache(
        Entity<MapGridComponent, TransformComponent> grid,
        NebulaEmpGridHazardComponent hazard,
        MapId mapId,
        NebulaMapComponent mapComponent,
        bool hasPulseNebulaTarget,
        in PulseNebulaTarget pulseNebulaTarget,
        out TileRef selected,
        out MapCoordinates selectedCoords)
    {
        selected = default;
        selectedCoords = default;

        while (hazard.CachedPulseTiles.Count > 0)
        {
            var index = _random.Next(hazard.CachedPulseTiles.Count);
            var tile = hazard.CachedPulseTiles[index];

            if (TryResolvePulseTile(
                    grid,
                    hazard,
                    mapId,
                    mapComponent,
                    hasPulseNebulaTarget,
                    in pulseNebulaTarget,
                    tile,
                    out selected,
                    out selectedCoords))
                return true;

            RemoveCachedPulseTileAt(hazard, index);
        }

        return false;
    }

    private bool TryResolvePulseTile(
        Entity<MapGridComponent, TransformComponent> grid,
        NebulaEmpGridHazardComponent hazard,
        MapId mapId,
        NebulaMapComponent mapComponent,
        bool hasPulseNebulaTarget,
        in PulseNebulaTarget pulseNebulaTarget,
        Vector2i tile,
        out TileRef selected,
        out MapCoordinates selectedCoords)
    {
        selected = default;
        selectedCoords = default;

        if (!_map.TryGetTileRef(grid.Owner, grid.Comp1, tile, out selected) || selected.Tile.IsEmpty)
            return false;

        var gridCoords = _map.GridTileToLocal(grid.Owner, grid.Comp1, tile);
        var coords = _transform.ToMapCoordinates(gridCoords);
        if (coords.MapId != mapId)
            return false;

        if (!IsPositionInsidePulseNebula(
                mapComponent,
                coords.Position,
                hazard.Marker,
                hasPulseNebulaTarget,
                in pulseNebulaTarget))
            return false;

        selectedCoords = coords;
        return true;
    }

    private static void RemoveCachedPulseTileAt(NebulaEmpGridHazardComponent hazard, int index)
    {
        var last = hazard.CachedPulseTiles.Count - 1;
        hazard.CachedPulseTiles[index] = hazard.CachedPulseTiles[last];
        hazard.CachedPulseTiles.RemoveAt(last);
    }

    private static bool IsPositionInsidePulseNebula(
        NebulaMapComponent mapComponent,
        Vector2 position,
        EntProtoId marker,
        bool hasPulseNebulaTarget,
        in PulseNebulaTarget pulseNebulaTarget)
    {
        if (hasPulseNebulaTarget)
            return pulseNebulaTarget.Contains(mapComponent, position);

        return NebulaQueryHelper.IsPositionInsideMarkerNebula(mapComponent, position, marker);
    }

    private static bool TryGetPulseNebulaTarget(
        NebulaMapComponent mapComponent,
        EntProtoId marker,
        int nebulaIndex,
        out PulseNebulaTarget target)
    {
        target = default;

        if (string.IsNullOrEmpty(marker.Id))
            return false;

        if (nebulaIndex >= 0 &&
            nebulaIndex < mapComponent.Nebulas.Count &&
            nebulaIndex < mapComponent.NebulaPrototypes.Count &&
            mapComponent.NebulaPrototypes[nebulaIndex] == marker)
        {
            target = PulseNebulaTarget.FromShape(mapComponent.Nebulas[nebulaIndex]);
            return true;
        }

        if (!mapComponent.WorldEnd.IsGenerated)
            return false;

        if (marker == mapComponent.WorldEndInnerMarker)
        {
            target = PulseNebulaTarget.FromWorldEndZone(WorldEndZone.Inner);
            return true;
        }

        if (marker == mapComponent.WorldEndOuterMarker)
        {
            target = PulseNebulaTarget.FromWorldEndZone(WorldEndZone.Outer);
            return true;
        }

        return false;
    }

    private readonly struct PulseNebulaTarget
    {
        private readonly NebulaShape _shape;
        private readonly float _shapeRotationCos;
        private readonly float _shapeRotationSin;
        private readonly float _shapeBoundingRadiusSquared;
        private readonly WorldEndZone _zone;
        private readonly bool _isWorldEnd;

        private PulseNebulaTarget(NebulaShape shape)
        {
            _shape = shape;
            _shapeRotationCos = MathF.Cos(shape.Rotation);
            _shapeRotationSin = MathF.Sin(shape.Rotation);
            _shapeBoundingRadiusSquared = shape.BoundingRadius * shape.BoundingRadius;
            _zone = default;
            _isWorldEnd = false;
        }

        private PulseNebulaTarget(WorldEndZone zone)
        {
            _shape = default;
            _shapeRotationCos = 0f;
            _shapeRotationSin = 0f;
            _shapeBoundingRadiusSquared = 0f;
            _zone = zone;
            _isWorldEnd = true;
        }

        public static PulseNebulaTarget FromShape(NebulaShape shape)
        {
            return new PulseNebulaTarget(shape);
        }

        public static PulseNebulaTarget FromWorldEndZone(WorldEndZone zone)
        {
            return new PulseNebulaTarget(zone);
        }

        public bool Contains(NebulaMapComponent mapComponent, Vector2 position)
        {
            if (_isWorldEnd)
                return mapComponent.WorldEnd.TryGetZone(position, out var zone) && zone == _zone;

            var delta = position - _shape.Center;
            if (delta.LengthSquared() > _shapeBoundingRadiusSquared)
                return false;

            var px = delta.X * _shapeRotationCos + delta.Y * _shapeRotationSin;
            var py = -delta.X * _shapeRotationSin + delta.Y * _shapeRotationCos;
            var ex = px / _shape.Stretch;
            var ey = py * _shape.Stretch;
            var theta = MathF.Atan2(ey, ex);
            var radius = _shape.GetRadius(theta);

            return radius > 0f && ex * ex + ey * ey <= radius * radius;
        }
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

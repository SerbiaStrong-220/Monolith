using System.Numerics;
using Content.Server._Exodus.Nebula.Components;
using Content.Server._Exodus.Nebula.Presence;
using Content.Server.Explosion.EntitySystems;
using Content.Shared._Crescent.ShipShields;
using Content.Shared._Exodus.Nebula.Components;
using Content.Shared._Exodus.Nebula.Generation;
using Content.Shared._Exodus.Nebula.Hazards;
using Content.Shared.Damage;
using Content.Shared.Electrocution;
using Content.Shared.Explosion;
using Content.Shared.GameTicking;
using Content.Shared.Maps;
using Robust.Server.GameObjects;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._Exodus.Nebula.Hazards;

/// <summary>
/// Drives nebula lightning effects. Iterates only entities the coordinator has tagged with
/// <see cref="NebulaLightningGridHazardComponent"/> (grids inside a lightning nebula) and
/// <see cref="NebulaSpaceLightningTargetComponent"/> (free EVA entities). Static configuration
/// is pulled from the marker prototype's
/// <see cref="Content.Shared._Exodus.Nebula.Hazards.NebulaLightningHazardComponent"/>.
/// </summary>
public sealed partial class NebulaLightningHazardSystem : EntitySystem
{
    private static readonly TimeSpan UpdateInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan StrikeTileCacheRefreshInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan EmptyStrikeTileCacheRetryInterval = TimeSpan.FromSeconds(5);
    private const float ShieldProtectionSearchRange = 32f;
    private const float LightningAudioRange = 512f;
    private const float LightningAudioVolume = 8f;
    private const string SparksPrototype = "EffectSparks";

    private static readonly Vector2i[] CardinalOffsets =
    {
        new(1, 0),
        new(-1, 0),
        new(0, 1),
        new(0, -1),
    };

    [Dependency] private DamageableSystem _damageable = default!;
    [Dependency] private ExplosionSystem _explosions = default!;
    [Dependency] private IComponentFactory _componentFactory = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IMapManager _mapManager = default!;
    [Dependency] private IPrototypeManager _prototype = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private MapSystem _map = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedElectrocutionSystem _electrocution = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    private List<Entity<MapGridComponent>> _shieldSearchBuffer = new();
    private TimeSpan _nextUpdate;

    private enum LightningStrikeTier : byte
    {
        Small,
        Heavy,
        SuperHeavy,
    }

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
        var grids = EntityQueryEnumerator<NebulaLightningGridHazardComponent>();
        while (grids.MoveNext(out var uid, out _))
            RemCompDeferred<NebulaLightningGridHazardComponent>(uid);

        var players = EntityQueryEnumerator<NebulaSpaceLightningTargetComponent>();
        while (players.MoveNext(out var uid, out _))
            RemCompDeferred<NebulaSpaceLightningTargetComponent>(uid);
    }

    private void UpdateGrids()
    {
        var query = EntityQueryEnumerator<NebulaLightningGridHazardComponent, MapGridComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var hazard, out var grid, out var xform))
        {
            // Sanity check: if presence was removed but the hazard component leaked, drop it
            // here instead of striking a grid that is no longer inside a nebula.
            if (!TryComp<NebulaPresenceComponent>(uid, out var presence) || presence.Marker != hazard.Marker)
            {
                RemCompDeferred<NebulaLightningGridHazardComponent>(uid);
                continue;
            }

            if (!NebulaQueryHelper.TryGetMarkerComponent<NebulaLightningHazardComponent>(_prototype, _componentFactory, hazard.Marker, out var config))
                continue;

            InitializeGridTimers(hazard, config);

            if (config.EnableSmall && _timing.CurTime >= hazard.NextSmallStrike)
            {
                hazard.NextSmallStrike = _timing.CurTime + config.SmallStrikeInterval;
                if (TryStrikeGrid(
                        (uid, grid, xform),
                        hazard,
                        presence.NebulaIndex,
                        config,
                        LightningStrikeTier.Small))
                    RecordStrike(hazard, LightningStrikeTier.Small);
            }

            if (config.EnableHeavy && _timing.CurTime >= hazard.NextHeavyStrike)
            {
                hazard.NextHeavyStrike = _timing.CurTime + config.HeavyStrikeInterval;
                if (TryStrikeGrid(
                        (uid, grid, xform),
                        hazard,
                        presence.NebulaIndex,
                        config,
                        LightningStrikeTier.Heavy))
                    RecordStrike(hazard, LightningStrikeTier.Heavy);
            }

            if (config.EnableSuperHeavy && _timing.CurTime >= hazard.NextSuperHeavyStrike)
            {
                hazard.NextSuperHeavyStrike = _timing.CurTime + config.SuperHeavyStrikeInterval;
                if (TryStrikeGrid(
                        (uid, grid, xform),
                        hazard,
                        presence.NebulaIndex,
                        config,
                        LightningStrikeTier.SuperHeavy))
                    RecordStrike(hazard, LightningStrikeTier.SuperHeavy);
            }
        }
    }

    private void UpdateSpaceTargets()
    {
        var query = EntityQueryEnumerator<NebulaSpaceLightningTargetComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var target, out var xform))
        {
            // Resolve marker via presence each tick so transitions between nebula kinds
            // (e.g. death-zone inner -> outer at the 90k boundary) take effect immediately.
            if (!TryComp<NebulaPresenceComponent>(uid, out var presence) ||
                !NebulaQueryHelper.TryGetMarkerComponent<NebulaSpaceLightningHazardComponent>(_prototype, _componentFactory, presence.Marker, out var config))
            {
                RemCompDeferred<NebulaSpaceLightningTargetComponent>(uid);
                continue;
            }

            if (IsOnNonEmptyTile(xform))
                continue;

            if (target.NextStrike == TimeSpan.Zero)
            {
                ScheduleNextSpaceStrike(target, config);
                continue;
            }

            if (_timing.CurTime < target.NextStrike)
                continue;

            StrikeSpaceTarget((uid, target, xform), _transform.GetMapCoordinates(uid, xform), config);
            ScheduleNextSpaceStrike(target, config);
        }
    }

    // TryGetMarkerConfig / TryGetSpaceMarkerConfig replaced by NebulaQueryHelper.TryGetMarkerComponent<T>.

    private void InitializeGridTimers(NebulaLightningGridHazardComponent hazard, NebulaLightningHazardComponent config)
    {
        if (hazard.TimersInitialized)
            return;

        hazard.TimersInitialized = true;
        if (config.EnableSmall)
            hazard.NextSmallStrike = _timing.CurTime + config.SmallStrikeInterval;
        if (config.EnableHeavy)
            hazard.NextHeavyStrike = _timing.CurTime + config.HeavyStrikeInterval;
        if (config.EnableSuperHeavy)
            hazard.NextSuperHeavyStrike = _timing.CurTime + config.SuperHeavyStrikeInterval;
    }

    private void RecordStrike(NebulaLightningGridHazardComponent hazard, LightningStrikeTier tier)
    {
        if (tier == LightningStrikeTier.SuperHeavy)
        {
            if (hazard.LastSuperHeavyStrike != TimeSpan.Zero)
                hazard.LastSuperHeavyDelta = _timing.CurTime - hazard.LastSuperHeavyStrike;
            hazard.LastSuperHeavyStrike = _timing.CurTime;
            hazard.SuperHeavyStrikeCount++;
            return;
        }

        if (tier == LightningStrikeTier.Heavy)
        {
            if (hazard.LastHeavyStrike != TimeSpan.Zero)
                hazard.LastHeavyDelta = _timing.CurTime - hazard.LastHeavyStrike;
            hazard.LastHeavyStrike = _timing.CurTime;
            hazard.HeavyStrikeCount++;
            return;
        }

        if (hazard.LastSmallStrike != TimeSpan.Zero)
            hazard.LastSmallDelta = _timing.CurTime - hazard.LastSmallStrike;
        hazard.LastSmallStrike = _timing.CurTime;
        hazard.SmallStrikeCount++;
    }

    private bool TryStrikeGrid(
        Entity<MapGridComponent, TransformComponent> grid,
        NebulaLightningGridHazardComponent hazard,
        int nebulaIndex,
        NebulaLightningHazardComponent config,
        LightningStrikeTier tier)
    {
        if (!TrySelectStrikeTile(grid, hazard, nebulaIndex, out _, out var targetCoords, out var targetGridCoords))
            return false;

        var lightning = GetLightningPrototype(config, tier);
        var lightningLength = GetLightningLength(config, tier);
        var sourceDirection = targetCoords.Position - _transform.GetWorldPosition(grid.Comp2);
        SpawnLightning(targetCoords, lightning, lightningLength, sourceDirection);

        var shieldLoad = GetShieldLoad(config, tier);
        var shieldHit = new ShipShieldHitAttemptEvent(targetCoords, shieldLoad, false);
        RaiseLocalEvent(grid.Owner, ref shieldHit);
        if (shieldHit.Absorbed)
        {
            PlayLightningSound(config.ShieldImpactSound, targetGridCoords);
            Spawn(SparksPrototype, targetCoords);
            return true;
        }

        var impactSound = GetImpactSound(config, tier);
        QueueExplosion(targetGridCoords, config, tier);
        PlayLightningSound(impactSound, targetGridCoords);
        Spawn(SparksPrototype, targetCoords);
        return true;
    }

    private bool TrySelectStrikeTile(
        Entity<MapGridComponent, TransformComponent> grid,
        NebulaLightningGridHazardComponent hazard,
        int nebulaIndex,
        out TileRef selected,
        out MapCoordinates selectedCoords,
        out EntityCoordinates selectedGridCoords)
    {
        selected = default;
        selectedCoords = default;
        selectedGridCoords = default;

        var mapId = grid.Comp2.MapID;
        if (!TryGetNebulaMapComponent(mapId, out var mapComponent))
            return false;

        var hasStrikeNebulaTarget = TryGetStrikeNebulaTarget(
            mapComponent,
            hazard.Marker,
            nebulaIndex,
            out var strikeNebulaTarget);

        var rebuilt = false;
        if (!hazard.StrikeTileCacheInitialized || _timing.CurTime >= hazard.NextStrikeTileCacheRefresh)
        {
            RebuildStrikeTileCache(
                grid,
                hazard,
                mapId,
                mapComponent,
                hasStrikeNebulaTarget,
                in strikeNebulaTarget);
            rebuilt = true;
        }

        if (TryPickStrikeTileFromCache(
                grid,
                hazard,
                mapId,
                mapComponent,
                hasStrikeNebulaTarget,
                in strikeNebulaTarget,
                out selected,
                out selectedCoords,
                out selectedGridCoords))
            return true;

        if (rebuilt)
            return false;

        RebuildStrikeTileCache(
            grid,
            hazard,
            mapId,
            mapComponent,
            hasStrikeNebulaTarget,
            in strikeNebulaTarget);
        return TryPickStrikeTileFromCache(
            grid,
            hazard,
            mapId,
            mapComponent,
            hasStrikeNebulaTarget,
            in strikeNebulaTarget,
            out selected,
            out selectedCoords,
            out selectedGridCoords);
    }

    private void RebuildStrikeTileCache(
        Entity<MapGridComponent, TransformComponent> grid,
        NebulaLightningGridHazardComponent hazard,
        MapId mapId,
        NebulaMapComponent mapComponent,
        bool hasStrikeNebulaTarget,
        in StrikeNebulaTarget strikeNebulaTarget)
    {
        hazard.CachedStrikeTiles.Clear();

        var tiles = _map.GetAllTilesEnumerator(grid.Owner, grid.Comp1, true);
        while (tiles.MoveNext(out var tile))
        {
            if (tile is not { } tileRef)
                continue;
            if (tileRef.Tile.IsEmpty || !IsEdgeTile(grid.Owner, grid.Comp1, tileRef.GridIndices))
                continue;

            var gridCoords = _map.GridTileToLocal(grid.Owner, grid.Comp1, tileRef.GridIndices);
            var coords = _transform.ToMapCoordinates(gridCoords);
            if (coords.MapId != mapId)
                continue;

            // The grid may overlap a nebula only partially; only fire on tiles that are
            // actually inside a nebula whose marker matches this hazard.
            if (!IsPositionInsideStrikeNebula(
                    mapComponent,
                    coords.Position,
                    hazard.Marker,
                    hasStrikeNebulaTarget,
                    in strikeNebulaTarget))
                continue;

            hazard.CachedStrikeTiles.Add(tileRef.GridIndices);
        }

        hazard.StrikeTileCacheInitialized = true;
        hazard.NextStrikeTileCacheRefresh = _timing.CurTime +
            (hazard.CachedStrikeTiles.Count == 0
                ? EmptyStrikeTileCacheRetryInterval
                : StrikeTileCacheRefreshInterval);
    }

    private bool TryPickStrikeTileFromCache(
        Entity<MapGridComponent, TransformComponent> grid,
        NebulaLightningGridHazardComponent hazard,
        MapId mapId,
        NebulaMapComponent mapComponent,
        bool hasStrikeNebulaTarget,
        in StrikeNebulaTarget strikeNebulaTarget,
        out TileRef selected,
        out MapCoordinates selectedCoords,
        out EntityCoordinates selectedGridCoords)
    {
        selected = default;
        selectedCoords = default;
        selectedGridCoords = default;

        while (hazard.CachedStrikeTiles.Count > 0)
        {
            var index = _random.Next(hazard.CachedStrikeTiles.Count);
            var tile = hazard.CachedStrikeTiles[index];

            if (TryResolveStrikeTile(
                    grid,
                    hazard,
                    mapId,
                    mapComponent,
                    hasStrikeNebulaTarget,
                    in strikeNebulaTarget,
                    tile,
                    out selected,
                    out selectedCoords,
                    out selectedGridCoords))
                return true;

            RemoveCachedStrikeTileAt(hazard, index);
        }

        return false;
    }

    private bool TryResolveStrikeTile(
        Entity<MapGridComponent, TransformComponent> grid,
        NebulaLightningGridHazardComponent hazard,
        MapId mapId,
        NebulaMapComponent mapComponent,
        bool hasStrikeNebulaTarget,
        in StrikeNebulaTarget strikeNebulaTarget,
        Vector2i tile,
        out TileRef selected,
        out MapCoordinates selectedCoords,
        out EntityCoordinates selectedGridCoords)
    {
        selected = default;
        selectedCoords = default;
        selectedGridCoords = default;

        if (!_map.TryGetTileRef(grid.Owner, grid.Comp1, tile, out selected) ||
            selected.Tile.IsEmpty ||
            !IsEdgeTile(grid.Owner, grid.Comp1, tile))
        {
            return false;
        }

        var gridCoords = _map.GridTileToLocal(grid.Owner, grid.Comp1, tile);
        var coords = _transform.ToMapCoordinates(gridCoords);
        if (coords.MapId != mapId)
            return false;

        if (!IsPositionInsideStrikeNebula(
                mapComponent,
                coords.Position,
                hazard.Marker,
                hasStrikeNebulaTarget,
                in strikeNebulaTarget))
            return false;

        selectedCoords = coords;
        selectedGridCoords = gridCoords;
        return true;
    }

    private static void RemoveCachedStrikeTileAt(NebulaLightningGridHazardComponent hazard, int index)
    {
        var last = hazard.CachedStrikeTiles.Count - 1;
        hazard.CachedStrikeTiles[index] = hazard.CachedStrikeTiles[last];
        hazard.CachedStrikeTiles.RemoveAt(last);
    }

    private static bool IsPositionInsideStrikeNebula(
        NebulaMapComponent mapComponent,
        Vector2 position,
        EntProtoId marker,
        bool hasStrikeNebulaTarget,
        in StrikeNebulaTarget strikeNebulaTarget)
    {
        if (hasStrikeNebulaTarget)
            return strikeNebulaTarget.Contains(mapComponent, position);

        return NebulaQueryHelper.IsPositionInsideMarkerNebula(mapComponent, position, marker);
    }

    private static bool TryGetStrikeNebulaTarget(
        NebulaMapComponent mapComponent,
        EntProtoId marker,
        int nebulaIndex,
        out StrikeNebulaTarget target)
    {
        target = default;

        if (string.IsNullOrEmpty(marker.Id))
            return false;

        if (nebulaIndex >= 0 &&
            nebulaIndex < mapComponent.Nebulas.Count &&
            nebulaIndex < mapComponent.NebulaPrototypes.Count &&
            mapComponent.NebulaPrototypes[nebulaIndex] == marker)
        {
            target = StrikeNebulaTarget.FromShape(mapComponent.Nebulas[nebulaIndex]);
            return true;
        }

        if (!mapComponent.WorldEnd.IsGenerated)
            return false;

        if (marker == mapComponent.WorldEndInnerMarker)
        {
            target = StrikeNebulaTarget.FromWorldEndZone(WorldEndZone.Inner);
            return true;
        }

        if (marker == mapComponent.WorldEndOuterMarker)
        {
            target = StrikeNebulaTarget.FromWorldEndZone(WorldEndZone.Outer);
            return true;
        }

        return false;
    }

    private readonly struct StrikeNebulaTarget
    {
        private readonly NebulaShape _shape;
        private readonly float _shapeRotationCos;
        private readonly float _shapeRotationSin;
        private readonly float _shapeBoundingRadiusSquared;
        private readonly WorldEndZone _zone;
        private readonly bool _isWorldEnd;

        private StrikeNebulaTarget(NebulaShape shape)
        {
            _shape = shape;
            _shapeRotationCos = MathF.Cos(shape.Rotation);
            _shapeRotationSin = MathF.Sin(shape.Rotation);
            _shapeBoundingRadiusSquared = shape.BoundingRadius * shape.BoundingRadius;
            _zone = default;
            _isWorldEnd = false;
        }

        private StrikeNebulaTarget(WorldEndZone zone)
        {
            _shape = default;
            _shapeRotationCos = 0f;
            _shapeRotationSin = 0f;
            _shapeBoundingRadiusSquared = 0f;
            _zone = zone;
            _isWorldEnd = true;
        }

        public static StrikeNebulaTarget FromShape(NebulaShape shape)
        {
            return new StrikeNebulaTarget(shape);
        }

        public static StrikeNebulaTarget FromWorldEndZone(WorldEndZone zone)
        {
            return new StrikeNebulaTarget(zone);
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

    // IsPositionInsideMarkerNebula moved to NebulaQueryHelper.

    private bool IsEdgeTile(EntityUid gridUid, MapGridComponent grid, Vector2i tile)
    {
        for (var i = 0; i < CardinalOffsets.Length; i++)
        {
            var neighbor = tile + CardinalOffsets[i];
            if (!_map.TryGetTileRef(gridUid, grid, neighbor, out var tileRef) || tileRef.Tile.IsEmpty)
                return true;
        }

        return false;
    }

    private void SpawnLightning(MapCoordinates targetCoords, EntProtoId lightningPrototype, float length, Vector2 sourceDirection)
    {
        var direction = sourceDirection.LengthSquared() > 0.01f
            ? Vector2.Normalize(sourceDirection)
            : _random.NextAngle().ToWorldVec();

        var visual = Spawn(lightningPrototype, targetCoords);
        _transform.SetWorldRotation(visual, direction.ToWorldAngle() - Angle.FromDegrees(90));
    }

    private void PlayLightningSound(SoundSpecifier sound, EntityCoordinates coordinates)
    {
        PlayLightningSound(sound, coordinates, LightningAudioRange, LightningAudioVolume);
    }

    private void PlayLightningSound(SoundSpecifier sound, EntityCoordinates coordinates, float range, float volume)
    {
        var mapCoords = _transform.ToMapCoordinates(coordinates);
        var filter = Filter.Pvs(mapCoords).AddInRange(mapCoords, range);
        var audioParams = sound.Params
            .AddVolume(volume)
            .WithMaxDistance(range)
            .WithRolloffFactor(0f);

        _audio.PlayStatic(sound, filter, coordinates, true, audioParams);
    }

    private void QueueExplosion(EntityCoordinates targetCoords, NebulaLightningHazardComponent config, LightningStrikeTier tier)
    {
        var explosionType = GetExplosionType(config, tier);
        var totalIntensity = GetExplosionTotalIntensity(config, tier);
        var slope = GetExplosionIntensitySlope(config, tier);
        var maxTileIntensity = GetExplosionMaxTileIntensity(config, tier);

        var mapCoords = _transform.ToMapCoordinates(targetCoords);
        _explosions.QueueExplosion(
            mapCoords,
            explosionType.ToString(),
            totalIntensity,
            slope,
            maxTileIntensity,
            cause: null,
            addLog: false);
    }

    private static EntProtoId GetLightningPrototype(NebulaLightningHazardComponent config, LightningStrikeTier tier)
    {
        return tier switch
        {
            LightningStrikeTier.SuperHeavy => config.SuperHeavyLightningPrototype,
            LightningStrikeTier.Heavy => config.HeavyLightningPrototype,
            _ => config.SmallLightningPrototype,
        };
    }

    private static float GetLightningLength(NebulaLightningHazardComponent config, LightningStrikeTier tier)
    {
        return tier switch
        {
            LightningStrikeTier.SuperHeavy => config.SuperHeavyLightningLength,
            LightningStrikeTier.Heavy => config.HeavyLightningLength,
            _ => config.SmallLightningLength,
        };
    }

    private static float GetShieldLoad(NebulaLightningHazardComponent config, LightningStrikeTier tier)
    {
        return tier switch
        {
            LightningStrikeTier.SuperHeavy => config.SuperHeavyShieldLoad,
            LightningStrikeTier.Heavy => config.HeavyShieldLoad,
            _ => config.SmallShieldLoad,
        };
    }

    private static ProtoId<ExplosionPrototype> GetExplosionType(NebulaLightningHazardComponent config, LightningStrikeTier tier)
    {
        return tier switch
        {
            LightningStrikeTier.SuperHeavy => config.SuperHeavyExplosionType,
            LightningStrikeTier.Heavy => config.HeavyExplosionType,
            _ => config.SmallExplosionType,
        };
    }

    private static float GetExplosionTotalIntensity(NebulaLightningHazardComponent config, LightningStrikeTier tier)
    {
        return tier switch
        {
            LightningStrikeTier.SuperHeavy => config.SuperHeavyExplosionTotalIntensity,
            LightningStrikeTier.Heavy => config.HeavyExplosionTotalIntensity,
            _ => config.SmallExplosionTotalIntensity,
        };
    }

    private static float GetExplosionIntensitySlope(NebulaLightningHazardComponent config, LightningStrikeTier tier)
    {
        return tier switch
        {
            LightningStrikeTier.SuperHeavy => config.SuperHeavyExplosionIntensitySlope,
            LightningStrikeTier.Heavy => config.HeavyExplosionIntensitySlope,
            _ => config.SmallExplosionIntensitySlope,
        };
    }

    private static float GetExplosionMaxTileIntensity(NebulaLightningHazardComponent config, LightningStrikeTier tier)
    {
        return tier switch
        {
            LightningStrikeTier.SuperHeavy => config.SuperHeavyExplosionMaxTileIntensity,
            LightningStrikeTier.Heavy => config.HeavyExplosionMaxTileIntensity,
            _ => config.SmallExplosionMaxTileIntensity,
        };
    }

    private static SoundSpecifier GetImpactSound(NebulaLightningHazardComponent config, LightningStrikeTier tier)
    {
        return tier switch
        {
            LightningStrikeTier.SuperHeavy => config.SuperHeavyImpactSound,
            LightningStrikeTier.Heavy => config.HeavyImpactSound,
            _ => config.SmallImpactSound,
        };
    }

    private void StrikeSpaceTarget(
        Entity<NebulaSpaceLightningTargetComponent, TransformComponent> player,
        MapCoordinates mapCoords,
        NebulaSpaceLightningHazardComponent config)
    {
        var target = player.Comp1;
        SpawnLightning(mapCoords, config.LightningPrototype, config.LightningLength, _random.NextAngle().ToWorldVec());
        Spawn(SparksPrototype, player.Comp2.Coordinates);

        if (TryAbsorbSpaceStrike(mapCoords, config.ShieldLoad))
        {
            PlayLightningSound(config.ShieldImpactSound, player.Comp2.Coordinates, config.ImpactSoundRange, config.ImpactSoundVolume);
            target.LastStrike = _timing.CurTime;
            target.StrikeCount++;
            return;
        }

        PlayLightningSound(config.ImpactSound, player.Comp2.Coordinates, config.ImpactSoundRange, config.ImpactSoundVolume);
        _damageable.TryChangeDamage(player.Owner, config.BurnDamage);
        _electrocution.TryDoElectrocution(player.Owner, null, config.ShockDamage, config.ShockTime, true);

        target.LastStrike = _timing.CurTime;
        target.StrikeCount++;
    }

    private bool TryAbsorbSpaceStrike(MapCoordinates mapCoords, float shieldLoad)
    {
        var rangeVector = new Vector2(ShieldProtectionSearchRange, ShieldProtectionSearchRange);
        _shieldSearchBuffer.Clear();
        _mapManager.FindGridsIntersecting(
            mapCoords.MapId,
            new Box2(mapCoords.Position - rangeVector, mapCoords.Position + rangeVector),
            ref _shieldSearchBuffer,
            approx: true,
            includeMap: false);

        for (var i = 0; i < _shieldSearchBuffer.Count; i++)
        {
            var shieldHit = new ShipShieldHitAttemptEvent(mapCoords, shieldLoad, false);
            RaiseLocalEvent(_shieldSearchBuffer[i].Owner, ref shieldHit);
            if (shieldHit.Absorbed)
                return true;
        }

        return false;
    }

    private void ScheduleNextSpaceStrike(NebulaSpaceLightningTargetComponent target, NebulaSpaceLightningHazardComponent config)
    {
        var min = Math.Max(1, Math.Min(config.MinStrikeDelaySeconds, config.MaxStrikeDelaySeconds));
        var max = Math.Max(min, Math.Max(config.MinStrikeDelaySeconds, config.MaxStrikeDelaySeconds));
        target.NextStrike = _timing.CurTime + TimeSpan.FromSeconds(_random.Next(min, max + 1));
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

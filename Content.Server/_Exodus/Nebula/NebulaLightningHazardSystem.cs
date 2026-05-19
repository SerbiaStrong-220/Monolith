using System.Numerics;
using Content.Server.Explosion.EntitySystems;
using Content.Shared._Crescent.ShipShields;
using Content.Shared._Exodus.Nebula;
using Content.Shared.Damage;
using Content.Shared.Electrocution;
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

namespace Content.Server._Exodus.Nebula;

/// <summary>
/// Drives nebula lightning effects. Iterates only entities the coordinator has tagged with
/// <see cref="NebulaLightningGridHazardComponent"/> (grids inside a lightning nebula) and
/// <see cref="NebulaSpaceLightningTargetComponent"/> (free EVA entities). Static configuration
/// is pulled from the marker prototype's
/// <see cref="Content.Shared._Exodus.Nebula.NebulaLightningHazardComponent"/>.
/// </summary>
public sealed class NebulaLightningHazardSystem : EntitySystem
{
    private static readonly TimeSpan UpdateInterval = TimeSpan.FromSeconds(1);
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

    [Dependency] private readonly DamageableSystem _damageable = default!;
    [Dependency] private readonly ExplosionSystem _explosions = default!;
    [Dependency] private readonly IComponentFactory _componentFactory = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IMapManager _mapManager = default!;
    [Dependency] private readonly IPrototypeManager _prototype = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly MapSystem _map = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly SharedElectrocutionSystem _electrocution = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;

    private List<Entity<MapGridComponent>> _shieldSearchBuffer = new();
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

            if (!TryGetMarkerConfig(hazard.Marker, out var config))
                continue;

            InitializeGridTimers(hazard, config);

            if (config.EnableSmall && _timing.CurTime >= hazard.NextSmallStrike)
            {
                hazard.NextSmallStrike = _timing.CurTime + config.SmallStrikeInterval;
                if (TryStrikeGrid((uid, grid, xform), hazard, config, false))
                    RecordStrike(hazard, false);
            }

            if (config.EnableHeavy && _timing.CurTime >= hazard.NextHeavyStrike)
            {
                hazard.NextHeavyStrike = _timing.CurTime + config.HeavyStrikeInterval;
                if (TryStrikeGrid((uid, grid, xform), hazard, config, true))
                    RecordStrike(hazard, true);
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
                !TryGetSpaceMarkerConfig(presence.Marker, out var config))
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

    private bool TryGetMarkerConfig(EntProtoId marker, out NebulaLightningHazardComponent config)
    {
        config = default!;
        if (string.IsNullOrEmpty(marker.Id))
            return false;
        if (!_prototype.TryIndex<EntityPrototype>(marker, out var proto))
            return false;
        return proto.TryGetComponent<NebulaLightningHazardComponent>(out config!, _componentFactory);
    }

    private bool TryGetSpaceMarkerConfig(EntProtoId marker, out NebulaSpaceLightningHazardComponent config)
    {
        config = default!;
        if (string.IsNullOrEmpty(marker.Id))
            return false;
        if (!_prototype.TryIndex<EntityPrototype>(marker, out var proto))
            return false;
        return proto.TryGetComponent<NebulaSpaceLightningHazardComponent>(out config!, _componentFactory);
    }

    private void InitializeGridTimers(NebulaLightningGridHazardComponent hazard, NebulaLightningHazardComponent config)
    {
        if (hazard.TimersInitialized)
            return;

        hazard.TimersInitialized = true;
        if (config.EnableSmall)
            hazard.NextSmallStrike = _timing.CurTime + config.SmallStrikeInterval;
        if (config.EnableHeavy)
            hazard.NextHeavyStrike = _timing.CurTime + config.HeavyStrikeInterval;
    }

    private void RecordStrike(NebulaLightningGridHazardComponent hazard, bool heavy)
    {
        if (heavy)
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
        NebulaLightningHazardComponent config,
        bool heavy)
    {
        if (!TrySelectStrikeTile(grid, hazard.Marker, out _, out var targetCoords, out var targetGridCoords))
            return false;

        var lightning = heavy ? config.HeavyLightningPrototype : config.SmallLightningPrototype;
        var lightningLength = heavy ? config.HeavyLightningLength : config.SmallLightningLength;
        var sourceDirection = targetCoords.Position - _transform.GetWorldPosition(grid.Comp2);
        SpawnLightning(targetCoords, lightning, lightningLength, sourceDirection);

        var shieldLoad = heavy ? config.HeavyShieldLoad : config.SmallShieldLoad;
        var shieldHit = new ShipShieldHitAttemptEvent(targetCoords, shieldLoad, false);
        RaiseLocalEvent(grid.Owner, ref shieldHit);
        if (shieldHit.Absorbed)
        {
            PlayLightningSound(config.ShieldImpactSound, targetGridCoords);
            Spawn(SparksPrototype, targetCoords);
            return true;
        }

        var impactSound = heavy ? config.HeavyImpactSound : config.SmallImpactSound;
        QueueExplosion(targetGridCoords, config, heavy);
        PlayLightningSound(impactSound, targetGridCoords);
        Spawn(SparksPrototype, targetCoords);
        return true;
    }

    private bool TrySelectStrikeTile(
        Entity<MapGridComponent, TransformComponent> grid,
        EntProtoId marker,
        out TileRef selected,
        out MapCoordinates selectedCoords,
        out EntityCoordinates selectedGridCoords)
    {
        selected = default;
        selectedCoords = default;
        selectedGridCoords = default;
        var candidates = 0;

        var mapId = grid.Comp2.MapID;
        if (!TryGetNebulaMapComponent(mapId, out var mapComponent))
            return false;

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
            if (!IsPositionInsideMarkerNebula(coords.Position, mapComponent, marker))
                continue;

            candidates++;
            if (_random.Next(candidates) != 0)
                continue;

            selected = tileRef;
            selectedCoords = coords;
            selectedGridCoords = gridCoords;
        }

        return candidates > 0;
    }

    private bool TryGetNebulaMapComponent(MapId mapId, out NebulaMapComponent component)
    {
        component = default!;
        return _map.TryGetMap(mapId, out var mapUid) && TryComp(mapUid, out component!);
    }

    private static bool IsPositionInsideMarkerNebula(Vector2 position, NebulaMapComponent mapComponent, EntProtoId marker)
    {
        for (var i = 0; i < mapComponent.Nebulas.Count; i++)
        {
            if (i >= mapComponent.NebulaPrototypes.Count || mapComponent.NebulaPrototypes[i] != marker)
                continue;

            var nebula = mapComponent.Nebulas[i];
            var delta = position - nebula.Center;
            if (delta.LengthSquared() > nebula.BoundingRadius * nebula.BoundingRadius)
                continue;

            if (nebula.Contains(position))
                return true;
        }

        // Death zone is split into concentric sub-zones; only the matching marker's zone
        // counts as "inside" — an outer-zone hazard must not pick a tile that is actually
        // in the inner zone, and vice versa.
        var isInner = mapComponent.WorldEndInnerMarker == marker;
        var isOuter = mapComponent.WorldEndOuterMarker == marker;
        if ((isInner || isOuter) &&
            mapComponent.WorldEnd.IsGenerated &&
            mapComponent.WorldEnd.TryGetZone(position, out var zone))
        {
            return isInner ? zone == WorldEndZone.Inner : zone == WorldEndZone.Outer;
        }

        return false;
    }

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

    private void QueueExplosion(EntityCoordinates targetCoords, NebulaLightningHazardComponent config, bool heavy)
    {
        var explosionType = heavy ? config.HeavyExplosionType : config.SmallExplosionType;
        var totalIntensity = heavy ? config.HeavyExplosionTotalIntensity : config.SmallExplosionTotalIntensity;
        var slope = heavy ? config.HeavyExplosionIntensitySlope : config.SmallExplosionIntensitySlope;
        var maxTileIntensity = heavy ? config.HeavyExplosionMaxTileIntensity : config.SmallExplosionMaxTileIntensity;

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
        var tileRef = xform.Coordinates.GetTileRef(EntityManager, _mapManager);
        return tileRef is { Tile.IsEmpty: false };
    }
}

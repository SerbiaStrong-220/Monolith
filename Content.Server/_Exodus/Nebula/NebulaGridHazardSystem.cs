using System.Numerics;
using Content.Server._Crescent.ShipShields;
using Content.Server.Explosion.EntitySystems;
using Content.Server.GameTicking;
using Content.Shared._Exodus.Nebula;
using Content.Shared.Damage;
using Content.Shared.Electrocution;
using Content.Shared.GameTicking;
using Content.Shared.Ghost;
using Content.Shared.Maps;
using Content.Shared.Mobs.Systems;
using Robust.Server.GameObjects;
using Robust.Server.Player;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Enums;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._Exodus.Nebula;

public sealed class NebulaGridHazardSystem : EntitySystem
{
    private static readonly TimeSpan UpdateInterval = TimeSpan.FromSeconds(1);
    private static readonly Vector2i[] CardinalOffsets =
    {
        new(1, 0),
        new(-1, 0),
        new(0, 1),
        new(0, -1),
    };

    private static readonly TimeSpan SmallStrikeInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan HeavyStrikeInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan LongTestSmallStrikeInterval = TimeSpan.FromSeconds(50);
    private static readonly TimeSpan LongTestHeavyStrikeInterval = TimeSpan.FromSeconds(300);
    private static readonly TimeSpan OldTestSmallStrikeInterval = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan OldTestHeavyStrikeInterval = TimeSpan.FromSeconds(120);
    private const string LegacySmallLightningPrototype = "NebulaRedSmallLightning";
    private const string LegacyHeavyLightningPrototype = "NebulaRedHeavyLightning";
    private const string SmallLightningPrototype = "NebulaRedSmallStrikeVisual";
    private const string HeavyLightningPrototype = "NebulaRedHeavyStrikeVisual";
    private const float SmallExplosionTotalIntensity = 133.333f;
    private const float SmallExplosionMaxTileIntensity = 40f;
    private const float HeavyExplosionTotalIntensity = 1066.667f;
    private const float HeavyExplosionMaxTileIntensity = 80f;
    private const float DefaultPlayerRange = 32f;
    private const float ShieldProtectionSearchRange = 32f;
    private const float LightningAudioRange = 512f;
    private const float LightningAudioVolume = 8f;
    private const string SparksPrototype = "EffectSparks";

    [Dependency] private readonly ExplosionSystem _explosions = default!;
    [Dependency] private readonly GameTicker _ticker = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IMapManager _mapManager = default!;
    [Dependency] private readonly IPlayerManager _player = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly DamageableSystem _damageable = default!;
    [Dependency] private readonly MapSystem _map = default!;
    [Dependency] private readonly MobStateSystem _mobState = default!;
    [Dependency] private readonly NebulaPresenceSystem _presence = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly SharedElectrocutionSystem _electrocution = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly ShipShieldsSystem _shields = default!;

    private readonly HashSet<EntityUid> _activeHazardGrids = new();
    private List<Entity<MapGridComponent>> _nearbyGrids = new();
    private TimeSpan _nextUpdate;

    public override void Initialize()
    {
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestart);
    }

    public override void Update(float frameTime)
    {
        if (_timing.CurTime < _nextUpdate)
            return;

        _nextUpdate = _timing.CurTime + UpdateInterval;

        if (!TryGetNebulaMap(out var mapId, out var mapComponent))
        {
            ClearHazards();
            return;
        }

        _activeHazardGrids.Clear();
        UpdatePlayerHazards(mapId, mapComponent);
        UpdateActiveGridHazards(mapId, mapComponent);
        ClearInactiveGridHazards();
    }

    private void OnRoundRestart(RoundRestartCleanupEvent args)
    {
        ClearHazards();
    }

    private void InitializeTimers(NebulaGridHazardComponent hazard)
    {
        if (hazard.TimersInitialized)
            return;

        hazard.TimersInitialized = true;
        hazard.NextSmallStrike = _timing.CurTime + hazard.SmallStrikeInterval;
        hazard.NextHeavyStrike = _timing.CurTime + hazard.HeavyStrikeInterval;
    }

    private void UpdateLegacyHazardSettings(NebulaGridHazardComponent hazard)
    {
        if (hazard.SmallLightningPrototype.ToString() == LegacySmallLightningPrototype)
            hazard.SmallLightningPrototype = SmallLightningPrototype;

        if (hazard.HeavyLightningPrototype.ToString() == LegacyHeavyLightningPrototype)
            hazard.HeavyLightningPrototype = HeavyLightningPrototype;

        if (hazard.SmallStrikeInterval == LongTestSmallStrikeInterval ||
            hazard.SmallStrikeInterval == OldTestSmallStrikeInterval)
        {
            hazard.SmallStrikeInterval = SmallStrikeInterval;
        }

        if (hazard.HeavyStrikeInterval == LongTestHeavyStrikeInterval ||
            hazard.HeavyStrikeInterval == OldTestHeavyStrikeInterval)
        {
            hazard.HeavyStrikeInterval = HeavyStrikeInterval;
        }

        if (hazard.TimersInitialized)
        {
            var nextSmallLimit = _timing.CurTime + hazard.SmallStrikeInterval;
            if (hazard.NextSmallStrike > nextSmallLimit)
                hazard.NextSmallStrike = nextSmallLimit;

            var nextHeavyLimit = _timing.CurTime + hazard.HeavyStrikeInterval;
            if (hazard.NextHeavyStrike > nextHeavyLimit)
                hazard.NextHeavyStrike = nextHeavyLimit;
        }

        if (MathHelper.CloseTo(hazard.SmallExplosionTotalIntensity, 200f))
            hazard.SmallExplosionTotalIntensity = SmallExplosionTotalIntensity;

        if (MathHelper.CloseTo(hazard.SmallExplosionMaxTileIntensity, 60f))
            hazard.SmallExplosionMaxTileIntensity = SmallExplosionMaxTileIntensity;

        if (MathHelper.CloseTo(hazard.HeavyExplosionTotalIntensity, 1600f))
            hazard.HeavyExplosionTotalIntensity = HeavyExplosionTotalIntensity;

        if (MathHelper.CloseTo(hazard.HeavyExplosionMaxTileIntensity, 120f))
            hazard.HeavyExplosionMaxTileIntensity = HeavyExplosionMaxTileIntensity;
    }

    private void UpdateHazard(
        Entity<MapGridComponent, TransformComponent, NebulaGridHazardComponent> grid,
        MapId mapId,
        NebulaMapComponent mapComponent)
    {
        var hazard = grid.Comp3;
        if (_timing.CurTime >= hazard.NextSmallStrike)
        {
            hazard.NextSmallStrike = _timing.CurTime + hazard.SmallStrikeInterval;

            if (TryStrikeGridSafely(grid, mapId, mapComponent, false))
                RecordStrike(hazard, false);
        }

        if (_timing.CurTime >= hazard.NextHeavyStrike)
        {
            hazard.NextHeavyStrike = _timing.CurTime + hazard.HeavyStrikeInterval;

            if (TryStrikeGridSafely(grid, mapId, mapComponent, true))
                RecordStrike(hazard, true);
        }
    }

    private bool TryStrikeGridSafely(
        Entity<MapGridComponent, TransformComponent, NebulaGridHazardComponent> grid,
        MapId mapId,
        NebulaMapComponent mapComponent,
        bool heavy)
    {
        try
        {
            return TryStrikeGrid(grid, mapId, mapComponent, heavy);
        }
        catch (Exception ex)
        {
            Logger.ErrorS("nebula", $"Failed to process red nebula lightning strike on {ToPrettyString(grid.Owner)}: {ex}");
            return false;
        }
    }

    private void RecordStrike(NebulaGridHazardComponent hazard, bool heavy)
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
        Entity<MapGridComponent, TransformComponent, NebulaGridHazardComponent> grid,
        MapId mapId,
        NebulaMapComponent mapComponent,
        bool heavy)
    {
        if (!TrySelectStrikeTile(grid, mapId, mapComponent, out _, out var targetCoords, out var targetGridCoords))
            return false;

        var hazard = grid.Comp3;
        var lightning = heavy ? hazard.HeavyLightningPrototype : hazard.SmallLightningPrototype;
        var lightningLength = heavy ? hazard.HeavyLightningLength : hazard.SmallLightningLength;
        var sourceDirection = targetCoords.Position - _transform.GetWorldPosition(grid.Comp2);
        SpawnLightning(targetCoords, lightning, lightningLength, sourceDirection);

        var shieldLoad = heavy ? hazard.HeavyShieldLoad : hazard.SmallShieldLoad;
        if (_shields.TryAbsorbNebulaStrike(grid.Owner, shieldLoad, out _))
        {
            PlayLightningSound(hazard.ShieldImpactSound, targetGridCoords);
            Spawn(SparksPrototype, targetCoords);
            return true;
        }

        var impactSound = heavy ? hazard.HeavyImpactSound : hazard.SmallImpactSound;

        QueueExplosion(targetGridCoords, hazard, heavy);
        PlayLightningSound(impactSound, targetGridCoords);
        Spawn(SparksPrototype, targetCoords);
        return true;
    }

    private bool TrySelectStrikeTile(
        Entity<MapGridComponent, TransformComponent, NebulaGridHazardComponent> grid,
        MapId mapId,
        NebulaMapComponent mapComponent,
        out TileRef selected,
        out MapCoordinates selectedCoords,
        out EntityCoordinates selectedGridCoords)
    {
        selected = default;
        selectedCoords = default;
        selectedGridCoords = default;
        var candidates = 0;

        var tiles = _map.GetAllTilesEnumerator(grid.Owner, grid.Comp1, true);
        while (tiles.MoveNext(out var tile))
        {
            if (tile is not { } tileRef)
                continue;

            if (tileRef.Tile.IsEmpty || !IsEdgeTile(grid.Owner, grid.Comp1, tileRef.GridIndices))
                continue;

            var gridCoords = _map.GridTileToLocal(grid.Owner, grid.Comp1, tileRef.GridIndices);
            var coords = _transform.ToMapCoordinates(gridCoords);
            if (coords.MapId != mapId || !IsRedNebulaAt(coords.Position, mapComponent))
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
        var mapCoords = _transform.ToMapCoordinates(coordinates);
        var filter = Filter.Pvs(mapCoords).AddInRange(mapCoords, LightningAudioRange);
        var audioParams = sound.Params
            .AddVolume(LightningAudioVolume)
            .WithMaxDistance(LightningAudioRange)
            .WithRolloffFactor(0f);

        _audio.PlayStatic(sound, filter, coordinates, true, audioParams);
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

    private void QueueExplosion(EntityCoordinates targetCoords, NebulaGridHazardComponent hazard, bool heavy)
    {
        var explosionType = heavy ? hazard.HeavyExplosionType : hazard.SmallExplosionType;
        var totalIntensity = heavy ? hazard.HeavyExplosionTotalIntensity : hazard.SmallExplosionTotalIntensity;
        var slope = heavy ? hazard.HeavyExplosionIntensitySlope : hazard.SmallExplosionIntensitySlope;
        var maxTileIntensity = heavy ? hazard.HeavyExplosionMaxTileIntensity : hazard.SmallExplosionMaxTileIntensity;

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

    private void UpdatePlayerHazards(MapId mapId, NebulaMapComponent mapComponent)
    {
        foreach (var session in _player.Sessions)
        {
            if (!TryGetValidPlayer(session, mapId, out var player, out var xform))
                continue;

            var mapCoords = _transform.GetMapCoordinates(player, xform);
            if (!IsRedNebulaAt(mapCoords.Position, mapComponent))
            {
                RemoveSpacePlayerHazard(player);
                continue;
            }

            UpdateSpacePlayerHazard((player, xform), mapCoords);
            CollectNearbyHazardGrids((player, xform), mapCoords, mapId, mapComponent);
        }
    }

    private bool TryGetValidPlayer(
        ICommonSession session,
        MapId mapId,
        out EntityUid player,
        out TransformComponent xform)
    {
        player = EntityUid.Invalid;
        xform = default!;

        if (session.AttachedEntity is not { Valid: true } attached)
            return false;

        if (session.Status != SessionStatus.InGame ||
            Deleted(attached) ||
            HasComp<GhostComponent>(attached) ||
            _mobState.IsDead(attached) ||
            !TryComp(attached, out TransformComponent? playerXform) ||
            playerXform.MapID != mapId)
        {
            RemoveSpacePlayerHazard(attached);
            return false;
        }

        player = attached;
        xform = playerXform;
        return true;
    }

    private void UpdateSpacePlayerHazard(Entity<TransformComponent> player, MapCoordinates mapCoords)
    {
        if (IsOnNonEmptyTile(player.Comp))
        {
            RemoveSpacePlayerHazard(player.Owner);
            return;
        }

        var hazard = EnsureComp<NebulaSpaceLightningTargetComponent>(player.Owner);
        if (hazard.NextStrike == TimeSpan.Zero)
        {
            // The timer starts only after the player enters tileless space, preventing instant exit strikes.
            ScheduleNextSpacePlayerStrike(hazard);
            return;
        }

        if (_timing.CurTime < hazard.NextStrike)
            return;

        StrikeSpacePlayer((player.Owner, hazard, player.Comp), mapCoords);
        ScheduleNextSpacePlayerStrike(hazard);
    }

    private void CollectNearbyHazardGrids(
        Entity<TransformComponent> player,
        MapCoordinates mapCoords,
        MapId mapId,
        NebulaMapComponent mapComponent)
    {
        if (player.Comp.GridUid is { Valid: true } playerGrid)
            TryAddActiveHazardGrid(playerGrid, mapId, mapComponent);

        var range = GetPlayerHazardRange(player.Comp.GridUid);
        var rangeVector = new Vector2(range, range);
        _nearbyGrids.Clear();
        _mapManager.FindGridsIntersecting(
            mapId,
            new Box2(mapCoords.Position - rangeVector, mapCoords.Position + rangeVector),
            ref _nearbyGrids,
            approx: true,
            includeMap: false);

        for (var i = 0; i < _nearbyGrids.Count; i++)
        {
            var grid = _nearbyGrids[i];
            if (!TryComp(grid.Owner, out TransformComponent? gridXform) ||
                !IsPlayerInGridHazardRange((grid.Owner, grid.Comp, gridXform), player.Comp, range))
            {
                continue;
            }

            TryAddActiveHazardGrid((grid.Owner, grid.Comp, gridXform), mapId, mapComponent);
        }
    }

    private float GetPlayerHazardRange(EntityUid? playerGrid)
    {
        if (playerGrid is { Valid: true } grid &&
            TryComp<NebulaGridHazardComponent>(grid, out var hazard))
        {
            return hazard.PlayerRange;
        }

        return DefaultPlayerRange;
    }

    private bool TryAddActiveHazardGrid(
        EntityUid uid,
        MapId mapId,
        NebulaMapComponent mapComponent)
    {
        if (!TryComp(uid, out MapGridComponent? grid) ||
            !TryComp(uid, out TransformComponent? xform))
        {
            return false;
        }

        return TryAddActiveHazardGrid((uid, grid, xform), mapId, mapComponent);
    }

    private bool TryAddActiveHazardGrid(
        Entity<MapGridComponent, TransformComponent> grid,
        MapId mapId,
        NebulaMapComponent mapComponent)
    {
        if (grid.Comp2.MapID != mapId ||
            !IsGridNearRedNebula(grid, mapComponent))
        {
            return false;
        }

        _activeHazardGrids.Add(grid.Owner);
        return true;
    }

    private bool IsPlayerInGridHazardRange(
        Entity<MapGridComponent, TransformComponent> grid,
        TransformComponent playerXform,
        float range)
    {
        if (playerXform.GridUid == grid.Owner)
            return true;

        var playerLocal = Vector2.Transform(_transform.GetWorldPosition(playerXform), _transform.GetInvWorldMatrix(grid.Comp2));
        if (!grid.Comp1.LocalAABB.Enlarged(range).Contains(playerLocal))
            return false;

        foreach (var _ in _map.GetLocalTilesIntersecting(grid.Owner, grid.Comp1, new Circle(playerLocal, range), true))
        {
            return true;
        }

        return false;
    }

    private void UpdateActiveGridHazards(MapId mapId, NebulaMapComponent mapComponent)
    {
        foreach (var uid in _activeHazardGrids)
        {
            if (!TryComp(uid, out MapGridComponent? grid) ||
                !TryComp(uid, out TransformComponent? xform) ||
                xform.MapID != mapId)
            {
                RemCompDeferred<NebulaGridHazardComponent>(uid);
                continue;
            }

            var hazard = EnsureComp<NebulaGridHazardComponent>(uid);
            UpdateLegacyHazardSettings(hazard);
            InitializeTimers(hazard);
            UpdateHazard((uid, grid, xform, hazard), mapId, mapComponent);
        }
    }

    private void ClearInactiveGridHazards()
    {
        var query = EntityQueryEnumerator<NebulaGridHazardComponent>();
        while (query.MoveNext(out var uid, out _))
        {
            if (_activeHazardGrids.Contains(uid))
                continue;

            RemCompDeferred<NebulaGridHazardComponent>(uid);
        }
    }

    private void StrikeSpacePlayer(
        Entity<NebulaSpaceLightningTargetComponent, TransformComponent> player,
        MapCoordinates mapCoords)
    {
        var hazard = player.Comp1;
        SpawnLightning(mapCoords, hazard.LightningPrototype, hazard.LightningLength, _random.NextAngle().ToWorldVec());
        Spawn(SparksPrototype, player.Comp2.Coordinates);

        if (TryAbsorbSpacePlayerStrike(mapCoords, hazard.ShieldLoad))
        {
            PlayLightningSound(hazard.ShieldImpactSound, player.Comp2.Coordinates, hazard.ImpactSoundRange, hazard.ImpactSoundVolume);
            hazard.LastStrike = _timing.CurTime;
            hazard.StrikeCount++;
            return;
        }

        PlayLightningSound(hazard.ImpactSound, player.Comp2.Coordinates, hazard.ImpactSoundRange, hazard.ImpactSoundVolume);
        _damageable.TryChangeDamage(player.Owner, hazard.BurnDamage);
        _electrocution.TryDoElectrocution(player.Owner, null, hazard.ShockDamage, hazard.ShockTime, true);

        hazard.LastStrike = _timing.CurTime;
        hazard.StrikeCount++;
    }

    private bool TryAbsorbSpacePlayerStrike(MapCoordinates mapCoords, float shieldLoad)
    {
        var rangeVector = new Vector2(ShieldProtectionSearchRange, ShieldProtectionSearchRange);
        _nearbyGrids.Clear();
        _mapManager.FindGridsIntersecting(
            mapCoords.MapId,
            new Box2(mapCoords.Position - rangeVector, mapCoords.Position + rangeVector),
            ref _nearbyGrids,
            approx: true,
            includeMap: false);

        for (var i = 0; i < _nearbyGrids.Count; i++)
        {
            var grid = _nearbyGrids[i];
            if (!TryComp(grid.Owner, out TransformComponent? xform))
                continue;

            if (_shields.TryAbsorbNebulaPointStrike((grid.Owner, grid.Comp, xform), mapCoords, shieldLoad, out _))
                return true;
        }

        return false;
    }

    private void ScheduleNextSpacePlayerStrike(NebulaSpaceLightningTargetComponent hazard)
    {
        var min = Math.Min(hazard.MinStrikeDelaySeconds, hazard.MaxStrikeDelaySeconds);
        var max = Math.Max(hazard.MinStrikeDelaySeconds, hazard.MaxStrikeDelaySeconds);
        hazard.NextStrike = _timing.CurTime + TimeSpan.FromSeconds(_random.Next(min, max + 1));
    }

    private bool IsOnNonEmptyTile(TransformComponent xform)
    {
        var tileRef = xform.Coordinates.GetTileRef(EntityManager, _mapManager);
        return tileRef is { Tile.IsEmpty: false };
    }

    private void RemoveSpacePlayerHazard(EntityUid player)
    {
        if (HasComp<NebulaSpaceLightningTargetComponent>(player))
            RemCompDeferred<NebulaSpaceLightningTargetComponent>(player);
    }

    private bool IsGridNearRedNebula(Entity<MapGridComponent, TransformComponent> grid, NebulaMapComponent mapComponent)
    {
        var bounds = _transform.GetWorldMatrix(grid.Comp2).TransformBox(grid.Comp1.LocalAABB);

        for (var i = 0; i < mapComponent.Nebulas.Count; i++)
        {
            if (NebulaTypeHelpers.GetOrDefault(mapComponent.NebulaTypes, i) != NebulaType.Red)
                continue;

            var nebula = mapComponent.Nebulas[i];
            var closest = new Vector2(
                Math.Clamp(nebula.Center.X, bounds.Left, bounds.Right),
                Math.Clamp(nebula.Center.Y, bounds.Bottom, bounds.Top));

            if ((closest - nebula.Center).LengthSquared() <= nebula.BoundingRadius * nebula.BoundingRadius)
                return true;
        }

        return false;
    }

    private bool IsRedNebulaAt(Vector2 position, NebulaMapComponent mapComponent)
    {
        return _presence.TryGetNebulaAt(position, mapComponent, out _, out var type, out _, out _) &&
               type == NebulaType.Red;
    }

    private bool TryGetNebulaMap(out MapId mapId, out NebulaMapComponent component)
    {
        mapId = _ticker.DefaultMap;
        component = default!;

        if (!_mapManager.MapExists(mapId))
            return false;

        var mapUid = _mapManager.GetMapEntityId(mapId);
        if (!TryComp<NebulaMapComponent>(mapUid, out var mapComponent) || mapComponent.Nebulas.Count == 0)
            return false;

        component = mapComponent;
        return true;
    }

    private void ClearHazards()
    {
        _activeHazardGrids.Clear();
        _nearbyGrids.Clear();

        var query = EntityQueryEnumerator<NebulaGridHazardComponent>();
        while (query.MoveNext(out var uid, out _))
        {
            RemCompDeferred<NebulaGridHazardComponent>(uid);
        }

        var playerQuery = EntityQueryEnumerator<NebulaSpaceLightningTargetComponent>();
        while (playerQuery.MoveNext(out var uid, out _))
        {
            RemCompDeferred<NebulaSpaceLightningTargetComponent>(uid);
        }
    }
}

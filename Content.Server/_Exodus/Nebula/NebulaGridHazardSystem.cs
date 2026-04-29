using System.Numerics;
using Content.Server._Crescent.ShipShields;
using Content.Server.Beam;
using Content.Server.GameTicking;
using Content.Shared._Exodus.Nebula;
using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.GameTicking;
using Content.Shared.Ghost;
using Content.Shared.Maps;
using Content.Shared.Mobs.Systems;
using Robust.Server.GameObjects;
using Robust.Server.Player;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Enums;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._Exodus.Nebula;

public sealed class NebulaGridHazardSystem : EntitySystem
{
    private static readonly TimeSpan UpdateInterval = TimeSpan.FromSeconds(1);
    private const float StrikeSourceDistance = 8f;
    private const string SparksPrototype = "EffectSparks";

    [Dependency] private readonly BeamSystem _beam = default!;
    [Dependency] private readonly DamageableSystem _damageable = default!;
    [Dependency] private readonly GameTicker _ticker = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IMapManager _mapManager = default!;
    [Dependency] private readonly IPlayerManager _player = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly MapSystem _map = default!;
    [Dependency] private readonly MobStateSystem _mobState = default!;
    [Dependency] private readonly NebulaPresenceSystem _presence = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly ShipShieldsSystem _shields = default!;

    private readonly HashSet<Entity<TransformComponent>> _entitiesOnTile = new();
    private readonly HashSet<EntityUid> _damagedEntities = new();
    private EntityQuery<DamageableComponent> _damageableQuery;
    private TimeSpan _nextUpdate;

    public override void Initialize()
    {
        _damageableQuery = GetEntityQuery<DamageableComponent>();

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

        var query = EntityQueryEnumerator<MapGridComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var grid, out var xform))
        {
            if (xform.MapID != mapId ||
                !IsGridNearRedNebula((uid, grid, xform), mapComponent))
            {
                RemCompDeferred<NebulaGridHazardComponent>(uid);
                continue;
            }

            var hazard = EnsureComp<NebulaGridHazardComponent>(uid);
            if (!HasNearbyPlayer((uid, grid, xform), mapId, hazard.PlayerRange))
            {
                RemCompDeferred<NebulaGridHazardComponent>(uid);
                continue;
            }

            InitializeTimers(hazard);
            UpdateHazard((uid, grid, xform, hazard), mapId, mapComponent);
        }
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
        hazard.NextSmallStrike = _timing.CurTime + TimeSpan.FromSeconds(_random.NextFloat(1f, (float)hazard.SmallStrikeInterval.TotalSeconds));
        hazard.NextHeavyStrike = _timing.CurTime + TimeSpan.FromSeconds(_random.NextFloat(5f, (float)hazard.HeavyStrikeInterval.TotalSeconds));
    }

    private void UpdateHazard(
        Entity<MapGridComponent, TransformComponent, NebulaGridHazardComponent> grid,
        MapId mapId,
        NebulaMapComponent mapComponent)
    {
        var hazard = grid.Comp3;
        if (_timing.CurTime >= hazard.NextHeavyStrike)
        {
            TryStrikeGrid(grid, mapId, mapComponent, true);
            hazard.NextHeavyStrike = _timing.CurTime + hazard.HeavyStrikeInterval;

            if (hazard.NextSmallStrike <= _timing.CurTime)
                hazard.NextSmallStrike = _timing.CurTime + hazard.SmallStrikeInterval;

            return;
        }

        if (_timing.CurTime < hazard.NextSmallStrike)
            return;

        TryStrikeGrid(grid, mapId, mapComponent, false);
        hazard.NextSmallStrike = _timing.CurTime + hazard.SmallStrikeInterval;
    }

    private bool TryStrikeGrid(
        Entity<MapGridComponent, TransformComponent, NebulaGridHazardComponent> grid,
        MapId mapId,
        NebulaMapComponent mapComponent,
        bool heavy)
    {
        if (!TrySelectStrikeTile(grid, mapId, mapComponent, out var tile, out var targetCoords))
            return false;

        var hazard = grid.Comp3;
        var shieldLoad = heavy ? hazard.HeavyShieldLoad : hazard.SmallShieldLoad;
        if (_shields.TryAbsorbNebulaStrike(grid.Owner, shieldLoad, out var shield))
        {
            if (!Deleted(shield))
                _audio.PlayPvs(hazard.ShieldImpactSound, shield);

            Spawn(SparksPrototype, targetCoords);
            return true;
        }

        var lightning = heavy ? hazard.HeavyLightningPrototype : hazard.SmallLightningPrototype;
        var radius = heavy ? hazard.HeavyRadius : hazard.SmallRadius;
        var damage = heavy ? hazard.HeavyDamage : hazard.SmallDamage;

        SpawnLightning(targetCoords, lightning);
        DamageStrikeArea(grid.Owner, grid.Comp1, tile.GridIndices, radius, damage);
        Spawn(SparksPrototype, targetCoords);
        return true;
    }

    private bool TrySelectStrikeTile(
        Entity<MapGridComponent, TransformComponent, NebulaGridHazardComponent> grid,
        MapId mapId,
        NebulaMapComponent mapComponent,
        out TileRef selected,
        out MapCoordinates selectedCoords)
    {
        selected = default;
        selectedCoords = default;
        var candidates = 0;

        var tiles = _map.GetAllTilesEnumerator(grid.Owner, grid.Comp1, true);
        while (tiles.MoveNext(out var tile))
        {
            if (tile is not { } tileRef)
                continue;

            var coords = _transform.ToMapCoordinates(_map.GridTileToLocal(grid.Owner, grid.Comp1, tileRef.GridIndices));
            if (coords.MapId != mapId || !IsRedNebulaAt(coords.Position, mapComponent))
                continue;

            candidates++;
            if (_random.Next(candidates) != 0)
                continue;

            selected = tileRef;
            selectedCoords = coords;
        }

        return candidates > 0;
    }

    private void SpawnLightning(MapCoordinates targetCoords, EntProtoId lightningPrototype)
    {
        var offset = _random.NextAngle().ToWorldVec() * StrikeSourceDistance;
        var source = Spawn(null, targetCoords.Offset(offset));
        var target = Spawn(null, targetCoords);

        _beam.TryCreateBeam(source, target, lightningPrototype);

        QueueDel(source);
        QueueDel(target);
    }

    private void DamageStrikeArea(
        EntityUid gridUid,
        MapGridComponent grid,
        Vector2i centerTile,
        float radius,
        DamageSpecifier damage)
    {
        _damagedEntities.Clear();

        foreach (var tile in _map.GetLocalTilesIntersecting(gridUid, grid, new Circle(centerTile, radius)))
        {
            var distance = (tile.GridIndices - centerTile).Length;
            var falloff = Math.Clamp(1f - distance / (radius + 0.5f), 0f, 1f);
            if (falloff <= 0f)
                continue;

            _entitiesOnTile.Clear();
            _lookup.GetLocalEntitiesIntersecting(gridUid, tile.GridIndices, _entitiesOnTile, gridComp: grid);

            foreach (var entity in _entitiesOnTile)
            {
                if (!_damagedEntities.Add(entity.Owner) ||
                    !_damageableQuery.TryComp(entity.Owner, out var damageable))
                {
                    continue;
                }

                _damageable.TryChangeDamage(entity.Owner, damage * falloff, damageable: damageable);
            }
        }
    }

    private bool HasNearbyPlayer(Entity<MapGridComponent, TransformComponent> grid, MapId mapId, float range)
    {
        var enlargedBounds = grid.Comp1.LocalAABB.Enlarged(range);
        var inverseMatrix = _transform.GetInvWorldMatrix(grid.Comp2);

        foreach (var session in _player.Sessions)
        {
            if (session.Status != SessionStatus.InGame ||
                session.AttachedEntity is not { Valid: true } player ||
                Deleted(player) ||
                HasComp<GhostComponent>(player) ||
                _mobState.IsDead(player) ||
                !TryComp(player, out TransformComponent? playerXform) ||
                playerXform.MapID != mapId)
            {
                continue;
            }

            if (playerXform.GridUid == grid.Owner)
                return true;

            var playerLocal = Vector2.Transform(_transform.GetWorldPosition(playerXform), inverseMatrix);
            if (!enlargedBounds.Contains(playerLocal))
                continue;

            foreach (var _ in _map.GetLocalTilesIntersecting(grid.Owner, grid.Comp1, new Circle(playerLocal, range), true))
            {
                return true;
            }
        }

        return false;
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
        var query = EntityQueryEnumerator<NebulaGridHazardComponent>();
        while (query.MoveNext(out var uid, out _))
        {
            RemCompDeferred<NebulaGridHazardComponent>(uid);
        }
    }
}

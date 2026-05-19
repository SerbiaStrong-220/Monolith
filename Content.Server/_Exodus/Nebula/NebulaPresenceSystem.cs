using System.Numerics;
using Content.Server.GameTicking;
using Content.Shared._Exodus.Nebula;
using Content.Shared.GameTicking;
using Content.Shared.Ghost;
using Content.Shared.Mobs.Systems;
using Robust.Server.Player;
using Robust.Shared.Enums;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._Exodus.Nebula;

public sealed class NebulaPresenceSystem : EntitySystem
{
    private static readonly TimeSpan UpdateInterval = TimeSpan.FromSeconds(1);
    private const float DirtyThreshold = 0.01f;

    [Dependency] private readonly GameTicker _ticker = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IMapManager _mapManager = default!;
    [Dependency] private readonly IPlayerManager _playerManager = default!;
    [Dependency] private readonly MobStateSystem _mobState = default!;
    [Dependency] private readonly SharedMapSystem _mapSystem = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;

    private ISawmill _sawmill = Logger.GetSawmill("nebula");

    private const float GridHazardRadius = 32f;

    private readonly HashSet<EntityUid> _checkedGrids = new();
    private readonly HashSet<EntityUid> _updatedEntities = new();
    private List<Entity<MapGridComponent>> _nearbyGridBuffer = new();
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
            ClearAllPresence();
            return;
        }

        _checkedGrids.Clear();
        _updatedEntities.Clear();

        UpdatePlayerPresence(mapId, mapComponent);
        ClearStalePresence();
    }

    private void OnRoundRestart(RoundRestartCleanupEvent args)
    {
        ClearAllPresence();
    }

    private void UpdatePlayerPresence(MapId mapId, NebulaMapComponent mapComponent)
    {
        foreach (var session in _playerManager.Sessions)
        {
            if (session.Status != SessionStatus.InGame ||
                session.AttachedEntity is not { Valid: true } player ||
                Deleted(player))
            {
                continue;
            }

            UpdateEntityPresence(player, mapId, mapComponent);

            // Ghosts and dead bodies must not activate grid hazards.
            if (HasComp<GhostComponent>(player) || _mobState.IsDead(player))
                continue;

            if (!TryComp(player, out TransformComponent? xform) || xform.MapID != mapId)
                continue;

            var pos = _transform.GetWorldPosition(xform);
            if (!TryGetNebulaAt(pos, mapComponent, out _, out _, out _, out _))
                continue;

            // Activate hazards on all grids within radius of this player.
            var rangeVec = new Vector2(GridHazardRadius, GridHazardRadius);
            _nearbyGridBuffer.Clear();
            _mapManager.FindGridsIntersecting(
                mapId,
                new Box2(pos - rangeVec, pos + rangeVec),
                ref _nearbyGridBuffer,
                approx: true,
                includeMap: false);

            foreach (var grid in _nearbyGridBuffer)
            {
                if (!_checkedGrids.Add(grid.Owner))
                    continue;

                UpdateEntityPresence(grid.Owner, mapId, mapComponent);
            }
        }
    }

    private void UpdateEntityPresence(
        EntityUid uid,
        MapId mapId,
        NebulaMapComponent mapComponent,
        TransformComponent? xform = null)
    {
        _updatedEntities.Add(uid);

        if (!Resolve(uid, ref xform, false) || xform.MapID != mapId)
        {
            ClearPresence(uid);
            return;
        }

        var position = _transform.GetWorldPosition(xform);
        if (TryGetNebulaAt(position, mapComponent, out var index, out var marker, out var density, out var alpha))
            SetPresence(uid, index, marker, density, alpha);
        else
            ClearPresence(uid);
    }

    public bool TryGetNebulaAt(
        Vector2 position,
        NebulaMapComponent mapComponent,
        out int index,
        out EntProtoId marker,
        out float density,
        out float alpha)
    {
        index = -1;
        marker = default;
        density = 0f;
        alpha = 0f;

        for (var i = 0; i < mapComponent.Nebulas.Count; i++)
        {
            var nebula = mapComponent.Nebulas[i];
            var delta = position - nebula.Center;
            if (delta.LengthSquared() > nebula.BoundingRadius * nebula.BoundingRadius)
                continue;

            if (!nebula.Contains(position))
                continue;

            index = i;
            marker = i < mapComponent.NebulaPrototypes.Count ? mapComponent.NebulaPrototypes[i] : default;
            density = nebula.GetDensity(position);
            alpha = nebula.GetAlpha(position);
            return true;
        }

        if (mapComponent.WorldEndMarker != default &&
            mapComponent.WorldEnd.IsGenerated &&
            mapComponent.WorldEnd.Contains(position))
        {
            index = -1;
            marker = mapComponent.WorldEndMarker;
            density = 1f;
            alpha = 1f;
            return true;
        }

        return false;
    }

    private void SetPresence(EntityUid uid, int index, EntProtoId marker, float density, float alpha)
    {
        var hadComponent = TryComp<NebulaPresenceComponent>(uid, out var existing);
        var oldMarker = hadComponent ? existing!.Marker : default;
        var component = hadComponent ? existing! : EnsureComp<NebulaPresenceComponent>(uid);
        var changedMarker = !hadComponent || component.NebulaIndex != index || component.Marker != marker;

        if (!changedMarker &&
            MathF.Abs(component.Density - density) < DirtyThreshold &&
            MathF.Abs(component.Alpha - alpha) < DirtyThreshold)
        {
            return;
        }

        component.NebulaIndex = index;
        component.Marker = marker;
        component.Density = density;
        component.Alpha = alpha;
        Dirty(uid, component);

        if (changedMarker)
        {
            _sawmill.Debug($"{ToPrettyString(uid)} entered {marker} nebula {index + 1} with density {density:0.00}.");
            var ev = new NebulaPresenceChangedEvent(oldMarker, marker);
            RaiseLocalEvent(uid, ref ev);
        }
    }

    private void ClearPresence(EntityUid uid)
    {
        if (!TryComp<NebulaPresenceComponent>(uid, out var component))
            return;

        _sawmill.Debug($"{ToPrettyString(uid)} left nebula {component.NebulaIndex + 1}.");
        RemCompDeferred<NebulaPresenceComponent>(uid);
        // The hazard coordinator listens to ComponentRemove on NebulaPresenceComponent, so it
        // tears down per-effect components without needing a separate event here.
    }

    private void ClearStalePresence()
    {
        var query = EntityQueryEnumerator<NebulaPresenceComponent>();
        while (query.MoveNext(out var uid, out _))
        {
            if (!_updatedEntities.Contains(uid))
                ClearPresence(uid);
        }
    }

    private void ClearAllPresence()
    {
        var query = EntityQueryEnumerator<NebulaPresenceComponent>();
        while (query.MoveNext(out var uid, out _))
        {
            RemCompDeferred<NebulaPresenceComponent>(uid);
        }
    }

    private bool TryGetNebulaMap(out MapId mapId, out NebulaMapComponent component)
    {
        mapId = _ticker.DefaultMap;
        component = default!;

        if (!_mapSystem.TryGetMap(mapId, out var mapUid))
            return false;

        if (!TryComp<NebulaMapComponent>(mapUid, out var mapComponent))
            return false;

        if (mapComponent.Nebulas.Count == 0 && !mapComponent.WorldEnd.IsGenerated)
            return false;

        component = mapComponent;
        return true;
    }
}

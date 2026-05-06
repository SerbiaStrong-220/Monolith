using System.Numerics;
using Content.Server.GameTicking;
using Content.Server.Shuttles.Components;
using Content.Shared._Exodus.Nebula;
using Content.Shared.GameTicking;
using Robust.Server.Player;
using Robust.Shared.Enums;
using Robust.Shared.Map;
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
    [Dependency] private readonly SharedTransformSystem _transform = default!;

    private readonly HashSet<EntityUid> _checkedGrids = new();
    private readonly HashSet<EntityUid> _updatedEntities = new();
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
        UpdateShuttlePresence(mapId, mapComponent);
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

            if (!TryComp(player, out TransformComponent? xform) ||
                xform.GridUid is not { } grid ||
                Deleted(grid) ||
                !_checkedGrids.Add(grid))
            {
                continue;
            }

            UpdateEntityPresence(grid, mapId, mapComponent);
        }
    }

    private void UpdateShuttlePresence(MapId mapId, NebulaMapComponent mapComponent)
    {
        var shuttleQuery = EntityQueryEnumerator<ShuttleComponent, TransformComponent>();
        while (shuttleQuery.MoveNext(out var uid, out _, out var xform))
        {
            if (xform.MapID != mapId || !_checkedGrids.Add(uid))
                continue;

            UpdateEntityPresence(uid, mapId, mapComponent, xform);
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
            Logger.DebugS("nebula", $"{ToPrettyString(uid)} entered {marker} nebula {index + 1} with density {density:0.00}.");
            var ev = new NebulaPresenceChangedEvent(uid, oldMarker, marker);
            RaiseLocalEvent(uid, ref ev);
        }
    }

    private void ClearPresence(EntityUid uid)
    {
        if (!TryComp<NebulaPresenceComponent>(uid, out var component))
            return;

        Logger.DebugS("nebula", $"{ToPrettyString(uid)} left nebula {component.NebulaIndex + 1}.");
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

        if (!_mapManager.MapExists(mapId))
            return false;

        var mapUid = _mapManager.GetMapEntityId(mapId);
        if (!TryComp<NebulaMapComponent>(mapUid, out var mapComponent) || mapComponent.Nebulas.Count == 0)
            return false;

        component = mapComponent;
        return true;
    }
}

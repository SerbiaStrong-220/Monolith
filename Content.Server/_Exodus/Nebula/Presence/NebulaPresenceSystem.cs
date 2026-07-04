using System.Numerics;
using Content.Server._Exodus.Nebula.Components;
using Content.Server.GameTicking;
using Content.Server.Power.Components;
using Content.Server.Power.EntitySystems;
using Content.Shared._Exodus.Nebula.Components;
using Content.Shared._Exodus.Nebula.Events;
using Content.Shared._Exodus.Nebula.Generation;
using Content.Shared.GameTicking;
using Content.Shared.Ghost;
using Content.Shared.Mind;
using Content.Shared.Mobs.Systems;
using Robust.Server.Player;
using Robust.Shared.Enums;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._Exodus.Nebula.Presence;

public sealed partial class NebulaPresenceSystem : EntitySystem
{
    private static readonly TimeSpan UpdateInterval = TimeSpan.FromSeconds(2);
    public static readonly TimeSpan NpcGridScanInterval = TimeSpan.FromSeconds(20);
    public static readonly TimeSpan NpcGridLeaseDuration = TimeSpan.FromSeconds(25);
    private const float DirtyThreshold = 0.01f;
    public const float NpcGridScanRadius = 1500f;

    [Dependency] private GameTicker _ticker = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IMapManager _mapManager = default!;
    [Dependency] private IPlayerManager _playerManager = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private PowerReceiverSystem _power = default!;
    [Dependency] private SharedMindSystem _mind = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private SharedMapSystem _mapSystem = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    private ISawmill _sawmill = Logger.GetSawmill("nebula");

    private const float GridHazardRadius = 32f;

    private readonly HashSet<EntityUid> _checkedGrids = new();
    private readonly HashSet<EntityUid> _checkedNpcGrids = new();
    private readonly HashSet<EntityUid> _updatedEntities = new();
    private readonly HashSet<Entity<NebulaNpcGridPresenceSourceComponent>> _nearbyNpcCoreBuffer = new();
    private List<Entity<MapGridComponent>> _nearbyGridBuffer = new();
    private TimeSpan _nextUpdate;
    private TimeSpan _nextNpcGridScan;

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
            ClearAllNpcPresenceLeases();
            return;
        }

        _checkedGrids.Clear();
        _updatedEntities.Clear();

        UpdatePlayerPresence(mapId, mapComponent);
        if (_timing.CurTime >= _nextNpcGridScan)
        {
            _nextNpcGridScan = _timing.CurTime + NpcGridScanInterval;
            UpdateNpcGridPresence(mapId, mapComponent);
        }

        ClearExpiredNpcPresenceLeases();
        ClearStalePresence();
    }

    private void OnRoundRestart(RoundRestartCleanupEvent args)
    {
        ClearAllPresence();
        ClearAllNpcPresenceLeases();
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

    private void UpdateNpcGridPresence(MapId mapId, NebulaMapComponent mapComponent)
    {
        _checkedNpcGrids.Clear();

        foreach (var session in _playerManager.Sessions)
        {
            if (!TryGetNpcScanOrigin(session, mapId, out var origin))
                continue;

            _nearbyNpcCoreBuffer.Clear();
            _lookup.GetEntitiesInRange(
                origin,
                NpcGridScanRadius,
                _nearbyNpcCoreBuffer,
                LookupFlags.Uncontained);

            foreach (var core in _nearbyNpcCoreBuffer)
            {
                if (!TryGetActiveNpcGrid(core.Owner, mapId, out var gridUid))
                    continue;

                if (!_checkedNpcGrids.Add(gridUid))
                    continue;

                UpdateEntityPresence(gridUid, mapId, mapComponent);
                RefreshNpcPresenceLease(gridUid, core.Owner);
            }
        }
    }

    private bool TryGetNpcScanOrigin(ICommonSession session, MapId mapId, out MapCoordinates origin)
    {
        origin = default;

        if (session.Status != SessionStatus.InGame ||
            session.AttachedEntity is not { Valid: true } player ||
            Deleted(player) ||
            HasComp<GhostComponent>(player) ||
            _mobState.IsDead(player) ||
            !_mind.TryGetMind(player, out _, out _) ||
            !TryComp(player, out TransformComponent? xform) ||
            xform.MapID != mapId)
        {
            return false;
        }

        origin = _transform.GetMapCoordinates(player, xform);
        return true;
    }

    private bool TryGetActiveNpcGrid(EntityUid coreUid, MapId mapId, out EntityUid gridUid)
    {
        gridUid = default;

        if (Deleted(coreUid) ||
            !TryComp(coreUid, out TransformComponent? xform) ||
            xform.MapID != mapId ||
            !xform.Anchored ||
            xform.GridUid is not { Valid: true } grid ||
            Deleted(grid) ||
            !HasComp<MapGridComponent>(grid))
        {
            return false;
        }

        if (TryComp<ApcPowerReceiverComponent>(coreUid, out var receiver) &&
            !_power.IsPowered(coreUid, receiver))
        {
            return false;
        }

        gridUid = grid;
        return true;
    }

    private void RefreshNpcPresenceLease(EntityUid gridUid, EntityUid coreUid)
    {
        var lease = EnsureComp<NebulaNpcPresenceLeaseComponent>(gridUid);
        lease.SourceCore = coreUid;
        lease.LastRefresh = _timing.CurTime;
        lease.ExpiresAt = _timing.CurTime + NpcGridLeaseDuration;
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

        var position = GetPresencePosition(uid, xform);
        if (TryGetNebulaAt(position, mapComponent, out var index, out var marker, out var density, out var alpha))
            SetPresence(uid, index, marker, density, alpha);
        else
            ClearPresence(uid);
    }

    /// <summary>
    /// Picks the representative world point used to ask "which nebula is this entity in".
    /// For free-space entities (EVA players) this is just the world position. For grids the
    /// origin can sit far from the actual hull bounds, so we use the AABB center — this keeps
    /// hazard activation aligned with what the player sees, especially for capships that
    /// straddle the death-zone inner/outer boundary at 90k.
    /// </summary>
    public Vector2 GetPresencePosition(EntityUid uid, TransformComponent xform)
    {
        if (!TryComp<MapGridComponent>(uid, out var grid))
            return _transform.GetWorldPosition(xform);

        var (worldPos, worldRot) = _transform.GetWorldPositionRotation(xform);
        return worldPos + worldRot.RotateVec(grid.LocalAABB.Center);
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

        if (mapComponent.WorldEnd.IsGenerated &&
            mapComponent.WorldEnd.TryGetZone(position, out var zone))
        {
            var zoneMarker = zone == WorldEndZone.Outer
                ? mapComponent.WorldEndOuterMarker
                : mapComponent.WorldEndInnerMarker;

            if (zoneMarker != default)
            {
                index = -1;
                marker = zoneMarker;
                density = 1f;
                alpha = 1f;
                return true;
            }
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
            var ev = new NebulaPresenceChangedEvent(uid, oldMarker, marker);
            RaiseLocalEvent(uid, ref ev, broadcast: true);
        }
    }

    private void ClearPresence(EntityUid uid)
    {
        if (!TryComp<NebulaPresenceComponent>(uid, out var component))
            return;

        _sawmill.Debug($"{ToPrettyString(uid)} left nebula {component.NebulaIndex + 1}.");
        var oldMarker = component.Marker;
        component.NebulaIndex = -1;
        component.Marker = default;
        component.Density = 0f;
        component.Alpha = 0f;
        Dirty(uid, component);

        var ev = new NebulaPresenceChangedEvent(uid, oldMarker, default);
        RaiseLocalEvent(uid, ref ev, broadcast: true);
        RemCompDeferred<NebulaPresenceComponent>(uid);
    }

    private void ClearStalePresence()
    {
        var query = EntityQueryEnumerator<NebulaPresenceComponent>();
        while (query.MoveNext(out var uid, out _))
        {
            if (_updatedEntities.Contains(uid))
                continue;

            if (TryComp<NebulaNpcPresenceLeaseComponent>(uid, out var lease) &&
                lease.ExpiresAt > _timing.CurTime)
            {
                continue;
            }

            ClearPresence(uid);
        }
    }

    private void ClearExpiredNpcPresenceLeases()
    {
        var query = EntityQueryEnumerator<NebulaNpcPresenceLeaseComponent>();
        while (query.MoveNext(out var uid, out var lease))
        {
            if (lease.ExpiresAt > _timing.CurTime)
                continue;

            RemCompDeferred<NebulaNpcPresenceLeaseComponent>(uid);
        }
    }

    private void ClearAllPresence()
    {
        var query = EntityQueryEnumerator<NebulaPresenceComponent>();
        while (query.MoveNext(out var uid, out _))
        {
            ClearPresence(uid);
        }
    }

    private void ClearAllNpcPresenceLeases()
    {
        var query = EntityQueryEnumerator<NebulaNpcPresenceLeaseComponent>();
        while (query.MoveNext(out var uid, out _))
        {
            RemCompDeferred<NebulaNpcPresenceLeaseComponent>(uid);
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

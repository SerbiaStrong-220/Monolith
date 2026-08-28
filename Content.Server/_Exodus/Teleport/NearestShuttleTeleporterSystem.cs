using System.Collections.Generic;
using System.Numerics;
using Content.Server.Popups;
using Content.Server.Shuttles.Components;
using Content.Shared._Exodus.Teleport;
using Content.Shared.Interaction;
using Content.Shared.Maps;
using Content.Shared.Mobs.Systems;
using Content.Shared.Movement.Pulling.Components;
using Content.Shared.Movement.Pulling.Systems;
using Content.Shared.Physics;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Timing;

namespace Content.Server._Exodus.Teleport;

public sealed class NearestShuttleTeleporterSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly SharedMapSystem _map = default!;
    [Dependency] private readonly PopupSystem _popup = default!;
    [Dependency] private readonly MobStateSystem _mobState = default!;
    [Dependency] private readonly PullingSystem _pulling = default!;
    [Dependency] private readonly TurfSystem _turf = default!;

    private EntityQuery<MapGridComponent> _gridQuery;
    private readonly List<(EntityUid Grid, float DistanceSquared)> _candidateGridBuffer = new();

    public override void Initialize()
    {
        base.Initialize();
        _gridQuery = GetEntityQuery<MapGridComponent>();
        SubscribeLocalEvent<NearestShuttleTeleporterComponent, ActivateInWorldEvent>(OnActivate);
    }

    private void OnActivate(Entity<NearestShuttleTeleporterComponent> ent, ref ActivateInWorldEvent args)
    {
        if (args.Handled || !args.Complex)
            return;

        var user = args.User;
        if (!_mobState.IsAlive(user))
            return;

        var padXform = Transform(ent);
        var userXform = Transform(user);

        if (userXform.GridUid != padXform.GridUid
            || userXform.Coordinates.GetGridUid(EntityManager) != padXform.GridUid)
        {
            _popup.PopupEntity(Loc.GetString(ent.Comp.PopupStandOnPad), user, user);
            args.Handled = true;
            return;
        }

        if (padXform.GridUid is not { } currentGrid
            || !_gridQuery.HasComp(currentGrid))
        {
            _popup.PopupEntity(Loc.GetString(ent.Comp.PopupFail), user, user);
            args.Handled = true;
            return;
        }

        var grid = Comp<MapGridComponent>(currentGrid);
        var padTile = _map.TileIndicesFor(currentGrid, grid, padXform.Coordinates);
        var userTile = _map.TileIndicesFor(currentGrid, grid, userXform.Coordinates);
        if (padTile != userTile)
        {
            _popup.PopupEntity(Loc.GetString(ent.Comp.PopupStandOnPad), user, user);
            args.Handled = true;
            return;
        }

        var curTime = _timing.CurTime;
        if (ent.Comp.NextUse > curTime)
        {
            _popup.PopupEntity(Loc.GetString(ent.Comp.PopupCooldown), user, user);
            args.Handled = true;
            return;
        }

        if (padXform.MapUid is not { } mapUid)
        {
            _popup.PopupEntity(Loc.GetString(ent.Comp.PopupFail), user, user);
            args.Handled = true;
            return;
        }

        var origin = _transform.GetWorldPosition(padXform);
        if (!TryFindDestination(mapUid, currentGrid, origin, ent.Comp.MaxRange, out var destCoords))
        {
            ent.Comp.NextUse = curTime + ent.Comp.FailureCooldown;
            _popup.PopupEntity(Loc.GetString(ent.Comp.PopupFail), user, user);
            args.Handled = true;
            return;
        }

        if (TryComp<PullerComponent>(user, out var puller)
            && puller.Pulling is { } pulled
            && TryComp<PullableComponent>(pulled, out var pullable))
        {
            _pulling.TryStopPull(pulled, pullable, user);
        }

        _transform.SetCoordinates(user, destCoords);
        ent.Comp.NextUse = curTime + ent.Comp.Cooldown;

        _popup.PopupEntity(Loc.GetString(ent.Comp.PopupSuccess), user, user);
        args.Handled = true;
    }

    private bool TryFindDestination(
        EntityUid mapUid,
        EntityUid excludeGrid,
        Vector2 origin,
        float maxRange,
        out EntityCoordinates destCoords)
    {
        destCoords = default;
        var maxDistanceSquared = maxRange > 0f ? maxRange * maxRange : float.MaxValue;
        _candidateGridBuffer.Clear();

        var query = EntityQueryEnumerator<ShuttleComponent, TransformComponent, MapGridComponent>();
        while (query.MoveNext(out var gridUid, out _, out var xform, out _))
        {
            if (gridUid == excludeGrid || xform.MapUid != mapUid)
                continue;

            var delta = _transform.GetWorldPosition(xform) - origin;
            var distanceSquared = delta.LengthSquared();
            if (distanceSquared > maxDistanceSquared)
                continue;

            _candidateGridBuffer.Add((gridUid, distanceSquared));
        }

        _candidateGridBuffer.Sort(static (left, right) =>
            left.DistanceSquared.CompareTo(right.DistanceSquared));

        foreach (var candidate in _candidateGridBuffer)
        {
            if (!_gridQuery.TryGetComponent(candidate.Grid, out var grid) ||
                !TryFindSafeTile(candidate.Grid, grid, out destCoords))
            {
                continue;
            }

            return true;
        }

        return false;
    }

    private bool TryFindSafeTile(EntityUid gridUid, MapGridComponent grid, out EntityCoordinates coords)
    {
        coords = default;
        var tiles = _map.GetAllTilesEnumerator(gridUid, grid);
        while (tiles.MoveNext(out var maybeTile))
        {
            if (maybeTile is not { } tile ||
                _turf.IsSpace(tile) ||
                tile.Tile.IsEmpty ||
                _turf.IsTileBlocked(tile, CollisionGroup.MobMask))
            {
                continue;
            }

            coords = _map.GridTileToLocal(gridUid, grid, tile.GridIndices);
            return true;
        }

        return false;
    }
}

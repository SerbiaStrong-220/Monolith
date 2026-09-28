using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Content.Server.NPC.Components;
using Content.Server.NPC.Pathfinding;
using Content.Shared._Exodus.Virology.Lifecycle;
using Content.Shared.Maps;
using Content.Shared.Physics;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.Server._Exodus.Virology.Lifecycle;

public sealed partial class RotDefenderSystem
{
    [Dependency] private RotColonySiteSystem _sites = default!;
    private readonly List<Entity<RotColonySiteComponent, TransformComponent>> _colonySites = [];

    [Dependency] private PathfindingSystem _pathfinding = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private TurfSystem _turf = default!;
    [Dependency] private RotGroundStrikeSystem _groundStrike = default!;

    public void BeginRoute(Entity<RotDefenderComponent> ent, EntityUid? threat, RotDefenderRoute route)
    {
        CancelRoute(ent);
        var started = new RotDefenderRouteStartedEvent(route);
        RaiseLocalEvent(ent, ref started);
        RemCompDeferred<NPCMeleeCombatComponent>(ent);
        _steering.Unregister(ent);
        ent.Comp.Threat = threat;
        ent.Comp.Route = route;
        ent.Comp.RestUntil = TimeSpan.Zero;
        ent.Comp.MoveUntil = _timing.CurTime + TimeSpan.FromSeconds(15);
        ent.Comp.PathCancellation = new CancellationTokenSource();
        var token = ent.Comp.PathCancellation.Token;
        if (!ent.Comp.MovementRequested || route == RotDefenderRoute.Detour)
            ResetProgress(ent);
        ent.Comp.MovementRequested = true;
        ent.Comp.EscapePath = route switch
        {
            RotDefenderRoute.Cover => FindCoverPath(ent, token),
            RotDefenderRoute.Colony => FindColonyPath(ent, token),
            _ => _pathfinding.GetRandomPath(ent, ent.Comp.EscapeRange / 2, token, limit: 100, flags: PathFlags.Interact),
        };
    }

    // Glass blocks movement and bullets, but does not have the Opaque layer used by lasers.
    // Moving creatures and loose items cannot provide dependable cover.
    private bool IgnoreCover(EntityUid uid) => !Transform(uid).Anchored;

    public bool IsCovered(Entity<RotDefenderComponent> ent, EntityCoordinates point)
    {
        if (!point.IsValid(EntityManager))
            return false;
        if (ent.Comp.Threat is { } threat && IsEnemy(threat)
            && _interaction.InRangeUnobstructed(threat, point, 0, collisionMask: CollisionGroup.Opaque, predicate: IgnoreCover))
            return false;
        foreach (var enemy in ent.Comp.Enemies)
        {
            if (enemy == ent.Comp.Threat || !IsEnemy(enemy))
                continue;
            var position = _transform.GetMapCoordinates(enemy);
            var destination = _transform.ToMapCoordinates(point);
            if (position.MapId == destination.MapId
                && Vector2.DistanceSquared(position.Position, destination.Position) < ent.Comp.SearchRange * ent.Comp.SearchRange
                && _interaction.InRangeUnobstructed(enemy, point, 0, collisionMask: CollisionGroup.Opaque, predicate: IgnoreCover))
                return false;
        }
        return true;
    }

    private async Task<PathResultEvent> FindCoverPath(Entity<RotDefenderComponent> ent, CancellationToken token)
    {
        if (Transform(ent).GridUid is not { } grid || !TryComp<MapGridComponent>(grid, out var mapGrid))
            return new(PathResult.NoPath, []);
        var origin = _transform.GetMapCoordinates(ent);
        var enemy = ent.Comp.Threat is { } threat && IsEnemy(threat) ? _transform.GetMapCoordinates(threat) : origin;
        var away = origin.Position - enemy.Position;
        if (away.LengthSquared() < 0.01f)
            away = Vector2.UnitX;
        away = Vector2.Normalize(away);
        var candidates = new List<(EntityCoordinates Point, float Score)>();
        for (var ring = 1; ring <= 4; ring++)
        {
            var radius = ent.Comp.EscapeRange * ring / 4;
            for (var i = 0; i < 16; i++)
            {
                var angle = i * MathF.Tau / 16;
                var offset = new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * radius;
                var local = _transform.ToCoordinates(grid, new MapCoordinates(origin.Position + offset, origin.MapId));
                var tile = _map.GetTileRef(grid, mapGrid, _map.TileIndicesFor(grid, mapGrid, local));
                if (_turf.IsSpace(tile) || _turf.IsTileBlocked(tile, CollisionGroup.Impassable))
                    continue;
                var point = _map.GridTileToLocal(grid, mapGrid, tile.GridIndices);
                var score = Vector2.Dot(offset, away) - radius * 0.25f;
                if (IsCovered(ent, point))
                    score += ent.Comp.EscapeRange * 4;
                candidates.Add((point, score));
            }
        }
        candidates.Sort((a, b) => b.Score.CompareTo(a.Score));
        for (var i = 0; i < Math.Min(12, candidates.Count); i++)
        {
            token.ThrowIfCancellationRequested();
            if (TerminatingOrDeleted(ent) || TerminatingOrDeleted(grid))
                break;
            var path = await _pathfinding.GetPath(ent, Transform(ent).Coordinates, candidates[i].Point, 0.5f, token, flags: PathFlags.Interact);
            token.ThrowIfCancellationRequested();
            if (TerminatingOrDeleted(ent))
                return new(PathResult.NoPath, []);
            if (path.Result == PathResult.Path && path.Path.Count > 0 && RouteAvoidsThreats(ent, path))
                return path;
        }
        token.ThrowIfCancellationRequested();
        if (TerminatingOrDeleted(ent) || TerminatingOrDeleted(grid))
            return new(PathResult.NoPath, []);
        var fallback = await _pathfinding.GetRandomPath(ent, ent.Comp.EscapeRange, token, limit: 100, flags: PathFlags.Interact);
        token.ThrowIfCancellationRequested();
        return !TerminatingOrDeleted(ent) && RouteAvoidsThreats(ent, fallback) ? fallback : new(PathResult.NoPath, []);
    }

    private async Task<PathResultEvent> FindColonyPath(Entity<RotDefenderComponent> ent, CancellationToken token)
    {
        var origin = _transform.GetMapCoordinates(ent);
        var candidates = new List<(EntityUid Site, float Distance)>();
        var grid = Transform(ent).GridUid;
        // This rare query considers only colony sites, including those outside visual range on the same ship.
        _sites.GetSites(ent, _colonySites);
        foreach (var (uid, _, transform) in _colonySites)
        {
            if (grid == null || transform.GridUid != grid || TerminatingOrDeleted(uid) || _containers.IsEntityInContainer(uid))
                continue;
            var distance = Vector2.DistanceSquared(origin.Position, _transform.GetWorldPosition(uid));
            if (distance > 2.25f)
                candidates.Add((uid, distance));
        }
        candidates.Sort((a, b) => a.Distance.CompareTo(b.Distance));
        for (var i = 0; i < Math.Min(candidates.Count, 8); i++)
        {
            token.ThrowIfCancellationRequested();
            if (TerminatingOrDeleted(ent))
                break;
            if (TerminatingOrDeleted(candidates[i].Site))
                continue;
            var path = await _pathfinding.GetPath(ent, Transform(ent).Coordinates, new EntityCoordinates(candidates[i].Site, Vector2.Zero),
                1f, token, flags: PathFlags.Interact);
            token.ThrowIfCancellationRequested();
            if (TerminatingOrDeleted(ent))
                return new(PathResult.NoPath, []);
            if (path.Result == PathResult.Path && path.Path.Count > 0 && RouteAvoidsThreats(ent, path))
                return path;
        }
        token.ThrowIfCancellationRequested();
        if (TerminatingOrDeleted(ent))
            return new(PathResult.NoPath, []);
        // No reachable colony: keep moving away from danger instead of waiting in place.
        ent.Comp.Route = RotDefenderRoute.Cover;
        return await FindCoverPath(ent, token);
    }

    public bool UpdateRoute(Entity<RotDefenderComponent> ent)
    {
        if (ent.Comp.EscapePath == null && ent.Comp.EscapePoint == null)
            return false;
        if (_timing.CurTime >= ent.Comp.MoveUntil)
        {
            FinishRoute(ent, false);
            return false;
        }
        if (ent.Comp.EscapePath is { } task)
        {
            if (!task.IsCompleted)
                return true;
            ent.Comp.EscapePath = null;
            if (!task.IsCompletedSuccessfully)
            {
                _ = task.Exception;
                FinishRoute(ent, false);
                return false;
            }
#pragma warning disable RA0004
            var result = task.Result;
#pragma warning restore RA0004
            if (result.Result != PathResult.Path || result.Path.Count == 0 || !result.Path[^1].Coordinates.IsValid(EntityManager))
            {
                FinishRoute(ent, false);
                return false;
            }
            var point = result.Path[^1].Coordinates;
            // Random fallback can contain only the starting polygon in a sealed room.
            // Remaining at the place where we were hit is not a successful retreat.
            if (ent.Comp.Route != RotDefenderRoute.Wander
                && Transform(ent).Coordinates.TryDistance(EntityManager, point, out var remaining) && remaining <= 0.7f)
            {
                FinishRoute(ent, false);
                return false;
            }
            ent.Comp.EscapePoint = point;
            var steering = _steering.Register(ent, ent.Comp.EscapePoint.Value);
            steering.Flags = PathFlags.Interact;
            steering.Range = 0.5f;
            steering.CurrentPath = new(result.Path);
            ResetProgress(ent);
        }
        if (ent.Comp.EscapePoint is not { } destination)
            return false;
        if (Transform(ent).Coordinates.TryDistance(EntityManager, destination, out var distance) && distance <= 0.7f)
        {
            FinishRoute(ent, true);
            return false;
        }
        if (TryComp<NPCSteeringComponent>(ent, out var current) && current.Status == SteeringStatus.NoPath)
        {
            FinishRoute(ent, false);
            return false;
        }
        if (HandleStuckMovement(ent, destination, 0.5f))
            return true;
        if (!HasComp<NPCSteeringComponent>(ent))
            Move(ent, destination, 0.5f);
        return true;
    }

    private void FinishRoute(Entity<RotDefenderComponent> ent, bool arrived)
    {
        var route = ent.Comp.Route;
        CancelRoute(ent);
        _steering.Unregister(ent);
        if (arrived)
        {
            ent.Comp.MovementRequested = false;
            ResetProgress(ent);
        }
        ent.Comp.Route = route;
        ent.Comp.RouteFailed = !arrived;
        var delay = arrived ? route switch
        {
            RotDefenderRoute.Colony => ent.Comp.ColonyRestDuration,
            RotDefenderRoute.Cover when IsCovered(ent, Transform(ent).Coordinates) => ent.Comp.RestDuration,
            _ => ent.Comp.ThinkInterval,
        } : ent.Comp.ThinkInterval;
        ent.Comp.RestUntil = _timing.CurTime + delay;
    }

    public void CancelRoute(Entity<RotDefenderComponent> ent)
    {
        ent.Comp.PathCancellation?.Cancel();
        ent.Comp.PathCancellation?.Dispose();
        ent.Comp.PathCancellation = null;
        ent.Comp.EscapePath = null;
        ent.Comp.EscapePoint = null;
        ent.Comp.Route = RotDefenderRoute.None;
        ent.Comp.RouteFailed = false;
    }

    public void ResetProgress(Entity<RotDefenderComponent> ent)
    {
        ent.Comp.LastPosition = Transform(ent).Coordinates;
        ent.Comp.LastMoved = _timing.CurTime;
        ent.Comp.ObstructionSince = null;
        ent.Comp.ClearingObstacle = false;
        ent.Comp.RepathedObstruction = false;
    }

    public bool HandleStuckMovement(Entity<RotDefenderComponent> ent, EntityCoordinates? destination, float range)
    {
        var now = _timing.CurTime;
        var coordinates = Transform(ent).Coordinates;
        if (!ent.Comp.LastPosition.TryDistance(EntityManager, coordinates, out var moved) || moved > 0.25f
            || destination is { } goal && coordinates.TryDistance(EntityManager, goal, out var distance) && distance <= range
                && _interaction.InRangeUnobstructed(ent, goal, range))
        {
            ResetProgress(ent);
            return false;
        }
        if (now - ent.Comp.LastMoved < ent.Comp.StuckDelay)
            return false;
        if (ent.Comp.RepathBeforeStrike && !ent.Comp.RepathedObstruction)
        {
            ent.Comp.RepathedObstruction = true;
            ent.Comp.LastMoved = now;
            _steering.Unregister(ent);
            if (destination is { } retry)
                Move(ent, retry, range);
            return true;
        }
        ent.Comp.ObstructionSince ??= now;
        if (now - ent.Comp.ObstructionSince >= ent.Comp.DetourDelay)
        {
            if (ent.Comp.Target is { } target)
            {
                if (ent.Comp.DetouredTarget == target)
                {
                    RejectEnemy(ent, target);
                    BeginRoute(ent, ent.Comp.Threat, RotDefenderRoute.Cover);
                    return true;
                }
                ent.Comp.DetouredTarget = target;
            }
            BeginRoute(ent, ent.Comp.Threat, RotDefenderRoute.Detour);
            return true;
        }
        if (!_groundStrike.HasObstacle(ent, ent.Comp.StuckTileRadius))
            return false;
        RemCompDeferred<NPCMeleeCombatComponent>(ent);
        _steering.Unregister(ent);
        _combat.SetInCombatMode(ent, true);
        ent.Comp.ClearingObstacle = true;
        if (now < ent.Comp.NextStuckAttack)
            return true;
        if (_groundStrike.TryStrike(ent, ent.Comp.StuckTileRadius, ent.Comp.StuckDamage, ent.Comp.StuckSound))
            ent.Comp.NextStuckAttack = now + ent.Comp.StuckAttackInterval;
        return true;
    }
}

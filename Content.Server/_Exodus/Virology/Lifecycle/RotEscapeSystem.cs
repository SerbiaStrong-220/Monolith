using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Content.Server.NPC.Pathfinding;
using Content.Shared.Interaction;
using Content.Shared.Maps;
using Content.Shared.Physics;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.Server._Exodus.Virology.Lifecycle;

/// <summary>Bounded, reachable escape routes shared by colony defenders and retaliation.</summary>
public sealed partial class RotEscapeSystem : EntitySystem
{
    [Dependency] private PathfindingSystem _pathfinding = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private TurfSystem _turf = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private SharedInteractionSystem _interaction = default!;

    private bool IgnoreCover(EntityUid uid) => !Transform(uid).Anchored;

    public async Task<PathResultEvent> FindPath(EntityUid ent, EntityUid threat, float range, float advanceRange,
        Vector2 directionHint, CancellationToken token, bool preferCover = false)
    {
        if (Transform(ent).GridUid is not { } grid || !TryComp<MapGridComponent>(grid, out var mapGrid))
            return new(PathResult.NoPath, []);

        var origin = _transform.GetMapCoordinates(ent);
        var enemy = _transform.GetMapCoordinates(threat);
        if (origin.MapId != enemy.MapId)
            return new(PathResult.NoPath, []);
        var localOrigin = _transform.ToCoordinates(grid, origin);
        var currentDistance = Vector2.Distance(origin.Position, enemy.Position);
        var candidates = new List<(EntityCoordinates Point, float Score)>(16);
        EntityCoordinates? breakThrough = null;
        PathResultEvent? shortPath = null;
        // At most 64 tile samples, 12 open routes and one fallback through a breakable obstruction.
        for (var ring = 0; ring < 4; ring++)
        {
            token.ThrowIfCancellationRequested();
            if (TerminatingOrDeleted(ent) || TerminatingOrDeleted(grid) || TerminatingOrDeleted(threat))
                return new(PathResult.NoPath, []);
            candidates.Clear();
            var radius = range / (1 << ring);
            for (var i = 0; i < 16; i++)
            {
                var angle = i * MathF.Tau / 16;
                var local = localOrigin.Offset(new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * radius);
                var tile = _map.GetTileRef(grid, mapGrid, _map.TileIndicesFor(grid, mapGrid, local));
                if (_turf.IsSpace(tile) || _turf.IsTileBlocked(tile, CollisionGroup.Impassable))
                    continue;
                var point = _map.GridTileToLocal(grid, mapGrid, tile.GridIndices);
                var position = _transform.ToMapCoordinates(point).Position;
                var distance = Vector2.Distance(position, enemy.Position);
                if (distance <= currentDistance + 0.5f
                    || Vector2.Dot(position - origin.Position, origin.Position - enemy.Position) < -0.01f)
                    continue;
                var direction = Vector2.Normalize(position - origin.Position);
                // Distance takes priority; keeping the chosen heading breaks otherwise equal left/right choices.
                var score = distance + Vector2.Dot(direction, directionHint) * 2f;
                if (preferCover && !_interaction.InRangeUnobstructed(threat, point, 0,
                    collisionMask: CollisionGroup.Opaque, predicate: IgnoreCover))
                    score += range * 4;
                candidates.Add((point, score));
            }
            candidates.Sort((a, b) => b.Score.CompareTo(a.Score));
            if (candidates.Count > 0)
                breakThrough ??= candidates[0].Point;
            for (var i = 0; i < Math.Min(3, candidates.Count); i++)
            {
                token.ThrowIfCancellationRequested();
                if (TerminatingOrDeleted(ent) || TerminatingOrDeleted(grid) || TerminatingOrDeleted(threat))
                    return new(PathResult.NoPath, []);
                var path = await _pathfinding.GetPath(ent, Transform(ent).Coordinates, candidates[i].Point,
                    0.4f, token, flags: PathFlags.Interact);
                token.ThrowIfCancellationRequested();
                if (TerminatingOrDeleted(ent) || TerminatingOrDeleted(grid) || TerminatingOrDeleted(threat))
                    return new(PathResult.NoPath, []);
                if (!PathAvoidsThreat(path, enemy, currentDistance))
                    continue;
                if (radius > advanceRange * 2 || breakThrough == candidates[i].Point)
                    return path;
                // A tiny pocket beside a blocked exit is not a sustained escape route.
                // Prefer breaking through towards open floor over running into that pocket and turning back.
                shortPath ??= path;
            }
        }
        token.ThrowIfCancellationRequested();
        if (breakThrough is { } blocked && !TerminatingOrDeleted(ent) && !TerminatingOrDeleted(grid))
        {
            var path = await _pathfinding.GetPath(ent, Transform(ent).Coordinates, blocked,
                0.4f, token, flags: PathFlags.Interact | PathFlags.Smashing);
            token.ThrowIfCancellationRequested();
            if (!TerminatingOrDeleted(ent) && !TerminatingOrDeleted(threat) && PathAvoidsThreat(path, enemy, currentDistance))
                return path;
        }
        return shortPath ?? new(PathResult.NoPath, []);
    }

    public bool PathAvoidsThreat(PathResultEvent path, MapCoordinates enemy, float distance)
    {
        if (path.Result != PathResult.Path || path.Path.Count == 0)
            return false;
        // A far endpoint behind the shooter is not an escape route. Allow a small step around corners.
        var minimum = MathF.Max(0, distance - 1f);
        foreach (var node in path.Path)
        {
            if (!node.Coordinates.IsValid(EntityManager))
                return false;
            var point = _transform.ToMapCoordinates(node.Coordinates);
            if (point.MapId != enemy.MapId || Vector2.DistanceSquared(point.Position, enemy.Position) < minimum * minimum)
                return false;
        }
        return true;
    }

    public bool IsFurtherFromThreat(EntityUid source, EntityUid threat, EntityCoordinates point)
    {
        if (TerminatingOrDeleted(source) || TerminatingOrDeleted(threat) || !point.IsValid(EntityManager))
            return false;
        var origin = _transform.GetMapCoordinates(source);
        var enemy = _transform.GetMapCoordinates(threat);
        var destination = _transform.ToMapCoordinates(point);
        return origin.MapId == enemy.MapId && destination.MapId == origin.MapId
            && Vector2.Distance(destination.Position, enemy.Position) > Vector2.Distance(origin.Position, enemy.Position) + 0.5f;
    }
}

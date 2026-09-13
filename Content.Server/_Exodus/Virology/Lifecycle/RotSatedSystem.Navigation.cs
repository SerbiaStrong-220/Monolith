using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Content.Server.NPC.Components;
using Content.Server.NPC.Pathfinding;
using Content.Shared.Maps;
using Content.Shared.Physics;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.Server._Exodus.Virology.Lifecycle;

public sealed partial class RotSatedSystem
{
    [Dependency] private PathfindingSystem _pathfinding = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private TurfSystem _turf = default!;

    private void BeginRoute(Entity<RotSatedComponent> ent, EntityUid? threat)
    {
        CancelRoute(ent);
        RemComp<NPCMeleeCombatComponent>(ent);
        _steering.Unregister(ent);
        ent.Comp.Escaping = threat != null;
        ent.Comp.MoveUntil = _timing.CurTime + TimeSpan.FromSeconds(30);
        ent.Comp.PathCancellation = new CancellationTokenSource();
        ent.Comp.EscapePath = threat is { } enemy
            ? FindEscapePath(ent, enemy, ent.Comp.PathCancellation.Token)
            : _pathfinding.GetRandomPath(ent, ent.Comp.SearchRange / 2, ent.Comp.PathCancellation.Token, limit: 100);
    }

    private async Task<PathResultEvent> FindEscapePath(Entity<RotSatedComponent> ent, EntityUid threat, CancellationToken token)
    {
        var origin = _transform.GetMapCoordinates(ent);
        var away = origin.Position - _transform.GetMapCoordinates(threat).Position;
        if (away.LengthSquared() < 0.01f)
            away = Vector2.UnitX;
        var direction = MathF.Atan2(away.Y, away.X);
        if (Transform(ent).GridUid is { } grid && TryComp<MapGridComponent>(grid, out var mapGrid))
        {
            // First try a long escape, then nearer cover. Every candidate uses the native pathfinder.
            for (var ring = 0; ring < 2; ring++)
            {
                for (var i = 0; i < 7; i++)
                {
                    token.ThrowIfCancellationRequested();
                    if (TerminatingOrDeleted(ent) || TerminatingOrDeleted(grid))
                        return new PathResultEvent(PathResult.NoPath, []);
                    var angle = direction + (i + 1) / 2 * MathF.PI / 3 * (i % 2 == 0 ? 1 : -1);
                    var radius = ent.Comp.EscapeRange / (ring + 1);
                    var point = _transform.ToCoordinates(grid, new MapCoordinates(origin.Position
                        + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * radius, origin.MapId));
                    var tile = _map.GetTileRef(grid, mapGrid, _map.TileIndicesFor(grid, mapGrid, point));
                    if (_turf.IsSpace(tile) || _turf.IsTileBlocked(tile, CollisionGroup.Impassable))
                        continue;
                    var path = await _pathfinding.GetPath(ent, Transform(ent).Coordinates, point, 0.5f, token,
                        flags: PathFlags.Interact);
                    if (path.Result == PathResult.Path && path.Path.Count > 0)
                        return path;
                }
            }
        }
        return await _pathfinding.GetRandomPath(ent, ent.Comp.EscapeRange, token, limit: 100, flags: PathFlags.Interact);
    }

    private bool UpdateRoute(Entity<RotSatedComponent> ent)
    {
        if (ent.Comp.EscapePath == null && ent.Comp.EscapePoint == null)
            return false;
        if (_timing.CurTime >= ent.Comp.MoveUntil)
        {
            FinishRoute(ent);
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
                FinishRoute(ent);
                return true;
            }
            // The completed task is read without blocking, as in the native pathfinding system.
#pragma warning disable RA0004
            var result = task.Result;
#pragma warning restore RA0004
            if (result.Result != PathResult.Path || result.Path.Count == 0)
            {
                FinishRoute(ent);
                return true;
            }
            var path = result.Path;
            var point = path[^1].Coordinates;
            if (!point.IsValid(EntityManager))
            {
                FinishRoute(ent);
                return true;
            }
            ent.Comp.EscapePoint = point;
            var steering = _steering.Register(ent, point);
            steering.Flags = PathFlags.Interact;
            steering.Range = 0.5f;
            steering.CurrentPath = new(path);
        }

        if (!TryComp<NPCSteeringComponent>(ent, out var current) || current.Status != SteeringStatus.Moving)
        {
            FinishRoute(ent);
            return false;
        }
        return true;
    }

    private void FinishRoute(Entity<RotSatedComponent> ent)
    {
        var escaped = ent.Comp.Escaping;
        CancelRoute(ent);
        _steering.Unregister(ent);
        ent.Comp.RestUntil = _timing.CurTime + (escaped ? ent.Comp.RestDuration : ent.Comp.ThinkInterval);
    }

    private void CancelRoute(Entity<RotSatedComponent> ent)
    {
        ent.Comp.PathCancellation?.Cancel();
        ent.Comp.PathCancellation?.Dispose();
        ent.Comp.PathCancellation = null;
        ent.Comp.EscapePath = null;
        ent.Comp.EscapePoint = null;
        ent.Comp.Escaping = false;
    }
}

using System.Threading;
using System.Threading.Tasks;
using Content.Server.NPC.Components;
using Content.Server.NPC.Pathfinding;
using Content.Shared._Exodus.Virology.Lifecycle;
using Content.Shared.Physics;
using Content.Shared.Tag;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Random;

namespace Content.Server._Exodus.Virology.Lifecycle;

public sealed partial class RotNestSystem
{
    [Dependency] private PathfindingSystem _pathfinding = default!;
    [Dependency] private IRobustRandom _random = default!;
    private readonly HashSet<Entity<RotLarvaComponent>> _pupae = [];
    private static readonly Vector2i[] Neighbours = [new(0, 1), new(0, -1), new(1, 0), new(-1, 0)];

    private void CancelShelter(Entity<RotLarvaComponent> ent)
    {
        if (!TryComp<RotLarvaShelterComponent>(ent, out var shelter))
            return;
        shelter.Cancellation?.Cancel();
        shelter.Cancellation?.Dispose();
        shelter.Cancellation = null;
        shelter.Path = null;
        shelter.Destination = null;
        _steering.Unregister(ent);
    }

    private void UpdateShelter(Entity<RotLarvaComponent> ent)
    {
        ent.Comp.PupateBy ??= _timing.CurTime + TimeSpan.FromSeconds(_random.NextDouble(
            ent.Comp.ShelterSearchMin.TotalSeconds, ent.Comp.ShelterSearchMax.TotalSeconds));
        if (_timing.CurTime >= ent.Comp.PupateBy)
        {
            if (PupaOccupies(ent, Transform(ent).Coordinates) && TryLeaveOccupiedTile(ent))
                return;
            Pupate(ent);
            return;
        }
        if (!HasComp<RotLarvaShelterComponent>(ent)
            && ShelterWalls(ent, Transform(ent).Coordinates) >= ent.Comp.PreferredShelterWalls
            && !PupaOccupies(ent, Transform(ent).Coordinates))
        {
            Pupate(ent);
            return;
        }
        var shelter = EnsureComp<RotLarvaShelterComponent>(ent);
        if (shelter.Path is { } task)
        {
            if (!task.IsCompleted)
                return;
            shelter.Path = null;
            if (!task.IsCompletedSuccessfully)
            {
                _ = task.Exception;
                CancelShelter(ent);
                return;
            }
#pragma warning disable RA0004
            var result = task.Result;
#pragma warning restore RA0004
            if (result.Result != PathResult.Path || result.Path.Count == 0
                || !result.Path[^1].Coordinates.IsValid(EntityManager))
            {
                CancelShelter(ent);
                return;
            }
            shelter.Destination = result.Path[^1].Coordinates;
            var steering = _steering.Register(ent, shelter.Destination.Value);
            steering.Range = 0.25f;
            steering.Flags = PathFlags.None;
            steering.CurrentPath = new(result.Path);
        }
        if (shelter.Destination is { } destination)
        {
            if (PupaOccupies(ent, destination))
            {
                CancelShelter(ent);
                if (!PupaOccupies(ent, Transform(ent).Coordinates))
                    Pupate(ent);
                return;
            }
            if (Transform(ent).Coordinates.TryDistance(EntityManager, destination, out var distance) && distance < 0.4f)
            {
                if (ShelterWalls(ent, destination) >= ent.Comp.MinimumShelterWalls && !PupaOccupies(ent, destination))
                    Pupate(ent);
                else
                    CancelShelter(ent);
                return;
            }
            if (!destination.IsValid(EntityManager) || !TryComp<NPCSteeringComponent>(ent, out var steering)
                || steering.Status == SteeringStatus.NoPath || _timing.CurTime >= shelter.RetryAt)
                CancelShelter(ent);
            return;
        }
        if (_timing.CurTime < shelter.RetryAt)
            return;
        shelter.RetryAt = _timing.CurTime + TimeSpan.FromSeconds(8);
        shelter.Cancellation = new CancellationTokenSource();
        shelter.Path = FindPupationPath(ent, shelter.Cancellation.Token);
    }

    private async Task<PathResultEvent> FindPupationPath(Entity<RotLarvaComponent> ent, CancellationToken token)
    {
        if (Transform(ent).GridUid is not { } grid || !TryComp<MapGridComponent>(grid, out var mapGrid))
            return new(PathResult.NoPath, []);
        var center = _map.TileIndicesFor(grid, mapGrid, Transform(ent).Coordinates);
        var candidates = new List<(EntityCoordinates Point, int Walls, float Distance)>();
        var radius = (int)MathF.Ceiling(ent.Comp.SearchRange / mapGrid.TileSize);
        for (var x = -radius; x <= radius; x++)
        {
            for (var y = -radius; y <= radius; y++)
            {
                var point = _map.GridTileToLocal(grid, mapGrid, center + new Vector2i(x, y));
                var walls = ShelterWalls(ent, point);
                if (walls < ent.Comp.MinimumShelterWalls || PupaOccupies(ent, point))
                    continue;
                candidates.Add((point, walls, x * x + y * y));
            }
        }
        candidates.Sort((a, b) => a.Walls != b.Walls ? b.Walls.CompareTo(a.Walls) : a.Distance.CompareTo(b.Distance));
        var attempts = Math.Min(candidates.Count, 12);
        for (var i = 0; i < attempts; i++)
        {
            token.ThrowIfCancellationRequested();
            if (TerminatingOrDeleted(ent) || TerminatingOrDeleted(grid))
                break;
            var path = await _pathfinding.GetPath(ent, Transform(ent).Coordinates, candidates[i].Point, 0.25f, token);
            token.ThrowIfCancellationRequested();
            if (TerminatingOrDeleted(ent))
                return new(PathResult.NoPath, []);
            if (path.Result == PathResult.Path && path.Path.Count > 0)
                return path;
        }
        return new(PathResult.NoPath, []);
    }

    public int ShelterWalls(Entity<RotLarvaComponent> ent, EntityCoordinates point)
    {
        if (!point.IsValid(EntityManager) || Transform(ent).GridUid is not { } grid
            || !TryComp<MapGridComponent>(grid, out var mapGrid))
            return 0;
        var center = _map.TileIndicesFor(grid, mapGrid, point);
        var tile = _map.GetTileRef(grid, mapGrid, center);
        if (_turf.IsSpace(tile) || _turf.IsTileBlocked(tile, CollisionGroup.Impassable))
            return 0;
        var count = 0;
        foreach (var offset in Neighbours)
        {
            var walls = _map.GetAnchoredEntitiesEnumerator(grid, mapGrid, center + offset);
            while (walls.MoveNext(out var wall))
            {
                if (!_whitelist.IsValid(ent.Comp.ShelterWalls, wall.Value))
                    continue;
                count++;
                break;
            }
        }
        return count;
    }

    private bool PupaOccupies(Entity<RotLarvaComponent> ent, EntityCoordinates point)
    {
        if (!point.IsValid(EntityManager) || Transform(ent).GridUid is not { } grid || !TryComp<MapGridComponent>(grid, out var mapGrid))
            return false;
        var indices = _map.TileIndicesFor(grid, mapGrid, point);
        _pupae.Clear();
        _lookup.GetEntitiesInRange(_transform.ToMapCoordinates(point), mapGrid.TileSize, _pupae);
        foreach (var (uid, larva) in _pupae)
        {
            if (uid != ent.Owner && larva.HatchAt != null && !_mobs.IsDead(uid)
                && Transform(uid).GridUid == grid && _map.TileIndicesFor(grid, mapGrid, Transform(uid).Coordinates) == indices)
                return true;
        }
        return false;
    }

    private bool TryLeaveOccupiedTile(Entity<RotLarvaComponent> ent)
    {
        if (Transform(ent).GridUid is not { } grid || !TryComp<MapGridComponent>(grid, out var mapGrid))
            return false;
        var center = _map.TileIndicesFor(grid, mapGrid, Transform(ent).Coordinates);
        foreach (var offset in Neighbours)
        {
            var tile = _map.GetTileRef(grid, mapGrid, center + offset);
            var point = _map.GridTileToLocal(grid, mapGrid, center + offset);
            if (_turf.IsSpace(tile) || _turf.IsTileBlocked(tile, CollisionGroup.Impassable)
                || PupaOccupies(ent, point) || !_interaction.InRangeUnobstructed(ent, point, mapGrid.TileSize * 2))
                continue;
            var steering = _steering.Register(ent, point);
            steering.Range = 0.25f;
            steering.Flags = PathFlags.None;
            return true;
        }
        return false;
    }

    private void Pupate(Entity<RotLarvaComponent> ent)
    {
        CancelShelter(ent);
        ent.Comp.PupateBy = null;
        ent.Comp.HatchAt = _timing.CurTime + ent.Comp.HatchDelay;
        _steering.Unregister(ent);
        _movement.RefreshMovementSpeedModifiers(ent);
        UpdateLarvaVisual(ent);
        if (ent.Comp.PupaName is { } name)
            _metadata.SetEntityName(ent, Loc.GetString(name));
    }
}

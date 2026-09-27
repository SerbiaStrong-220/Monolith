using System.Numerics;
using Content.Shared._Exodus.Mining.AutoMining;
using Robust.Shared.Map.Components;

namespace Content.Server._Exodus.Mining.AutoMining;

public sealed partial class BulkAutoMiningSystem
{
    private const int MaxRangeTileChecks = 64;
    private static readonly TimeSpan RangeCheckInterval = TimeSpan.FromSeconds(1);

    private bool CheckTargetRange(Entity<BulkAutoMiningConsoleComponent> console, BulkAutoMiningJobComponent job)
    {
        var checks = MaxRangeTileChecks;
        var pending = false;
        var rangeSquared = console.Comp.MaxRange * console.Comp.MaxRange;
        var pairs = job.GridJobs.Count * job.Emitters.Count;
        var startIndex = job.NextRangePairIndex;
        for (var offset = 0; offset < pairs; offset++)
        {
            var pairIndex = (startIndex + offset) % pairs;
            var gridJob = job.GridJobs[pairIndex / job.Emitters.Count];
            if (gridJob.RemainingTiles.Count == 0)
                continue;

            var gridUid = gridJob.GridUid;
            if (TerminatingOrDeleted(gridUid) || !_gridQuery.TryComp(gridUid, out var grid) ||
                !_xformQuery.TryComp(gridUid, out var gridXform))
            {
                StopMining(console, "bulk-auto-mining-stopped-invalid-grid");
                return false;
            }

            var emitterIndex = pairIndex % job.Emitters.Count;
            var emitterUid = job.Emitters[emitterIndex];
            if (TerminatingOrDeleted(emitterUid) || !_emitterQuery.TryComp(emitterUid, out var emitter) ||
                emitter.Controller != console.Owner || !_xformQuery.TryComp(emitterUid, out var emitterXform) ||
                emitterXform.MapUid == null || emitterXform.MapUid != gridXform.MapUid || emitterXform.GridUid == gridUid)
                continue;

            var position = Vector2.Transform(_transform.GetWorldPosition(emitterXform), _transform.GetInvWorldMatrix(gridXform));
            var nearest = Vector2.Clamp(position, grid.LocalAABB.BottomLeft, grid.LocalAABB.TopRight);
            // All distant targets can be rejected in this update, even if a detailed search is unfinished.
            if (Vector2.DistanceSquared(position, nearest) > rangeSquared)
                continue;

            ref var search = ref gridJob.RangeSearches[emitterIndex];
            // A live beam supplies a witness immediately. Keep it when power, obstacles or a full buffer stop the beam.
            var checksBefore = checks;
            var candidate = emitter.BeamGrid == gridUid ? emitter.BeamTile : search.CachedTile;
            if (candidate is { } cached)
            {
                if (checks == 0)
                {
                    pending = true;
                    continue;
                }

                checks--;
                if (gridJob.RemainingTiles.Contains(cached) && IsRemainingTileInRange((gridUid, grid), cached, position, rangeSquared))
                {
                    search.CachedTile = cached;
                    return true;
                }

                search.CachedTile = null;
                search.NextTileIndex = 0;
            }

            if (search.NextTileIndex == gridJob.RangeTiles.Count)
            {
                if (IsRangeSearchOutOfReach(search, position, console.Comp.MaxRange))
                    continue;

                search.NextTileIndex = 0;
            }

            if (search.NextTileIndex == 0)
            {
                search.Origin = position;
                search.MinimumDistanceSquared = float.PositiveInfinity;
            }

            while (checks > 0 && search.NextTileIndex < gridJob.RangeTiles.Count)
            {
                checks--;
                var tile = gridJob.RangeTiles[search.NextTileIndex++];
                if (!gridJob.RemainingTiles.Contains(tile))
                    continue;

                if (_map.GetTileRef(gridUid, grid, tile).Tile.IsEmpty)
                {
                    gridJob.RemainingTiles.Remove(tile);
                    console.Comp.ProcessedTiles++;
                    continue;
                }

                var center = _map.GridTileToLocal(gridUid, grid, tile).Position;
                search.MinimumDistanceSquared = Math.Min(search.MinimumDistanceSquared, Vector2.DistanceSquared(search.Origin, center));
                if (Vector2.DistanceSquared(position, center) <= rangeSquared)
                {
                    search.CachedTile = tile;
                    return true;
                }
            }

            // Rotate the shared budget across pairs so a large target cannot starve other lasers or targets.
            if (checksBefore > 0 && checks == 0)
                job.NextRangePairIndex = (pairIndex + 1) % pairs;

            pending |= search.NextTileIndex < gridJob.RangeTiles.Count ||
                       !IsRangeSearchOutOfReach(search, position, console.Comp.MaxRange);
        }

        // An incomplete search is not evidence that every remaining tile is out of reach.
        if (pending)
            return true;

        var hasRemainingTiles = false;
        foreach (var grid in job.GridJobs)
            hasRemainingTiles |= grid.RemainingTiles.Count > 0;

        StopMining(console, hasRemainingTiles ? "bulk-auto-mining-stopped-out-of-range" : "bulk-auto-mining-complete");
        return false;
    }

    private static bool IsRangeSearchOutOfReach(BulkAutoMiningRangeSearch search, Vector2 position, float range)
    {
        // Tiles are scanned relative to one origin. Allow for movement/rotation since that scan started;
        // the triangle inequality makes a cached negative result safe without restarting on every movement.
        var maximumDistance = range + Vector2.Distance(search.Origin, position);
        return search.MinimumDistanceSquared > maximumDistance * maximumDistance;
    }

    private bool IsRemainingTileInRange(Entity<MapGridComponent> grid, Vector2i tile, Vector2 position, float rangeSquared)
    {
        return Vector2.DistanceSquared(position, _map.GridTileToLocal(grid, grid.Comp, tile).Position) <= rangeSquared &&
               !_map.GetTileRef(grid, grid.Comp, tile).Tile.IsEmpty;
    }
}

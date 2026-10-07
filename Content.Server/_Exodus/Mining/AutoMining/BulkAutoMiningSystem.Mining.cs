using System.Numerics;
using Content.Server.Gatherable.Components;
using Content.Shared._Exodus.CCVar;
using Content.Shared._Exodus.Mining.AutoMining;
using Content.Shared.Mining.Components;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.Server._Exodus.Mining.AutoMining;

public sealed partial class BulkAutoMiningSystem
{
    private const int MaxTilesPerTick = 64;

    /// <summary>Surface tiles examined per search, nearest first. Most checks are cheap local safety tests.</summary>
    private const int MaxSearchCandidates = 96;

    /// <summary>Line-of-sight raycasts per search.</summary>
    private const int MaxSearchRays = 12;

    private static readonly TimeSpan TargetSearchInterval = TimeSpan.FromSeconds(0.5);

    /// <summary>How long an obstructed tile is skipped, so later searches reach further visible tiles.</summary>
    private static readonly TimeSpan BlockedTileMemory = TimeSpan.FromSeconds(5);

    private readonly PriorityQueue<SearchNode, float> _searchQueue = new();
    private readonly List<SearchGrid> _searchGrids = new();

    private void ProcessMiningTick(Entity<BulkAutoMiningConsoleComponent> console, BulkAutoMiningJobComponent job)
    {
        if (!IsPoweredAndAnchored(console))
        {
            StopMining(console, "bulk-auto-mining-stopped-power");
            return;
        }

        var now = _timing.CurTime;
        if (now >= job.NextRangeCheckTime)
        {
            job.NextRangeCheckTime = now + RangeCheckInterval;
            if (!CheckTargetRange(console, job))
                return;
        }

        var budget = console.Comp.TilesPerTick > 0 ? console.Comp.TilesPerTick : _cfg.GetCVar(EXCVars.BulkMiningTilesPerTick);
        budget = Math.Clamp(budget, 1, MaxTilesPerTick);
        var interval = GetProcessInterval(console);
        job.NextProcessTime = now + TargetSearchInterval;

        foreach (var emitterUid in job.Emitters)
        {
            if (!_emitterQuery.TryComp(emitterUid, out var emitter))
                continue;

            // A console can display busy emitters but must never clear or fire their beams.
            if (emitter.Controller != console.Owner)
                continue;

            var status = GetEmitterStatus(console, emitterUid);
            if (status != BulkAutoMiningLaserStatus.Ready)
            {
                job.Statuses[emitterUid] = status;
                ClearBeam((emitterUid, emitter));
                continue;
            }

            if (emitter.NextTargetSearchTime > now && emitter.NextTargetSearchTime < job.NextProcessTime)
                job.NextProcessTime = emitter.NextTargetSearchTime;

            if (emitter.BeamGrid == null && now < emitter.NextTargetSearchTime)
            {
                KeepWaitingStatus(job, emitterUid);
                continue;
            }

            job.Statuses[emitterUid] = BulkAutoMiningLaserStatus.Ready;
            var mined = false;
            var tileBudget = now >= emitter.NextMiningTime ? budget : 0;
            for (var i = 0; i < tileBudget; i++)
            {
                if (!TrySelectTarget(console, job, (emitterUid, emitter), out var gridIndex, out var grid, out var tile) ||
                    !TryProcessTile(console, job, (emitterUid, emitter), gridIndex, grid, tile))
                    break;

                mined = true;
                if (!console.Comp.Active || TerminatingOrDeleted(emitterUid))
                    return;
            }

            if (!console.Comp.Active)
                return;

            // Only excavation advances this cooldown; searches and missed cycles cannot grant extra metal.
            if (mined)
                emitter.NextMiningTime = now + interval;

            if (emitter.NextMiningTime > now && emitter.NextMiningTime < job.NextProcessTime)
                job.NextProcessTime = emitter.NextMiningTime;

            if (emitter.NextTargetSearchTime > now && emitter.NextTargetSearchTime < job.NextProcessTime)
                job.NextProcessTime = emitter.NextTargetSearchTime;
        }

        // Aim after every laser has excavated, so another laser cannot leave our beam on an empty tile.
        foreach (var emitterUid in job.Emitters)
        {
            if (!_emitterQuery.TryComp(emitterUid, out var emitter) || emitter.Controller != console.Owner ||
                !job.Statuses.TryGetValue(emitterUid, out var status))
                continue;

            if (status != BulkAutoMiningLaserStatus.Ready)
            {
                ClearBeam((emitterUid, emitter));
                continue;
            }

            if (TrySelectTarget(console, job, (emitterUid, emitter), out _, out var grid, out var tile))
            {
                SetBeam((emitterUid, emitter), grid, tile);
                job.Statuses[emitterUid] = BulkAutoMiningLaserStatus.Mining;
            }
            else
            {
                ClearBeam((emitterUid, emitter));
            }

            if (!console.Comp.Active)
                return;
        }

        // The aiming pass has just validated every surviving beam; avoid raycasting them again this tick.
        job.NextBeamCheckTime = now + BeamCheckInterval;

        TryFinishMining(console, job);
    }

    private static void KeepWaitingStatus(BulkAutoMiningJobComponent job, EntityUid emitter)
    {
        job.Statuses[emitter] = job.Statuses.TryGetValue(emitter, out var previous) && previous == BulkAutoMiningLaserStatus.Blocked
            ? BulkAutoMiningLaserStatus.Blocked
            : BulkAutoMiningLaserStatus.Searching;
    }

    /// <summary>
    /// Keeps a laser on its current tile while it stays valid, otherwise searches for the nearest reachable, safe
    /// surface tile. Peeling each target from the side facing the laser leaves smooth craters.
    /// </summary>
    private bool TrySelectTarget(
        Entity<BulkAutoMiningConsoleComponent> console,
        BulkAutoMiningJobComponent job,
        Entity<BulkAutoMiningEmitterComponent> emitter,
        out int gridIndex,
        out Entity<MapGridComponent> grid,
        out Vector2i tile)
    {
        gridIndex = -1;
        grid = default;
        tile = default;
        var incomplete = false;
        // An exhausted target may be deleted together with its last tile; it is complete, not lost.
        if (emitter.Comp.BeamGrid is { } beamGrid && TryGetGridJob(job, beamGrid, out var beamIndex) &&
            job.GridJobs[beamIndex].Remaining > 0)
        {
            var gridJob = job.GridJobs[beamIndex];
            if (TerminatingOrDeleted(beamGrid) || EntityManager.IsQueuedForDeletion(beamGrid) ||
                !_gridQuery.TryComp(beamGrid, out var beamGridComp) || !_xformQuery.HasComp(beamGrid))
            {
                InvalidateGridJob(console, job, gridJob);
            }
            else if (IsTargetTile((beamGrid, beamGridComp), emitter.Comp.BeamTile) &&
                     IsSafeToMine((beamGrid, beamGridComp), emitter.Comp.BeamTile, ref incomplete) &&
                     IsBeamClear(console, emitter, (beamGrid, beamGridComp), emitter.Comp.BeamTile))
            {
                gridIndex = beamIndex;
                grid = (beamGrid, beamGridComp);
                tile = emitter.Comp.BeamTile;
                return true;
            }
        }

        var now = _timing.CurTime;
        if (now < emitter.Comp.NextTargetSearchTime)
        {
            job.Statuses[emitter] = BulkAutoMiningLaserStatus.Searching;
            return false;
        }

        emitter.Comp.NextTargetSearchTime = now + TargetSearchInterval;
        if (TryFindTarget(console, job, emitter, ref incomplete, out gridIndex, out tile))
        {
            var uid = job.GridJobs[gridIndex].GridUid;
            grid = (uid, _gridQuery.GetComponent(uid));
            return true;
        }

        job.Statuses[emitter] = incomplete ? BulkAutoMiningLaserStatus.Searching : BulkAutoMiningLaserStatus.Blocked;
        return false;
    }

    private static bool TryGetGridJob(BulkAutoMiningJobComponent job, EntityUid grid, out int index)
    {
        for (index = 0; index < job.GridJobs.Count; index++)
        {
            var gridJob = job.GridJobs[index];
            if (gridJob.GridUid == grid && !gridJob.Invalidated)
                return true;
        }

        index = -1;
        return false;
    }

    /// <summary>
    /// Best-first search over the exposed surface of every target: 8x8 blocks are expanded in order of their
    /// distance to the laser, so the nearest candidates are examined first without sorting the whole surface.
    /// </summary>
    private bool TryFindTarget(
        Entity<BulkAutoMiningConsoleComponent> console,
        BulkAutoMiningJobComponent job,
        Entity<BulkAutoMiningEmitterComponent> emitter,
        ref bool incomplete,
        out int gridIndex,
        out Vector2i tile)
    {
        gridIndex = -1;
        tile = default;
        var emitterXform = _xformQuery.GetComponent(emitter);
        if (emitterXform.MapUid == null)
            return false;

        var now = _timing.CurTime;
        if (!job.BlockedTiles.TryGetValue(emitter, out var blocked))
        {
            blocked = new BulkAutoMiningBlockedTiles();
            job.BlockedTiles.Add(emitter, blocked);
        }

        if (now >= blocked.Expires)
        {
            blocked.Tiles.Clear();
            blocked.Expires = now + BlockedTileMemory;
        }

        var range = console.Comp.MaxRange;
        var origin = _transform.GetWorldPosition(emitterXform);
        _searchQueue.Clear();
        _searchGrids.Clear();
        for (var i = 0; i < job.GridJobs.Count; i++)
        {
            var gridJob = job.GridJobs[i];
            var searchGrid = new SearchGrid(gridJob.GridUid, null, Vector2.Zero, 1f, 0f);
            if (!gridJob.Invalidated && gridJob.Remaining > 0)
            {
                if (TerminatingOrDeleted(gridJob.GridUid) || EntityManager.IsQueuedForDeletion(gridJob.GridUid) ||
                    !_gridQuery.TryComp(gridJob.GridUid, out var gridComp) ||
                    !_xformQuery.TryComp(gridJob.GridUid, out var gridXform))
                {
                    InvalidateGridJob(console, job, gridJob);
                }
                else if (_surfaceQuery.TryComp(gridJob.GridUid, out var surface) && surface.Remaining > 0 &&
                         gridXform.MapUid == emitterXform.MapUid && emitterXform.GridUid != gridJob.GridUid)
                {
                    var local = Vector2.Transform(origin, _transform.GetInvWorldMatrix(gridXform));
                    // Spread lasers over several selected targets unless one is much closer.
                    var penalty = console.Comp.GridBalancePenalty * CountOtherBeams(job, emitter, gridJob.GridUid);
                    searchGrid = new SearchGrid(gridJob.GridUid, surface, local, gridComp.TileSize, penalty);
                    foreach (var (block, bits) in surface.Exposed)
                    {
                        var distance = BulkMiningSurfaceSystem.GetBlockDistance(block, gridComp.TileSize, local);
                        if (bits != 0 && distance <= range)
                            _searchQueue.Enqueue(new SearchNode(i, block, true), distance + penalty);
                    }
                }
            }

            _searchGrids.Add(searchGrid);
        }

        var candidates = MaxSearchCandidates;
        var rays = MaxSearchRays;
        var rangeSquared = range * range;
        while (_searchQueue.TryDequeue(out var node, out _))
        {
            var searchGrid = _searchGrids[node.Grid];
            if (searchGrid.Surface is not { } surface || job.GridJobs[node.Grid].Invalidated)
                continue;

            if (node.Block)
            {
                if (!BulkMiningSurfaceSystem.TryGetExposedBlock(surface, node.Index, out var bits))
                    continue;

                while (bits != 0)
                {
                    var bit = BitOperations.TrailingZeroCount(bits);
                    bits &= bits - 1;
                    var candidate = BulkMiningSurfaceSystem.GetTile(node.Index, bit);
                    var center = new Vector2(candidate.X + 0.5f, candidate.Y + 0.5f) * searchGrid.TileSize;
                    var distanceSquared = Vector2.DistanceSquared(searchGrid.Local, center);
                    if (distanceSquared <= rangeSquared)
                        _searchQueue.Enqueue(new SearchNode(node.Grid, candidate, false), MathF.Sqrt(distanceSquared) + searchGrid.Penalty);
                }

                continue;
            }

            if (candidates-- <= 0)
            {
                incomplete = true;
                break;
            }

            var gridUid = searchGrid.Grid;
            var target = node.Index;
            if (!BulkMiningSurfaceSystem.IsExposed(surface, target) || blocked.Tiles.Contains((gridUid, target)) ||
                IsClaimedByOtherBeam(job, emitter, gridUid, target))
                continue;

            var grid = (gridUid, _gridQuery.GetComponent(gridUid));
            if (!IsTargetTile(grid, target) || !IsSafeToMine(grid, target, ref incomplete))
                continue;

            if (rays-- <= 0)
            {
                incomplete = true;
                break;
            }

            if (!IsBeamClear(console, emitter, grid, target))
            {
                blocked.Tiles.Add((gridUid, target));
                continue;
            }

            gridIndex = node.Grid;
            tile = target;
            _searchQueue.Clear();
            return true;
        }

        _searchQueue.Clear();
        return false;
    }

    private int CountOtherBeams(BulkAutoMiningJobComponent job, EntityUid emitter, EntityUid grid)
    {
        var count = 0;
        foreach (var uid in job.Emitters)
        {
            if (uid != emitter && _emitterQuery.TryComp(uid, out var other) && other.BeamGrid == grid)
                count++;
        }

        return count;
    }

    private bool IsClaimedByOtherBeam(BulkAutoMiningJobComponent job, EntityUid emitter, EntityUid grid, Vector2i tile)
    {
        foreach (var uid in job.Emitters)
        {
            if (uid != emitter && _emitterQuery.TryComp(uid, out var other) && other.BeamGrid == grid && other.BeamTile == tile)
                return true;
        }

        return false;
    }

    private bool IsSafeToMine(Entity<MapGridComponent> grid, Vector2i tile, ref bool searchIncomplete)
    {
        // Forbidden grids retain their existing overload behavior; they are never excavated.
        if (!_deposits.IsInitialized(grid))
            return true;

        var safety = _connectivity.GetTileSafety(grid, tile);
        searchIncomplete |= safety == BulkMiningTileSafety.Pending;
        return safety == BulkMiningTileSafety.Safe;
    }

    private bool TryProcessTile(
        Entity<BulkAutoMiningConsoleComponent> console,
        BulkAutoMiningJobComponent job,
        Entity<BulkAutoMiningEmitterComponent> emitter,
        int gridIndex,
        Entity<MapGridComponent> grid,
        Vector2i tile)
    {
        // Only generated deposits authorize mining, independently of station membership or IFF.
        // Reject artificial grids before any payout or excavation.
        if (!_deposits.IsInitialized(grid))
        {
            _damageable.TryChangeDamage(emitter, emitter.Comp.ForbiddenTileDamage, ignoreResistances: true);
            StopMining(console, TerminatingOrDeleted(emitter) || EntityManager.IsQueuedForDeletion(emitter)
                ? "bulk-auto-mining-stopped-laser-overload"
                : "bulk-auto-mining-stopped-forbidden-target");
            return false;
        }

        if (!_deposits.CanMine(grid, tile) || _map.GetTileRef(grid, grid.Comp, tile).Tile.IsEmpty)
            return false;

        var amount = GetSlurryYield(emitter, emitter.Comp.SlurryPerTile.Next(_random));
        if (amount <= 0 || !_storageQuery.TryComp(emitter, out var storage) ||
            !_pipes.TryDepositMaterial((emitter, storage), emitter.Comp.SlurryMaterial, amount))
        {
            job.Statuses[emitter] = BulkAutoMiningLaserStatus.Full;
            return false;
        }

        emitter.Comp.NextTargetSearchTime = _timing.CurTime;
        SyncProgress(console, job);
        MineTile(console, emitter, grid, tile);

        // Count the cut explicitly: removing the last tile may delete the grid together with its surface.
        // The surface already dropped the tile synchronously, so later syncs see no difference.
        var gridJob = job.GridJobs[gridIndex];
        if (gridJob.Remaining > 0)
        {
            gridJob.Remaining--;
            console.Comp.ProcessedTiles++;
        }

        return true;
    }

    private void MineTile(
        Entity<BulkAutoMiningConsoleComponent> console,
        Entity<BulkAutoMiningEmitterComponent> emitter,
        Entity<MapGridComponent> grid,
        Vector2i tile)
    {
        var anchored = _map.GetAnchoredEntitiesEnumerator(grid, grid.Comp, tile);
        while (anchored.MoveNext(out var uid))
        {
            if (uid is not { } entity || TerminatingOrDeleted(entity))
                continue;

            if (HasComp<GatherableComponent>(entity))
            {
                if (TryComp<OreVeinComponent>(entity, out var vein))
                    vein.PreventSpawning = true;

                QueueDel(entity);
            }
            else if (_whitelist.IsWhitelistPass(console.Comp.ClearableWhitelist, entity))
                QueueDel(entity);
        }

        // The synchronous tile-change event also consumes the deposit before another laser can mine it.
        _map.SetTile(grid, grid.Comp, tile, Tile.Empty);
        SetBeam(emitter, grid, tile);
    }

    private void SetBeam(Entity<BulkAutoMiningEmitterComponent> emitter, EntityUid grid, Vector2i tile)
    {
        if (emitter.Comp.BeamGrid == grid && emitter.Comp.BeamTile == tile)
            return;

        if (emitter.Comp.BeamGrid == null)
        {
            SnapshotWarmup(emitter);
            emitter.Comp.StartupStream = _audio.PlayPvs(emitter.Comp.StartSound, emitter)?.Entity;
            _ambient.SetAmbience(emitter, true);
        }

        emitter.Comp.BeamGrid = grid;
        emitter.Comp.BeamTile = tile;
        Dirty(emitter);
    }

    /// <param name="Grid">Index into the job's grid list.</param>
    /// <param name="Index">Block indices for a block node, tile indices for a tile node.</param>
    private readonly record struct SearchNode(int Grid, Vector2i Index, bool Block);

    /// <param name="Local">The laser's position in the grid's local coordinates.</param>
    /// <param name="Penalty">Distance added to every candidate of this grid to balance lasers across targets.</param>
    private readonly record struct SearchGrid(
        EntityUid Grid,
        BulkMiningSurfaceComponent? Surface,
        Vector2 Local,
        float TileSize,
        float Penalty);
}

using Content.Server.Gatherable.Components;
using Content.Shared._Exodus.Mining.AutoMining;
using Content.Shared._Exodus.CCVar;
using Content.Shared.Mining.Components;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.Server._Exodus.Mining.AutoMining;

public sealed partial class BulkAutoMiningSystem
{
    private const int MaxTileChecksPerEmitter = 64;
    private static readonly TimeSpan TargetSearchInterval = TimeSpan.FromSeconds(0.5);

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
        budget = Math.Clamp(budget, 1, MaxTileChecksPerEmitter);
        var interval = GetProcessInterval(console.Comp);
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
                job.Statuses[emitterUid] = job.Statuses.TryGetValue(emitterUid, out var previous) && previous == BulkAutoMiningLaserStatus.Blocked
                    ? BulkAutoMiningLaserStatus.Blocked
                    : BulkAutoMiningLaserStatus.Searching;
                continue;
            }

            job.Statuses[emitterUid] = BulkAutoMiningLaserStatus.Ready;
            var checks = MaxTileChecksPerEmitter;
            var mined = false;
            var tileBudget = now >= emitter.NextMiningTime ? budget : 0;
            for (var i = 0; i < tileBudget && checks > 0; i++)
            {
                if (!TryProcessNextTile(console, job, (emitterUid, emitter), ref checks, true))
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

            job.TileChecksRemaining[emitterUid] = checks;
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

            var checks = job.TileChecksRemaining[emitterUid];
            if (TryProcessNextTile(console, job, (emitterUid, emitter), ref checks, false))
                job.Statuses[emitterUid] = BulkAutoMiningLaserStatus.Mining;
            else
                ClearBeam((emitterUid, emitter));

            if (!console.Comp.Active)
                return;
        }

        // The aiming pass has just validated every surviving beam; avoid raycasting them again this tick.
        job.NextBeamCheckTime = now + BeamCheckInterval;

        TryFinishMining(console, job);
    }

    private bool TryProcessNextTile(
        Entity<BulkAutoMiningConsoleComponent> console,
        BulkAutoMiningJobComponent job,
        Entity<BulkAutoMiningEmitterComponent> emitter,
        ref int checks,
        bool excavate)
    {
        var searchIncomplete = false;
        // Keep aiming at the selected intact tile instead of moving the beam on every search update.
        if (emitter.Comp.BeamGrid is { } beamGrid && checks > 0)
        {
            for (var index = 0; index < job.GridJobs.Count; index++)
            {
                var gridJob = job.GridJobs[index];
                if (gridJob.GridUid != beamGrid || !gridJob.RemainingTiles.Contains(emitter.Comp.BeamTile))
                    continue;

                if (TerminatingOrDeleted(beamGrid) || EntityManager.IsQueuedForDeletion(beamGrid) ||
                    !_gridQuery.TryComp(beamGrid, out var grid) || !_xformQuery.HasComp(beamGrid))
                {
                    InvalidateGridJob(console, job, gridJob);
                    break;
                }

                if (!IsTargetTile((beamGrid, grid), emitter.Comp.BeamTile))
                    continue;

                checks--;
                if (CanReachTile(console, emitter, (beamGrid, grid), emitter.Comp.BeamTile) &&
                    IsSafeToMine((beamGrid, grid), emitter.Comp.BeamTile, ref searchIncomplete))
                    return TryProcessTile(console, job, emitter, index, (beamGrid, grid), emitter.Comp.BeamTile, excavate);

                break;
            }
        }

        if (_timing.CurTime < emitter.Comp.NextTargetSearchTime)
        {
            job.Statuses[emitter] = BulkAutoMiningLaserStatus.Searching;
            return false;
        }

        emitter.Comp.NextTargetSearchTime = _timing.CurTime + TargetSearchInterval;
        for (var offset = 0; offset < job.GridJobs.Count; offset++)
        {
            if (checks <= 0)
            {
                searchIncomplete = true;
                break;
            }

            var gridIndex = (job.NextGridIndex + offset) % job.GridJobs.Count;
            var gridJob = job.GridJobs[gridIndex];
            if (gridJob.RemainingTiles.Count == 0)
                continue;

            if (TerminatingOrDeleted(gridJob.GridUid) || EntityManager.IsQueuedForDeletion(gridJob.GridUid) ||
                !_gridQuery.TryComp(gridJob.GridUid, out var grid) || !_xformQuery.HasComp(gridJob.GridUid))
            {
                InvalidateGridJob(console, job, gridJob);
                continue;
            }

            var attempts = Math.Min(Math.Max(1, checks / (job.GridJobs.Count - offset)), gridJob.Tiles.Count);
            searchIncomplete |= attempts < gridJob.Tiles.Count;
            for (var i = 0; i < attempts; i++)
            {
                if (checks <= 0)
                {
                    searchIncomplete = true;
                    break;
                }

                checks--;
                var tile = gridJob.Tiles.Dequeue();
                // A surface tile may have been excavated while aiming at a deeper queued tile.
                if (!gridJob.RemainingTiles.Contains(tile))
                    continue;

                if (!IsTargetTile((gridJob.GridUid, grid), tile))
                {
                    // Another miner or construction may have removed this tile's deposit.
                    gridJob.RemainingTiles.Remove(tile);
                    console.Comp.ProcessedTiles++;
                    continue;
                }

                if (!TryFindBeamTile(console, emitter, (gridJob.GridUid, grid), tile, ref checks, ref searchIncomplete, out var reachable) ||
                    !gridJob.RemainingTiles.Contains(reachable) ||
                    !IsTargetTile((gridJob.GridUid, grid), reachable) ||
                    !IsSafeToMine((gridJob.GridUid, grid), reachable, ref searchIncomplete))
                {
                    gridJob.Tiles.Enqueue(tile);
                    continue;
                }

                // A cached beam can mine this tile later; its stale queue entry is removed on the next visit.
                gridJob.Tiles.Enqueue(tile);
                return TryProcessTile(console, job, emitter, gridIndex, (gridJob.GridUid, grid), reachable, excavate);
            }
        }

        job.Statuses[emitter] = searchIncomplete
            ? BulkAutoMiningLaserStatus.Searching
            : BulkAutoMiningLaserStatus.Blocked;
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
        Vector2i tile,
        bool excavate)
    {
        var gridJob = job.GridJobs[gridIndex];
        if (!excavate)
        {
            SetBeam(emitter, grid, tile);
            return true;
        }

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
        if (amount <= 0 || !_materials.TryChangeMaterialAmount(emitter, emitter.Comp.SlurryMaterial, amount, localOnly: true))
        {
            job.Statuses[emitter] = BulkAutoMiningLaserStatus.Full;
            return false;
        }

        gridJob.RemainingTiles.Remove(tile);
        job.NextGridIndex = (gridIndex + 1) % job.GridJobs.Count;
        emitter.Comp.NextTargetSearchTime = _timing.CurTime;
        MineTile(console, emitter, grid, tile);
        console.Comp.ProcessedTiles++;
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
}

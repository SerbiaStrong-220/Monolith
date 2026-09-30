using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using Content.Server.Worldgen.Components;
using Content.Server.Worldgen.Systems;
using Content.Shared._Exodus.Mining.AutoMining;
using Robust.Shared.Map.Components;

namespace Content.Server._Exodus.Mining.AutoMining;

public sealed partial class BulkAutoMiningSystem
{
    public bool TrySelectGrid(Entity<BulkAutoMiningConsoleComponent> ent, EntityUid target)
    {
        if (ent.Comp.Active)
        {
            Popup(ent, "bulk-auto-mining-select-while-active");
            return false;
        }

        // Allow removing a stale selection even after it moves out of range.
        if (ent.Comp.SelectedGrids.Remove(target))
        {
            ent.Comp.TotalTiles = 0;
            ent.Comp.ProcessedTiles = 0;
            UpdateUi(ent);
            return true;
        }

        if (!IsPoweredAndAnchored(ent) || !IsTargetInRange(ent, target))
        {
            Popup(ent, "bulk-auto-mining-select-out-of-range");
            return false;
        }

        if (!TryComp<BulkAutoMiningJobComponent>(ent, out var job))
            return false;

        ResolveEmitters(ent, job);
        if (ent.Comp.SelectedGrids.Count >= job.Emitters.Count)
        {
            Popup(ent, "bulk-auto-mining-start-no-emitter");
            return false;
        }

        ent.Comp.SelectedGrids.Add(target);
        ent.Comp.TotalTiles = 0;
        ent.Comp.ProcessedTiles = 0;
        if (!_deposits.IsInitialized(target))
            Popup(ent, "bulk-auto-mining-warning-forbidden-target");

        UpdateUi(ent);
        return true;
    }

    private bool IsTargetInRange(Entity<BulkAutoMiningConsoleComponent> ent, EntityUid target)
    {
        return IsGridInRange(ent, target, ent.Comp.MaxRange);
    }

    /// <summary>Whether the nearest point of a grid's bounds is within range of the console.</summary>
    private bool IsGridInRange(EntityUid console, EntityUid target, float range)
    {
        if (TerminatingOrDeleted(target) || !_gridQuery.TryComp(target, out var grid) ||
            !_xformQuery.TryComp(target, out var targetXform))
            return false;

        var consoleXform = Transform(console);
        if (consoleXform.MapUid == null || consoleXform.MapUid != targetXform.MapUid || consoleXform.GridUid == target)
            return false;

        return GetDistanceToGrid(_transform.GetWorldPosition(consoleXform), (target, grid, targetXform)) <= range;
    }

    private float GetDistanceToGrid(Vector2 worldPosition, Entity<MapGridComponent, TransformComponent> grid)
    {
        var position = Vector2.Transform(worldPosition, _transform.GetInvWorldMatrix(grid.Comp2));
        var nearest = Vector2.Clamp(position, grid.Comp1.LocalAABB.BottomLeft, grid.Comp1.LocalAABB.TopRight);
        return Vector2.Distance(position, nearest);
    }

    private bool TryPrepareGrid(Entity<BulkAutoMiningConsoleComponent> console, EntityUid target,
        [NotNullWhen(true)] out BulkAutoMiningGridJob? job)
    {
        job = null;
        if (TerminatingOrDeleted(target) || !_gridQuery.HasComp(target))
            return false;

        // Materialize this selected grid once, not all chunks around every target on every mining tick.
        // Remove the loader before raising the event so another console cannot populate it twice.
        if (RemComp<LocalityLoaderComponent>(target))
            RaiseLocalEvent(target, new LocalStructureLoadedEvent());

        if (TerminatingOrDeleted(target) || !_gridQuery.TryComp(target, out var grid))
            return false;

        // The surface is shared by every console mining this grid and maintained from tile events.
        if (_surface.Retain(console, (target, grid)) is not { } surface)
            return false;

        if (surface.Remaining == 0)
        {
            _surface.Release(console, target);
            return false;
        }

        job = new BulkAutoMiningGridJob
        {
            GridUid = target,
            Remaining = surface.Remaining,
        };
        return true;
    }

    private void ReleaseGridJob(Entity<BulkAutoMiningConsoleComponent> console, BulkAutoMiningGridJob gridJob)
    {
        _connectivity.ReleaseGrid(console, gridJob.GridUid);
        _surface.Release(console, gridJob.GridUid);
    }

    private void InvalidateGridJob(
        Entity<BulkAutoMiningConsoleComponent> console,
        BulkAutoMiningJobComponent job,
        BulkAutoMiningGridJob gridJob)
    {
        if (gridJob.Invalidated)
            return;

        gridJob.Invalidated = true;
        ReleaseGridJob(console, gridJob);
        console.Comp.TotalTiles -= gridJob.Remaining;
        gridJob.Remaining = 0;
        console.Comp.SelectedGrids.Remove(gridJob.GridUid);
        job.NextUiTime = _timing.CurTime;

        foreach (var uid in job.Emitters)
        {
            if (TerminatingOrDeleted(uid) || !_emitterQuery.TryComp(uid, out var emitter) || emitter.Controller != console.Owner ||
                emitter.BeamGrid != gridJob.GridUid)
                continue;

            ClearBeam((uid, emitter));
            emitter.NextTargetSearchTime = _timing.CurTime;
            // Preserve Ready during the mining pass so its aiming pass can immediately pick another target.
            if (!job.Statuses.TryGetValue(uid, out var status) || status != BulkAutoMiningLaserStatus.Ready)
                job.Statuses[uid] = BulkAutoMiningLaserStatus.Searching;
        }
    }

    /// <summary>Counts tiles removed from shared surfaces since the last update, by any miner or event.</summary>
    private void SyncProgress(Entity<BulkAutoMiningConsoleComponent> console, BulkAutoMiningJobComponent job)
    {
        foreach (var gridJob in job.GridJobs)
        {
            if (gridJob.Invalidated || !_surfaceQuery.TryComp(gridJob.GridUid, out var surface))
                continue;

            var delta = gridJob.Remaining - surface.Remaining;
            if (delta == 0)
                continue;

            // Artificial targets may gain tiles; they enlarge the job instead of undoing progress.
            if (delta > 0)
                console.Comp.ProcessedTiles += delta;
            else
                console.Comp.TotalTiles -= delta;

            gridJob.Remaining = surface.Remaining;
        }
    }

    private bool TryFinishMining(Entity<BulkAutoMiningConsoleComponent> console, BulkAutoMiningJobComponent job)
    {
        SyncProgress(console, job);
        var lostTargets = false;
        foreach (var gridJob in job.GridJobs)
        {
            if (gridJob.Remaining > 0)
                return false;

            lostTargets |= gridJob.Invalidated;
        }

        StopMining(console, lostTargets ? "bulk-auto-mining-stopped-invalid-grid" : "bulk-auto-mining-complete");
        return true;
    }

    private bool IsTargetTile(Entity<MapGridComponent> grid, Vector2i tile)
    {
        // Unknown grids remain targetable for the overload penalty, but never produce metal.
        // On natural grids, additions and replacements are excluded even from cached searches.
        return !_map.GetTileRef(grid, grid.Comp, tile).Tile.IsEmpty &&
               (!_deposits.IsInitialized(grid) || _deposits.CanMine(grid, tile));
    }
}

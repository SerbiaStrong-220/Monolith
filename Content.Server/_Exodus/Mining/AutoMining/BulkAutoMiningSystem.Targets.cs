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
        if (TerminatingOrDeleted(target) || !TryComp<MapGridComponent>(target, out var grid) ||
            !_xformQuery.TryComp(target, out var targetXform))
            return false;

        var consoleXform = Transform(ent);
        if (consoleXform.MapUid == null || consoleXform.MapUid != targetXform.MapUid || consoleXform.GridUid == target)
            return false;

        var position = Vector2.Transform(_transform.GetWorldPosition(consoleXform), _transform.GetInvWorldMatrix(target));
        var nearest = Vector2.Clamp(position, grid.LocalAABB.BottomLeft, grid.LocalAABB.TopRight);
        return Vector2.DistanceSquared(position, nearest) <= ent.Comp.MaxRange * ent.Comp.MaxRange;
    }

    private bool TryPrepareGrid(EntityUid target, [NotNullWhen(true)] out BulkAutoMiningGridJob? job)
    {
        job = null;
        if (TerminatingOrDeleted(target) || !TryComp<MapGridComponent>(target, out var grid))
            return false;

        // Materialize this selected grid once, not all chunks around every target on every mining tick.
        // Remove the loader before raising the event so another console cannot populate it twice.
        if (RemComp<LocalityLoaderComponent>(target))
            RaiseLocalEvent(target, new LocalStructureLoadedEvent());

        if (TerminatingOrDeleted(target))
            return false;

        var tiles = new Queue<Vector2i>();
        var remaining = new HashSet<Vector2i>();
        var rangeTiles = new List<Vector2i>();
        var natural = _deposits.IsInitialized(target);
        var enumerator = _map.GetAllTilesEnumerator(target, grid);
        while (enumerator.MoveNext(out var tile))
        {
            if (tile is { } tileRef && !tileRef.Tile.IsEmpty && (!natural || _deposits.CanMine(target, tileRef.GridIndices)))
            {
                tiles.Enqueue(tileRef.GridIndices);
                remaining.Add(tileRef.GridIndices);
                rangeTiles.Add(tileRef.GridIndices);
            }
        }

        if (tiles.Count == 0)
            return false;

        job = new BulkAutoMiningGridJob
        {
            GridUid = target,
            Tiles = tiles,
            RemainingTiles = remaining,
            RangeTiles = rangeTiles,
        };
        return true;
    }

    private void InvalidateGridJob(
        Entity<BulkAutoMiningConsoleComponent> console,
        BulkAutoMiningJobComponent job,
        BulkAutoMiningGridJob gridJob)
    {
        if (gridJob.Invalidated)
            return;

        gridJob.Invalidated = true;
        _connectivity.ReleaseGrid(console, gridJob.GridUid);
        console.Comp.TotalTiles -= gridJob.RemainingTiles.Count;
        console.Comp.SelectedGrids.Remove(gridJob.GridUid);
        gridJob.RemainingTiles.Clear();
        gridJob.Tiles.Clear();
        gridJob.RangeTiles.Clear();
        gridJob.RangeSearches = [];
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

    private bool TryFinishMining(Entity<BulkAutoMiningConsoleComponent> console, BulkAutoMiningJobComponent job)
    {
        var lostTargets = false;
        foreach (var gridJob in job.GridJobs)
        {
            if (gridJob.RemainingTiles.Count > 0)
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

using System.Numerics;
using Content.Shared._Exodus.Mining.AutoMining;

namespace Content.Server._Exodus.Mining.AutoMining;

public sealed partial class BulkAutoMiningSystem
{
    private static readonly TimeSpan RangeCheckInterval = TimeSpan.FromSeconds(1);

    private bool CheckTargetRange(Entity<BulkAutoMiningConsoleComponent> console, BulkAutoMiningJobComponent job)
    {
        // Validate each target before a reachable tile can short-circuit the range search.
        foreach (var gridJob in job.GridJobs)
        {
            // An exhausted target may have been deleted together with its last tile; it is complete, not lost.
            if (gridJob.Invalidated || gridJob.Remaining == 0)
                continue;

            var uid = gridJob.GridUid;
            if (TerminatingOrDeleted(uid) || EntityManager.IsQueuedForDeletion(uid) ||
                !_gridQuery.HasComp(uid) || !_xformQuery.HasComp(uid) || !_surfaceQuery.HasComp(uid))
                InvalidateGridJob(console, job, gridJob);
        }

        SyncProgress(console, job);
        foreach (var gridJob in job.GridJobs)
        {
            if (gridJob.Invalidated || gridJob.Remaining == 0 ||
                !_surfaceQuery.TryComp(gridJob.GridUid, out var surface))
                continue;

            var grid = _gridQuery.GetComponent(gridJob.GridUid);
            var gridXform = _xformQuery.GetComponent(gridJob.GridUid);
            var invMatrix = _transform.GetInvWorldMatrix(gridXform);
            foreach (var emitterUid in job.Emitters)
            {
                if (TerminatingOrDeleted(emitterUid) || !_emitterQuery.TryComp(emitterUid, out var emitter) ||
                    emitter.Controller != console.Owner || !_xformQuery.TryComp(emitterUid, out var emitterXform) ||
                    emitterXform.MapUid == null || emitterXform.MapUid != gridXform.MapUid ||
                    emitterXform.GridUid == gridJob.GridUid)
                    continue;

                // The nearest remaining tile to any outside point is always exposed, so this check is exact.
                var local = Vector2.Transform(_transform.GetWorldPosition(emitterXform), invMatrix);
                if (BulkMiningSurfaceSystem.HasExposedTileInRange(surface, grid.TileSize, local, console.Comp.MaxRange))
                    return true;
            }
        }

        if (!TryFinishMining(console, job))
            StopMining(console, "bulk-auto-mining-stopped-out-of-range");

        return false;
    }
}

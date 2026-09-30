using Content.Server._Exodus.Mining.AutoMining;
using Content.Shared._Exodus.Mining.AutoMining;

namespace Content.Server.Worldgen.Systems;

public sealed partial class WorldControllerSystem
{
    /// <summary>
    /// Retain only the existing worldgen chunks owning active targets. A console must not load
    /// hundreds of surrounding chunks, and writing LoadedChunk.Loaders alone does not prevent GC.
    /// </summary>
    private void AddBulkMiningLoaders()
    {
        var jobs = EntityQueryEnumerator<BulkAutoMiningJobComponent, BulkAutoMiningConsoleComponent>();
        while (jobs.MoveNext(out _, out var job, out var console))
        {
            if (!console.Active)
                continue;

            foreach (var target in job.GridJobs)
            {
                if (target.Invalidated || target.Remaining == 0 || TerminatingOrDeleted(target.GridUid) ||
                    !_transformQuery.TryComp(target.GridUid, out var xform))
                    continue;

                TryAddParentDebrisChunkLoader(target.GridUid, xform);
            }
        }
    }
}

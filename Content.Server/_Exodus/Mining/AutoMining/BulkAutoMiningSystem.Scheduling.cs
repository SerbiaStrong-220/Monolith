using System.Diagnostics;
using Content.Shared._Exodus.Mining.AutoMining;

namespace Content.Server._Exodus.Mining.AutoMining;

public sealed partial class BulkAutoMiningSystem
{
    private static readonly TimeSpan MiningUpdateBudget = TimeSpan.FromMilliseconds(4);
    private readonly List<Entity<BulkAutoMiningConsoleComponent, BulkAutoMiningJobComponent>> _jobBuffer = new();
    private int _nextJobIndex;

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        var now = _timing.CurTime;
        UpdateLinks(now);
        _jobBuffer.Clear();
        var query = EntityQueryEnumerator<BulkAutoMiningJobComponent, BulkAutoMiningConsoleComponent>();
        while (query.MoveNext(out var uid, out var job, out var console))
        {
            // Range checks have their own bounded tile budget. Departure must not wait behind excavation work.
            if (console.Active && now >= job.NextRangeCheckTime)
            {
                job.NextRangeCheckTime = now + RangeCheckInterval;
                CheckTargetRange((uid, console), job);
            }

            _jobBuffer.Add((uid, console, job));
        }

        if (_jobBuffer.Count == 0)
        {
            _nextJobIndex = 0;
            return;
        }

        // Finish the current console, then resume from the next one on the following tick if the budget is used.
        // Include idle consoles: their open radars must not create a separate, unbounded update spike.
        // The cursor covers all jobs, including those on cooldown, so late jobs cannot starve.
        var started = Stopwatch.GetTimestamp();
        for (var visited = 0; visited < _jobBuffer.Count; visited++)
        {
            _nextJobIndex %= _jobBuffer.Count;
            var (uid, console, job) = _jobBuffer[_nextJobIndex];
            _nextJobIndex++;
            if (TerminatingOrDeleted(uid))
                continue;

            if (console.Active && now >= job.NextProcessTime)
                ProcessMiningTick((uid, console), job);

            if (console.Active && now >= job.NextBeamCheckTime)
                CheckActiveBeams((uid, console), job);

            if (now >= job.NextUiTime && _ui.IsUiOpen(uid, BulkAutoMiningUiKey.Key))
            {
                job.NextUiTime = now + UiInterval;
                UpdateUi((uid, console));
            }

            if (Stopwatch.GetElapsedTime(started) >= MiningUpdateBudget)
                break;
        }
    }
}

using Content.Shared._Exodus.Mining.AutoMining;

namespace Content.Server._Exodus.Mining.AutoMining;

public sealed partial class BulkAutoMiningSystem
{
    /// <summary>
    /// Derives warmup from the last beam transition without ticking idle emitters or dirtying network state.
    /// </summary>
    public double GetWarmup(Entity<BulkAutoMiningEmitterComponent> emitter)
    {
        var firing = emitter.Comp.BeamGrid != null;
        var duration = firing ? emitter.Comp.WarmupTime : emitter.Comp.CooldownTime;
        if (duration <= TimeSpan.Zero)
            return firing ? 1 : 0;

        var elapsed = _timing.CurTime - _metadata.GetPauseTime(emitter) - emitter.Comp.WarmupLastUpdate;
        var change = Math.Max(0, elapsed.TotalSeconds) / duration.TotalSeconds;
        return Math.Clamp(emitter.Comp.WarmupProgress + (firing ? change : -change), 0, 1);
    }

    private void SnapshotWarmup(Entity<BulkAutoMiningEmitterComponent> emitter)
    {
        emitter.Comp.WarmupProgress = GetWarmup(emitter);
        emitter.Comp.WarmupLastUpdate = _timing.CurTime - _metadata.GetPauseTime(emitter);
    }

    private static double GetWarmupYieldBonus(Entity<BulkAutoMiningEmitterComponent> emitter, double warmup)
    {
        var maxBonus = emitter.Comp.MaxWarmupYieldBonus;
        return double.IsFinite(maxBonus) ? warmup * Math.Max(0, maxBonus) : 0;
    }

    private int GetSlurryYield(Entity<BulkAutoMiningEmitterComponent> emitter, int baseAmount)
    {
        var bonus = GetWarmupYieldBonus(emitter, GetWarmup(emitter));
        // Storage uses integer material units. Round down so the configured bonus is never exceeded.
        return (int)Math.Clamp(Math.Floor(baseAmount * (1 + bonus)), 0, int.MaxValue);
    }
}

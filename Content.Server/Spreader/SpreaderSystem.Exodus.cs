// Exodus: skip neighbor lookup until this spreader's configured growth interval elapses.
namespace Content.Server.Spreader;

public sealed partial class SpreaderSystem
{
    private void ScheduleSpread(Entity<EdgeSpreaderComponent> ent)
    {
        var min = ent.Comp.MinSpreadDelay < TimeSpan.Zero ? TimeSpan.Zero : ent.Comp.MinSpreadDelay;
        var max = ent.Comp.MaxSpreadDelay < min ? min : ent.Comp.MaxSpreadDelay;
        ent.Comp.NextSpread = _timing.CurTime + min + (max - min) * _random.NextFloat();
    }

    private bool IsSpreadReady(Entity<EdgeSpreaderComponent> ent)
    {
        if (_timing.CurTime < ent.Comp.NextSpread)
            return false;
        if (ent.Comp.MinSpreadDelay > TimeSpan.Zero || ent.Comp.MaxSpreadDelay > TimeSpan.Zero)
            ScheduleSpread(ent);
        return true;
    }
}

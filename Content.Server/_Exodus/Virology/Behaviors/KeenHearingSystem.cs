// Adapted from SS220 keen hearing. EULA/CLA: https://raw.githubusercontent.com/SerbiaStrong-220/space-station-14/master/CLA.txt

using Content.Shared._Exodus.Virology.Behaviors;
using Robust.Shared.Timing;

namespace Content.Server._Exodus.Virology.Behaviors;

public sealed partial class KeenHearingSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<KeenHearingComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<KeenHearingComponent, UseKeenHearingEvent>(OnUse);
    }

    private void OnStartup(Entity<KeenHearingComponent> ent, ref ComponentStartup args)
    {
        RefreshKeenHearing(ent);
    }

    private void OnUse(Entity<KeenHearingComponent> ent, ref UseKeenHearingEvent args)
    {
        if (args.Handled)
            return;

        ent.Comp.ManualOn = !ent.Comp.ManualOn;
        ent.Comp.ToggleTime = args.Duration is { } duration ? _timing.CurTime + duration : null;
        RefreshKeenHearing(ent);
        args.Handled = true;
    }

    public void RefreshKeenHearing(Entity<KeenHearingComponent> ent)
    {
        var ev = new GetKeenHearingModifiersEvent();
        RaiseLocalEvent(ent, ref ev);
        ent.Comp.Enabled = ent.Comp.ManualOn || ev.ForceOn;
        Dirty(ent);
    }

    public override void Update(float frameTime)
    {
        var query = EntityQueryEnumerator<KeenHearingComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (comp.ToggleTime is not { } end || _timing.CurTime < end)
                continue;

            comp.ManualOn = false;
            comp.ToggleTime = null;
            RefreshKeenHearing((uid, comp));
        }
    }
}

// © SS220, An EULA/CLA with a hosting restriction, full text: https://raw.githubusercontent.com/SerbiaStrong-220/space-station-14/master/CLA.txt

using Content.Shared.Body.Events;
using Content.Server.Body.Systems;
using Content.Shared._Exodus.Virology.Behaviors;
using Content.Shared._Exodus.Virology;

namespace Content.Server._Exodus.Virology.Behaviors;

public sealed partial class VirusMetabolismSlowSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<VirusMetabolismSlowComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<VirusMetabolismSlowComponent, GetMetabolicMultiplierEvent>(OnGetMultiplier);
    }

    private void OnShutdown(Entity<VirusMetabolismSlowComponent> ent, ref ComponentShutdown args)
    {
        if (Terminating(ent.Owner))
            return;

        ent.Comp.Reverting = true;
    }

    private void OnGetMultiplier(Entity<VirusMetabolismSlowComponent> ent, ref GetMetabolicMultiplierEvent args)
    {
        if (ent.Comp.Reverting || ent.Comp.Reduction <= 0f || ent.Comp.Reduction >= 1f)
            return;

        // higher multiplier = longer update interval = slower metabolism
        args.Multiplier *= 1f / (1f - ent.Comp.Reduction);
    }
}

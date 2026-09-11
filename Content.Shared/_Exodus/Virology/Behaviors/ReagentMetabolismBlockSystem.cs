// © SS220, An EULA/CLA with a hosting restriction, full text: https://raw.githubusercontent.com/SerbiaStrong-220/space-station-14/master/CLA.txt

using Content.Shared.Body.Events;
using Content.Shared.Chemistry.Reagent;

namespace Content.Shared._Exodus.Virology.Behaviors;

public sealed class ReagentMetabolismBlockSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ReagentMetabolismBlockComponent, ReagentMetabolismAttemptEvent>(OnMetabolismExclusion);
    }

    private void OnMetabolismExclusion(Entity<ReagentMetabolismBlockComponent> ent, ref ReagentMetabolismAttemptEvent args)
    {
        if (ent.Comp.Reagents.Contains(args.Reagent))
            args.Cancelled = true;
    }
}

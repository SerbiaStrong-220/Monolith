using Content.Shared.Mind;
using Content.Shared.Mind.Components;

namespace Content.Server._Exodus.Objectives;

public sealed partial class ObjectivesOnMindSystem : EntitySystem
{
    [Dependency] private SharedMindSystem _mind = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<ObjectivesOnMindComponent, MindAddedMessage>(OnMindAdded);
    }

    private void OnMindAdded(Entity<ObjectivesOnMindComponent> ent, ref MindAddedMessage args)
    {
        if (!ent.Comp.GrantedMinds.Add(args.Mind.Owner))
            return;

        foreach (var objective in ent.Comp.Objectives)
            _mind.TryAddObjective(args.Mind.Owner, args.Mind.Comp, objective);
    }
}

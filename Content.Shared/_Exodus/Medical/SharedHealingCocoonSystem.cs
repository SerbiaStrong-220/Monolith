using Content.Shared.Whitelist;
using Robust.Shared.Containers;

namespace Content.Shared._Exodus.Medical;

public sealed partial class SharedHealingCocoonSystem : EntitySystem
{
    [Dependency] private EntityWhitelistSystem _whitelist = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<HealingCocoonComponent, ContainerIsInsertingAttemptEvent>(OnInsertAttempt);
    }

    private void OnInsertAttempt(Entity<HealingCocoonComponent> ent, ref ContainerIsInsertingAttemptEvent args)
    {
        if (args.Container.ID == ent.Comp.ContainerId &&
            (ent.Comp.Ruptured || TerminatingOrDeleted(ent) || EntityManager.IsQueuedForDeletion(ent) ||
             !_whitelist.CheckBoth(args.EntityUid, ent.Comp.Blacklist, ent.Comp.Whitelist)))
            args.Cancel();
    }
}

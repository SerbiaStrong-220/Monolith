using Content.Shared.Doors;
using Content.Shared.Whitelist;

namespace Content.Shared._Exodus.Doors;

public sealed partial class DoorUserWhitelistSystem : EntitySystem
{
    [Dependency] private EntityWhitelistSystem _whitelist = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<DoorUserWhitelistComponent, BeforeDoorOpenedEvent>(OnOpening);
    }

    private void OnOpening(Entity<DoorUserWhitelistComponent> ent, ref BeforeDoorOpenedEvent args)
    {
        if (args.User is { } user ? !_whitelist.IsValid(ent.Comp.Whitelist, user) : !ent.Comp.AllowWithoutUser)
            args.Cancel();
    }
}

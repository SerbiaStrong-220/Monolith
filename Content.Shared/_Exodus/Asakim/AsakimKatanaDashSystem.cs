using Content.Shared.Ninja.Systems;

namespace Content.Shared._Exodus.Asakim;

public sealed class AsakimKatanaDashSystem : EntitySystem
{
    [Dependency] private readonly AsakimIdentitySystem _asakim = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<AsakimKatanaDashComponent, CheckDashEvent>(OnCheckDash);
    }

    private void OnCheckDash(Entity<AsakimKatanaDashComponent> ent, ref CheckDashEvent args)
    {
        if (!_asakim.IsAsakim(args.User))
            args.Cancelled = true;
    }
}

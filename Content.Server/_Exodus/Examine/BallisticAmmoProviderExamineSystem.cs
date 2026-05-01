// (c) Space Exodus Team - EXDS-RL with CLA
// Authors: Provstat
using Content.Server.Weapons.Ranged.Systems;
using Content.Shared.Damage.Events;
using Content.Shared.Weapons.Ranged.Components;

namespace Content.Server._Exodus.Examine;

public sealed class BallisticAmmoProviderExamineSystem : EntitySystem
{
    [Dependency] private readonly GunSystem _gun = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<BallisticAmmoProviderComponent, DamageExamineEvent>(OnDamageExamine);

        base.Initialize();
    }

    private void OnDamageExamine(Entity<BallisticAmmoProviderComponent> ent, ref DamageExamineEvent args)
    {
        if (ent.Comp.Proto is not null)
            _gun.AddCartridgeInfoToExamineMessage(args.Message, ent.Comp.Proto);
    }
}

// (c) Space Exodus Team - EXDS-RL with CLA
// Authors: Provstat
using Content.Server.Weapons.Ranged.Systems;
using Content.Shared.Damage.Events;
using Content.Shared.Weapons.Ranged.Components;

namespace Content.Server._Exodus.Examine;

public sealed class BasicEntityExamineSystem : EntitySystem
{
    [Dependency] private readonly GunSystem _gun = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<BasicEntityAmmoProviderComponent, DamageExamineEvent>(OnDamageExamine);

        base.Initialize();
    }

    private void OnDamageExamine(Entity<BasicEntityAmmoProviderComponent> ent, ref DamageExamineEvent args)
    {
        _gun.AddProjectileInfoToExamineMessage(args.Message, ent.Comp.Proto);
    }
}

// (c) Space Exodus Team - EXDS-RL with CLA
// Authors: Provstat
using Content.Server.Weapons.Ranged.Systems;
using Content.Shared._Exodus.Examine.Damage;
using Content.Shared.Damage;
using Content.Shared.Damage.Events;
using Content.Shared.Damage.Systems;
using Content.Shared.Weapons.Ranged.Components;

namespace Content.Server._Exodus.Examine;

public sealed class BasicEntityExamineSystem : EntitySystem
{
    [Dependency] private readonly DamageableSystem _damageable = default!;
    [Dependency] private readonly DamageExamineSystem _damageExamine = default!;
    [Dependency] private readonly GunSystem _gun = default!;
    [Dependency] private readonly PiercingExamineSystem _piercingExamine = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<BasicEntityAmmoProviderComponent, DamageExamineEvent>(OnDamageExamine);

        base.Initialize();
    }

    private void OnDamageExamine(Entity<BasicEntityAmmoProviderComponent> ent, ref DamageExamineEvent args)
    {
        var damageInfo = _gun.GetProjectileDamageInfo(ent.Comp.Proto);

        if (damageInfo is null)
            return;

        if (damageInfo.Value.Damage is not null)
            _damageExamine.AddDamageExamine(
                args.Message,
                _damageable.ApplyUniversalAllModifiers(damageInfo.Value.Damage),
                type: Loc.GetString("damage-projectile"));

        if (damageInfo.Value.ArmorPenetration is not null)
            _piercingExamine.AddPenetrationToExamineMessage(args.Message, damageInfo.Value.ArmorPenetration.Value);
    }
}

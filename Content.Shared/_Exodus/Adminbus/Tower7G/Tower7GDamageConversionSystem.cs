using Content.Shared.Weapons.Melee.Events;

namespace Content.Shared._Exodus.Adminbus.Tower7G;

public sealed class Tower7GDamageConversionSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<Tower7GDamageConversionComponent, GetMeleeDamageEvent>(OnGetMeleeDamage);
    }

    private void OnGetMeleeDamage(Entity<Tower7GDamageConversionComponent> ent, ref GetMeleeDamageEvent args)
    {
        if (!ent.Comp.Active || args.Weapon != ent.Owner || ent.Comp.SourceType == ent.Comp.TargetType)
            return;

        // GetMeleeDamageEvent owns a temporary specifier; leave the weapon's base damage untouched.
        var damage = args.Damage.DamageDict;
        if (!damage.TryGetValue(ent.Comp.SourceType, out var amount) || amount <= 0)
            return;

        damage.Remove(ent.Comp.SourceType);
        damage.TryGetValue(ent.Comp.TargetType, out var existing);
        damage[ent.Comp.TargetType] = existing + amount;
    }
}

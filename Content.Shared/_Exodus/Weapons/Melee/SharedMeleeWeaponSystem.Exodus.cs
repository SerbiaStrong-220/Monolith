using Content.Shared._Exodus.Weapons.Melee;
using Content.Shared.Damage;

namespace Content.Shared.Weapons.Melee;

public abstract partial class SharedMeleeWeaponSystem
{
    [Dependency] private MeleeChargeSystem _meleeCharges = default!;

    private DamageSpecifier? ApplyChargedMeleeDamage(EntityUid weapon, EntityUid user, EntityUid target,
        DamageSpecifier damage, List<DamageModifierSet> modifiers, float armorPenetration,
        float partMultiplier, out DamageSpecifier modifiedDamage)
    {
        var charged = _meleeCharges.TryReserveHit(weapon, user, target, out var use);
        modifiedDamage = DamageSpecifier.ApplyModifierSets(charged ? damage + use.Damage : damage, modifiers);
        var result = Damageable.TryChangeDamage(target, modifiedDamage, origin: user,
            armorPenetration: armorPenetration, partMultiplier: partMultiplier);
        if (charged)
            _meleeCharges.CompleteHit(use, user, target, result);
        return result;
    }
}

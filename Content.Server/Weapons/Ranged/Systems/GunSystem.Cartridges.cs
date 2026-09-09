using Content.Shared.Damage;
using Content.Shared.Damage.Events;
using Content.Shared.Examine;
using Content.Shared.Projectiles;
using Content.Shared.Weapons.Hitscan.Components;
using Content.Shared.Weapons.Ranged.Components;
using Robust.Shared.Prototypes;

namespace Content.Server.Weapons.Ranged.Systems;

public sealed partial class GunSystem
{
    protected override void InitializeCartridge()
    {
        base.InitializeCartridge();
        SubscribeLocalEvent<CartridgeAmmoComponent, ExaminedEvent>(OnCartridgeExamine);
        SubscribeLocalEvent<CartridgeAmmoComponent, DamageExamineEvent>(OnCartridgeDamageExamine);
    }

    private void OnCartridgeDamageExamine(EntityUid uid, CartridgeAmmoComponent component, ref DamageExamineEvent args)
    {
        var damageSpec = GetProjectileDamage(component.Prototype, out var isHitscan); // mono

        if (damageSpec == null)
            return;

        // Exodus-begin: retain armor penetration while supporting upstream hitscan cartridges.
        _damageExamine.AddDamageExamine(
            args.Message,
            Damageable.ApplyUniversalAllModifiers(damageSpec.Value.Damage),
            armorPenetration: damageSpec.Value.ArmorPenetration,
            type: Loc.GetString(isHitscan ? "damage-hitscan" : "damage-projectile"));
        // Exodus-end
    }

    // Exodus: retain armor-penetration examination data for both projectile and upstream hitscan cartridges.
    private (DamageSpecifier Damage, float ArmorPenetration)? GetProjectileDamage(string proto, out bool isHitscan)
    {
        isHitscan = false; // mono

        if (!ProtoManager.TryIndex<EntityPrototype>(proto, out var entityProto))
            return null;

        if (entityProto.Components
            .TryGetValue(Factory.GetComponentName<ProjectileComponent>(), out var projectile))
        {
            var p = (ProjectileComponent) projectile.Component;

            if (!p.Damage.Empty)
            {
                return (p.Damage * Damageable.UniversalProjectileDamageModifier, p.ArmorPenetration);
            }
        }
        // mono
        else if (entityProto.Components.TryGetValue(Factory.GetComponentName<HitscanBasicDamageComponent>(), out var hitscan))
        {
            var h = (HitscanBasicDamageComponent) hitscan.Component;

            if (h.Damage.Empty)
                return null;

            isHitscan = true;
            // Exodus: expose the same effective hitscan damage and armor penetration used when firing.
            return (h.Damage * Damageable.UniversalHitscanDamageModifier, h.ArmorPenetration);
        }

        return null;
    }

    private void OnCartridgeExamine(EntityUid uid, CartridgeAmmoComponent component, ExaminedEvent args)
    {
        if (component.Spent)
        {
            args.PushMarkup(Loc.GetString("gun-cartridge-spent"));
        }
        else
        {
            args.PushMarkup(Loc.GetString("gun-cartridge-unspent"));
        }
    }
}

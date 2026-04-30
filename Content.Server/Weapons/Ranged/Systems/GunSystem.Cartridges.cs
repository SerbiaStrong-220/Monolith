using Content.Shared._Exodus.Examine.Damage;    //Exodus ArmorPiercingExamine
using Content.Shared.Damage;
using Content.Shared.Damage.Events;
using Content.Shared.Examine;
using Content.Shared.FixedPoint;
using Content.Shared.Projectiles;
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
        var damageSpec = GetProjectileDamageInfo(component.Prototype);

        if (damageSpec == null)
            return;

        //Exodus ArmorPiercingExamine Start
        if (damageSpec.Value.Damage is not null)
            _damageExamine.AddDamageExamine(
                args.Message,
                Damageable.ApplyUniversalAllModifiers(damageSpec.Value.Damage),
                type: Loc.GetString("damage-projectile"));

        if (damageSpec.Value.ArmorPenetration is not null)
            _piercingExamine.AddPenetrationToExamineMessage(args.Message, damageSpec.Value.ArmorPenetration.Value);
        //Exodus ArmorPiercingExamine End
    }

    public ArmorPiercerDamageInfo? GetProjectileDamageInfo(string proto)     //Exodus ArmorPiercingExamine
    {
        if (!ProtoManager.TryIndex<EntityPrototype>(proto, out var entityProto))
            return null;

        if (entityProto.Components
            .TryGetValue(Factory.GetComponentName<ProjectileComponent>(), out var projectile))
        {
            var p = (ProjectileComponent)projectile.Component;

            if (!p.Damage.Empty)
            {
                //Exodus ArmorPiercingExamine Start
                return new()
                {
                    Damage = p.Damage * Damageable.UniversalProjectileDamageModifier,
                    ArmorPenetration = p.ArmorPenetration
                };
                //Exodus ArmorPiercingExamine End
            }
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

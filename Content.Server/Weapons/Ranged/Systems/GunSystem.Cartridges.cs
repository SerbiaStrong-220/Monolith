using Content.Shared._Exodus.Examine.Damage;    //Exodus ArmorPiercingExamine
using Content.Shared.Damage;
using Content.Shared.Damage.Events;
using Content.Shared.Examine;
using Content.Shared.FixedPoint;
using Content.Shared.Projectiles;
using Content.Shared.Weapons.Ranged.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;
using System.ComponentModel;

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
        AddCartridgeInfoToExamineMessage(args.Message, component.Prototype);    //Exodus AdvancedWeaponExamine
    }

    //Exodus AdvancedWeaponExamine Start
    public void AddCartridgeInfoToExamineMessage(FormattedMessage examineMessage, string cartridgeProtoId)
    {
        var cartridgeInfo = GetProjectileDamageInfo(cartridgeProtoId);

        if (cartridgeInfo == null)
            return;

        if (cartridgeInfo.Value.Damage is not null)
            _damageExamine.AddDamageExamine(
                examineMessage,
                Damageable.ApplyUniversalAllModifiers(cartridgeInfo.Value.Damage),
                type: Loc.GetString("damage-projectile"));

        if (cartridgeInfo.Value.ArmorPenetration is not null)
            _piercingExamine.AddPenetrationToExamineMessage(examineMessage, cartridgeInfo.Value.ArmorPenetration.Value);
    }
    //Exodus AdvancedWeaponExamine End

    public CartridgeInfo? GetProjectileDamageInfo(string proto)     //Exodus ArmorPiercingExamine
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

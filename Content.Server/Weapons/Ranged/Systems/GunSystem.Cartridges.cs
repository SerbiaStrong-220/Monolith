using Content.Server._Exodus.Examine;           //Exodus AdvancedWeaponExamine
using Content.Shared._Exodus.Examine.Damage;    //Exodus ArmorPiercingExamine
using Content.Shared.Damage;
using Content.Shared.Damage.Events;
using Content.Shared.Examine;
using Content.Shared.Explosion.Components;
using Content.Shared.FixedPoint;
using Content.Shared.Projectiles;
using Content.Shared.Weapons.Ranged.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;
using System.ComponentModel;
using static Robust.Shared.Physics.DynamicTree;

namespace Content.Server.Weapons.Ranged.Systems;

public sealed partial class GunSystem
{
    [Dependency] private readonly ExplosiveEntityExamineSystem _explosiveEntityExamine = default!;  //Exodus AdvancedWeaponExamine
    [Dependency] private readonly ExplosionExamineSystem _explosionExamine = default!;              //Exodus AdvancedWeaponExamine
    protected override void InitializeCartridge()
    {
        base.InitializeCartridge();
        SubscribeLocalEvent<CartridgeAmmoComponent, ExaminedEvent>(OnCartridgeExamine);
        SubscribeLocalEvent<CartridgeAmmoComponent, DamageExamineEvent>(OnCartridgeDamageExamine);
    }

    private void OnCartridgeDamageExamine(EntityUid uid, CartridgeAmmoComponent component, ref DamageExamineEvent args)
    {
        AddProjectileInfoToExamineMessage(args.Message, component.Prototype);    //Exodus AdvancedWeaponExamine
    }

    //Exodus AdvancedWeaponExamine Start
    public void AddProjectileInfoToExamineMessage(FormattedMessage examineMessage, string cartridgeProtoId)
    {
        var cartridgeInfo = GetIndefineProjectileDamageInfo(cartridgeProtoId);

        if (cartridgeInfo == null)
            return;

        if (cartridgeInfo.Value.Damage is not null)
            _damageExamine.AddDamageExamine(
                examineMessage,
                Damageable.ApplyUniversalAllModifiers(cartridgeInfo.Value.Damage),
                type: Loc.GetString("damage-projectile"));

        if (cartridgeInfo.Value.ArmorPenetration is not null)
            _piercingExamine.AddPenetrationToExamineMessage(examineMessage, cartridgeInfo.Value.ArmorPenetration.Value);

        if (cartridgeInfo.Value.Explosion is not null)
            _explosionExamine.AddExplosiveInfoToExamineMessage(examineMessage, cartridgeInfo.Value.Explosion.Value);
    }

    private ExamineCartridgeInfo? GetIndefineProjectileDamageInfo(string proto)
    {
        if (!ProtoManager.TryIndex<EntityPrototype>(proto, out var projectileProto))
            return null;

        if (projectileProto.Components
            .TryGetValue(Factory.GetComponentName<CartridgeAmmoComponent>(), out var projectile))
            return GetCartridjeDamageInfo((CartridgeAmmoComponent)projectile.Component);
        else
            return GetProjectileDamageInfo(projectileProto);
    }

    private ExamineCartridgeInfo? GetProjectileDamageInfo(EntityPrototype projectileEntity)
    {
        if (projectileEntity.Components
        .TryGetValue(Factory.GetComponentName<ProjectileComponent>(), out var projectile))
        {

            var p = (ProjectileComponent)projectile.Component;

            if (!p.Damage.Empty)
            {
                return new()
                {
                    Damage = p.Damage * Damageable.UniversalProjectileDamageModifier,
                    ArmorPenetration = p.ArmorPenetration,
                    Explosion = _explosiveEntityExamine.GetExplosiveInfo(projectileEntity)
                };
            }
        }

        return null;
    }

    private ExamineCartridgeInfo? GetCartridjeDamageInfo(CartridgeAmmoComponent cartrigjeAmmo)
    {
        if (!ProtoManager.TryIndex<EntityPrototype>(cartrigjeAmmo.Prototype, out var projectileEntity))
            return null;

        return GetProjectileDamageInfo(projectileEntity);
    }
    //Exodus AdvancedWeaponExamine End

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

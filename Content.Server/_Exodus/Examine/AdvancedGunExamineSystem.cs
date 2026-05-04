// (c) Space Exodus Team - EXDS-RL with CLA
// Authors: Provstat
using Content.Server.Emp;
using Content.Server.Weapons.Ranged.Systems;
using Content.Shared._Exodus.Examine.Damage;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Damage.Events;
using Content.Shared.Weapons.Ranged.Components;
using Robust.Shared.Prototypes;

namespace Content.Server._Exodus.Examine;

public sealed class AdvancedGunExamineSystem : EntitySystem
{
    [Dependency] private readonly ItemSlotsSystem _itemSlots = default!;
    [Dependency] private readonly GunSystem _gun = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<GunComponent, DamageExamineEvent>(OnDamageExamine);
        SubscribeLocalEvent<BallisticAmmoProviderComponent, DamageExamineEvent>(OnDamageExamine);
        SubscribeLocalEvent<BasicEntityAmmoProviderComponent, DamageExamineEvent>(OnDamageExamine);

        base.Initialize();
    }

    //The weapon may contain a self-loading, locked magazine. This is the only way to view its projectile characteristics.
    private void OnDamageExamine(Entity<GunComponent> ent, ref DamageExamineEvent args)
    {
        if (!_itemSlots.TryGetSlot(ent, "gun_magazine", out var slot))
            return;

        if (slot.Item is null)
            return;

        if (!TryComp(slot.Item.Value, out BallisticAmmoProviderComponent? ballisticAmmoProvider))
            return;

        if (ballisticAmmoProvider.Proto is not null)
            _gun.AddProjectileInfoToExamineMessage(args.Message, ballisticAmmoProvider.Proto);
    }

    private void OnDamageExamine(Entity<BallisticAmmoProviderComponent> ent, ref DamageExamineEvent args)
    {
        if (ent.Comp.Proto is not null)
            _gun.AddProjectileInfoToExamineMessage(args.Message, ent.Comp.Proto);
    }

    private void OnDamageExamine(Entity<BasicEntityAmmoProviderComponent> ent, ref DamageExamineEvent args)
    {
        _gun.AddProjectileInfoToExamineMessage(args.Message, ent.Comp.Proto);
    }

    public ExamineEmpInfo? GetEmpInfo(EntityPrototype proto)
    {
        if (proto.Components
           .TryGetValue(Factory.GetComponentName<EmpOnTriggerComponent>(), out var empOnTrigger))
        {
            var comp = (EmpOnTriggerComponent)empOnTrigger.Component;
            return new()
            {
                Energy = comp.EnergyConsumption,
                Range = comp.Range,
                Time = comp.DisableDuration
            };
        }

        return null;
    }
}

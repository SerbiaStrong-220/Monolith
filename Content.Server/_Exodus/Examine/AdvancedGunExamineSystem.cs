
using Content.Server.Weapons.Ranged.Systems;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Damage.Events;
using Content.Shared.Weapons.Ranged.Components;
using Robust.Server.Containers;

namespace Content.Server._Exodus.Examine;

public sealed class AdvancedGunExamineSystem : EntitySystem
{
    [Dependency] private readonly ItemSlotsSystem _itemSlots = default!;
    [Dependency] private readonly GunSystem _gun = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<GunComponent, DamageExamineEvent>(OnDamageExamine);

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
            _gun.AddCartridgeInfoToExamineMessage(args.Message, ballisticAmmoProvider.Proto);
    }
}

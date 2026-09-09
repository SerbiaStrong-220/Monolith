using Content.Server._Crescent.ShipShields;
using Content.Shared._Crescent.ShipShields;
using Content.Shared._Exodus.ShipShields;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Examine;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Containers;

namespace Content.Server._Exodus.ShipShields;

/// <summary>
/// Lets a CDM Bastion shield consume a reserve cartridge to avert one overload.
/// </summary>
public sealed class CdmShieldReserveSystem : EntitySystem
{
    [Dependency] private readonly SharedAppearanceSystem _appearance = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly ItemSlotsSystem _itemSlots = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<CdmShieldReserveComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<CdmShieldReserveComponent, EntInsertedIntoContainerMessage>(OnCartridgeInserted);
        SubscribeLocalEvent<CdmShieldReserveComponent, EntRemovedFromContainerMessage>(OnCartridgeRemoved);
        SubscribeLocalEvent<CdmShieldReserveComponent, ExaminedEvent>(OnExamined);
        SubscribeLocalEvent<CdmShieldReserveComponent, ShipShieldOverloadAttemptEvent>(OnOverloadAttempt);
    }

    private void OnStartup(Entity<CdmShieldReserveComponent> ent, ref ComponentStartup args)
    {
        UpdateAppearance(ent);
    }

    private void OnCartridgeInserted(Entity<CdmShieldReserveComponent> ent, ref EntInsertedIntoContainerMessage args)
    {
        if (IsReserveSlot(args.Container.ID))
            UpdateAppearance(ent);
    }

    private void OnCartridgeRemoved(Entity<CdmShieldReserveComponent> ent, ref EntRemovedFromContainerMessage args)
    {
        if (IsReserveSlot(args.Container.ID))
            UpdateAppearance(ent);
    }

    private void OnExamined(Entity<CdmShieldReserveComponent> ent, ref ExaminedEvent args)
    {
        if (!args.IsInDetailsRange)
            return;

        args.PushMarkup(Loc.GetString(
            "cdm-shield-generator-reserve-examine",
            ("cartridges", GetCartridgeCount(ent)),
            ("maximum", ent.Comp.MaxCartridges),
            ("reserve", MathF.Round(ent.Comp.EmergencyShieldFraction * 100f))));
    }

    private void OnOverloadAttempt(Entity<CdmShieldReserveComponent> ent, ref ShipShieldOverloadAttemptEvent args)
    {
        if (args.Cancelled ||
            args.Cause != ShipShieldOverloadCause.Damage ||
            !args.PoweredBeforeLoad)
            return;

        if (!TryComp<ShipShieldEmitterComponent>(ent.Owner, out var emitter)
            || emitter.Shield is not { } shield
            || TerminatingOrDeleted(shield)
            || EntityManager.IsQueuedForDeletion(shield)
            || !TryConsumeCartridge(ent, out var remainingCartridges))
        {
            return;
        }

        var reserveFraction = Math.Clamp(ent.Comp.EmergencyShieldFraction, 0.01f, 1f);
        var safeDamage = ShipShieldsSystem.CalculateSafeDamageAfterOverload(emitter, 1f - reserveFraction);
        emitter.Damage = Math.Min(emitter.Damage, safeDamage);
        emitter.Recharging = false;
        emitter.OverloadAccumulator = 0f;
        emitter.DamageOverloadStartedTick = null;
        args.Cancelled = true;

        UpdateAppearance(ent, remainingCartridges);
        _audio.PlayPvs(emitter.PowerUpSound, ent.Owner);
    }

    private bool TryConsumeCartridge(Entity<CdmShieldReserveComponent> ent, out int remainingCartridges)
    {
        remainingCartridges = 0;
        var cartridgeCount = GetCartridgeCount(ent);
        if (cartridgeCount == 0)
            return false;

        for (var i = 0; i < ent.Comp.MaxCartridges; i++)
        {
            if (!_itemSlots.TryGetSlot(ent.Owner, CdmShieldReserveComponent.GetSlotId(i), out var slot)
                || slot.Item is not { } cartridge
                || TerminatingOrDeleted(cartridge)
                || EntityManager.IsQueuedForDeletion(cartridge)
                || !HasComp<CdmShieldReserveCartridgeComponent>(cartridge))
            {
                continue;
            }

            if (!TryQueueDel(cartridge))
                continue;

            remainingCartridges = cartridgeCount - 1;
            return true;
        }

        return false;
    }

    private int GetCartridgeCount(Entity<CdmShieldReserveComponent> ent)
    {
        var count = 0;
        for (var i = 0; i < ent.Comp.MaxCartridges; i++)
        {
            if (!_itemSlots.TryGetSlot(ent.Owner, CdmShieldReserveComponent.GetSlotId(i), out var slot)
                || slot.Item is not { } cartridge
                || TerminatingOrDeleted(cartridge)
                || EntityManager.IsQueuedForDeletion(cartridge)
                || !HasComp<CdmShieldReserveCartridgeComponent>(cartridge))
            {
                continue;
            }

            count++;
        }

        return count;
    }

    private void UpdateAppearance(Entity<CdmShieldReserveComponent> ent, int? cartridgeCount = null)
    {
        var count = Math.Clamp(
            cartridgeCount ?? GetCartridgeCount(ent),
            0,
            Math.Max(ent.Comp.MaxCartridges, 0));
        _appearance.SetData(ent.Owner, CdmShieldReserveVisuals.CartridgeCount, count);
    }

    private static bool IsReserveSlot(string slotId)
    {
        return slotId.StartsWith(CdmShieldReserveComponent.SlotPrefix, StringComparison.Ordinal);
    }
}

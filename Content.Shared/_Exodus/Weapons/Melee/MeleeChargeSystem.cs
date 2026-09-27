using Content.Shared.Charges.Components;
using Content.Shared.Charges.Systems;
using Content.Shared.Damage;
using Content.Shared.Examine;
using Content.Shared.Hands;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Item.ItemToggle;
using Content.Shared.Item.ItemToggle.Components;
using Content.Shared.Whitelist;
using Content.Shared.Wieldable.Components;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Network;

namespace Content.Shared._Exodus.Weapons.Melee;

/// <summary>Event-driven charge lifecycle and per-target melee charge transactions.</summary>
public sealed class MeleeChargeSystem : EntitySystem
{
    [Dependency] private SharedChargesSystem _charges = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private ItemToggleSystem _toggle = default!;
    [Dependency] private EntityWhitelistSystem _whitelist = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private INetManager _net = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<MeleeChargeComponent, ItemToggledEvent>(OnToggled);
        SubscribeLocalEvent<MeleeChargeComponent, GotUnequippedHandEvent>(OnUnequipped);
        SubscribeLocalEvent<MeleeChargeComponent, ExaminedEvent>(OnExamined);
    }

    /// <summary>
    /// Reserve before damage so callbacks or other targets cannot spend the same charge twice.
    /// Only authoritative hits spend charges; misses and damage queries never call this method.
    /// </summary>
    public bool TryReserveHit(EntityUid weapon, EntityUid user, EntityUid target, out MeleeChargeUse use)
    {
        use = default;
        if (_net.IsClient || !TryComp<MeleeChargeComponent>(weapon, out var charge) ||
            charge.ChargesPerHit <= 0 || !charge.BonusDamage.AnyPositive() ||
            !IsActive((weapon, charge), user) ||
            _whitelist.IsWhitelistFail(charge.TargetWhitelist, target) ||
            !TryComp<LimitedChargesComponent>(weapon, out var counter) ||
            counter.Charges < charge.ChargesPerHit)
            return false;

        var cost = charge.ConsumeAllCharges ? counter.Charges : charge.ChargesPerHit;
        var damage = charge.ConsumeAllCharges ? charge.BonusDamage * (float) cost : charge.BonusDamage;
        use = new MeleeChargeUse((weapon, charge), counter, cost, damage);
        _charges.UseCharges(weapon, use.Cost, counter);
        return true;
    }

    /// <summary>Commit a successful hit, or restore its reservation when no damage was applied.</summary>
    public void CompleteHit(in MeleeChargeUse use, EntityUid user, EntityUid target, DamageSpecifier? result)
    {
        if (_net.IsClient || Deleted(use.Weapon) ||
            !TryComp<MeleeChargeComponent>(use.Weapon, out var charge) || charge != use.Weapon.Comp ||
            !TryComp<LimitedChargesComponent>(use.Weapon, out var counter) || counter != use.Counter)
            return;

        if (result == null || result.GetTotal() <= 0)
        {
            // A callback may have switched off or removed the weapon; do not undo that reset.
            if (IsActive(use.Weapon, user))
                _charges.AddCharges(use.Weapon, use.Cost, counter);
            return;
        }

        // Leave the impact at its location so deleting/moving the struck entity cannot cut off its tail.
        var coordinates = Transform(Deleted(target) ? use.Weapon.Owner : target).Coordinates;
        _audio.PlayPvs(use.Weapon.Comp.HitSound, coordinates);
    }

    private bool IsActive(Entity<MeleeChargeComponent> ent, EntityUid user)
    {
        if (ent.Comp.RequiresActivation && !_toggle.IsActivated((ent.Owner, null)))
            return false;

        if (ent.Comp.RequiresHeld && !_hands.IsHolding((user, null), ent.Owner))
            return false;

        return !ent.Comp.RequiresWield ||
               (TryComp<WieldableComponent>(ent, out var wieldable) && wieldable.Wielded);
    }

    private void OnToggled(Entity<MeleeChargeComponent> ent, ref ItemToggledEvent args)
    {
        if (!args.Activated && ent.Comp.ResetOnDeactivate)
            ResetCharges(ent);
    }

    private void OnUnequipped(Entity<MeleeChargeComponent> ent, ref GotUnequippedHandEvent args)
    {
        if (ent.Comp.ResetOnHandUnequipped)
            ResetCharges(ent);
    }

    private void ResetCharges(Entity<MeleeChargeComponent> ent)
    {
        if (_net.IsServer && TryComp<LimitedChargesComponent>(ent, out var counter))
            _charges.UseCharges(ent, counter.Charges, counter);
    }

    private void OnExamined(Entity<MeleeChargeComponent> ent, ref ExaminedEvent args)
    {
        if (!args.IsInDetailsRange || !TryComp<LimitedChargesComponent>(ent, out var counter))
            return;

        var message = ent.Comp.ConsumeAllCharges ? "melee-charge-examine-discharge" : "melee-charge-examine";
        args.PushText(Loc.GetString(message,
            ("effect", Loc.GetString(ent.Comp.EffectName)),
            ("charges", counter.Charges), ("max", counter.MaxCharges),
            ("damage", ent.Comp.BonusDamage.GetTotal()), ("cost", ent.Comp.ChargesPerHit)));
    }
}

/// <summary>A local reservation for one target, never shared between targets or stored on the system.</summary>
public readonly record struct MeleeChargeUse(
    Entity<MeleeChargeComponent> Weapon,
    LimitedChargesComponent Counter,
    int Cost,
    DamageSpecifier Damage);

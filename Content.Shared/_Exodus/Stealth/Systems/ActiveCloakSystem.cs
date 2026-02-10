using Content.Shared.Actions;
using Content.Shared.Exodus.Stealth.Components;
using Content.Shared.Inventory;
using Content.Shared.Item;
using Content.Shared.Item.ItemToggle.Components;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;
using Content.Shared.Projectiles;
using Content.Shared.Weapons.Melee.Events;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Timing;

namespace Content.Shared.Exodus.Stealth;

public sealed partial class ActiveCloakSystem : EntitySystem
{
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly SharedStealthSystem _stealth = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly MobStateSystem _mobState = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ActiveCloakComponent, GetItemActionsEvent>(OnGetItemActions);
        SubscribeLocalEvent<ActiveCloakComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<ActiveCloakComponent, ToggleActiveCloakEvent>(OnToggleCloak);
        SubscribeLocalEvent<ActiveCloakComponent, ItemToggledEvent>(OnItemToggled);

        // Breaking events - relayed from inventory when on clothing
        SubscribeLocalEvent<ActiveCloakComponent, InventoryRelayedEvent<AttackedEvent>>(OnAttacked);
        SubscribeLocalEvent<ActiveCloakComponent, InventoryRelayedEvent<ProjectileHitTargetEvent>>(OnProjectileHit);
        SubscribeLocalEvent<ActiveCloakComponent, InventoryRelayedEvent<GunShotUserEvent>>(OnGunShot);
        SubscribeLocalEvent<ActiveCloakComponent, InventoryRelayedEvent<MeleeHitEvent>>(OnMeleeHit);
        SubscribeLocalEvent<ActiveCloakComponent, InventoryRelayedEvent<MobStateChangedEvent>>(OnMobStateChanged);

        // Breaking events - direct when on entity
        SubscribeLocalEvent<ActiveCloakComponent, AttackedEvent>(OnAttackedDirect);
        SubscribeLocalEvent<ActiveCloakComponent, ProjectileHitTargetEvent>(OnProjectileHitDirect);
        SubscribeLocalEvent<ActiveCloakComponent, GunShotUserEvent>(OnGunShotDirect);
        SubscribeLocalEvent<ActiveCloakComponent, MeleeHitEvent>(OnMeleeHitDirect);
        SubscribeLocalEvent<ActiveCloakComponent, MobStateChangedEvent>(OnMobStateChangedDirect);
    }

    private void OnGetItemActions(EntityUid uid, ActiveCloakComponent comp, GetItemActionsEvent args)
    {
        if (comp.ToggleActionId != null)
            args.AddAction(ref comp.ToggleAction, comp.ToggleActionId, uid);
    }

    private void OnShutdown(EntityUid uid, ActiveCloakComponent comp, ComponentShutdown args)
    {
        // Remove stealth if active
        if (comp.Enabled)
        {
            var target = HasComp<StealthComponent>(uid) ? uid : Transform(uid).ParentUid;

            _stealth.RemoveRequest(nameof(ActiveCloakSystem), target);
        }
    }

    private void OnToggleCloak(EntityUid uid, ActiveCloakComponent comp, ToggleActiveCloakEvent args)
    {
        if (comp.Enabled)
        {
            DisableCloak(args.Performer, comp);
        }
        else
        {
            TryEnableCloak(args.Performer, uid, comp);
        }

        args.Handled = true;
    }

    private void OnItemToggled(EntityUid uid, ActiveCloakComponent comp, ItemToggledEvent args)
    {
        if (!args.Activated)
            DisableCloak(args.User ?? uid, comp);
        // don't enable cloak here, it should be in separate action
    }

    private void OnAttacked(EntityUid uid, ActiveCloakComponent comp, InventoryRelayedEvent<AttackedEvent> args)
    {
        if (!comp.Enabled)
            return;

        BreakCloak(args.Args.Target, comp);
    }

    private void OnProjectileHit(EntityUid uid, ActiveCloakComponent comp, InventoryRelayedEvent<ProjectileHitTargetEvent> args)
    {
        if (!comp.Enabled)
            return;

        BreakCloak(args.Args.Target, comp);
    }

    private void OnGunShot(EntityUid uid, ActiveCloakComponent comp, InventoryRelayedEvent<GunShotUserEvent> args)
    {
        if (!comp.Enabled)
            return;

        BreakCloak(args.Args.User, comp);
    }

    private void OnMeleeHit(EntityUid uid, ActiveCloakComponent comp, InventoryRelayedEvent<MeleeHitEvent> args)
    {
        if (!comp.Enabled || args.Args.HitEntities.Count == 0)
            return;

        BreakCloak(args.Args.User, comp);
    }

    private void OnMobStateChanged(EntityUid uid, ActiveCloakComponent comp, InventoryRelayedEvent<MobStateChangedEvent> args)
    {
        if (!comp.Enabled)
            return;

        if (!_mobState.IsIncapacitated(args.Args.Target))
            return;

        BreakCloak(args.Args.Target, comp);
    }

    private void OnAttackedDirect(EntityUid uid, ActiveCloakComponent comp, AttackedEvent args)
    {
        if (!comp.Enabled)
            return;

        BreakCloak(uid, comp);
    }

    private void OnProjectileHitDirect(EntityUid uid, ActiveCloakComponent comp, ref ProjectileHitTargetEvent args)
    {
        if (!comp.Enabled)
            return;

        BreakCloak(args.Target, comp);
    }

    private void OnGunShotDirect(EntityUid uid, ActiveCloakComponent comp, ref GunShotUserEvent args)
    {
        if (!comp.Enabled)
            return;

        BreakCloak(uid, comp);
    }

    private void OnMeleeHitDirect(EntityUid uid, ActiveCloakComponent comp, MeleeHitEvent args)
    {
        if (!comp.Enabled || args.HitEntities.Count == 0)
            return;

        BreakCloak(uid, comp);
    }

    private void OnMobStateChangedDirect(EntityUid uid, ActiveCloakComponent comp, MobStateChangedEvent args)
    {
        if (!comp.Enabled)
            return;

        if (!_mobState.IsIncapacitated(uid))
            return;

        BreakCloak(uid, comp);
    }

    private void TryEnableCloak(EntityUid target, EntityUid cloak, ActiveCloakComponent comp)
    {
        // if its toggleable and isn't toggled then the cloak is off too
        if (TryComp<ItemToggleComponent>(cloak, out var itemToggle) && !itemToggle.Activated)
            return;

        // Check cooldown
        if (comp.BrokenTime != null)
        {
            var remainingTime = (comp.BrokenTime.Value + comp.Cooldown) - _timing.CurTime;
            if (remainingTime > TimeSpan.Zero)
            {
                var seconds = (int)Math.Ceiling(remainingTime.TotalSeconds);
                _popup.PopupPredicted(Loc.GetString("active-cloak-cooldown", ("seconds", seconds)), target, target);
                return;
            }
        }

        // Enable cloak
        if (!_stealth.RequestStealth(target, nameof(ActiveCloakSystem), comp.Stealth))
            return;

        comp.Enabled = true;

        // Play sound
        if (comp.ActivateSound != null)
            _audio.PlayPredicted(comp.ActivateSound, target, target);

        _popup.PopupPredicted(Loc.GetString("active-cloak-enabled"), target, target);
    }

    private void DisableCloak(EntityUid target, ActiveCloakComponent comp)
    {
        if (!_stealth.RemoveRequest(nameof(ActiveCloakSystem), target))
            return;

        comp.Enabled = false;

        // Play sound
        if (comp.DeactivateSound != null)
            _audio.PlayPredicted(comp.DeactivateSound, target, target);

        _popup.PopupPredicted(Loc.GetString("active-cloak-disabled"), target, target);
    }

    private void BreakCloak(EntityUid target, ActiveCloakComponent comp)
    {
        if (!_stealth.RemoveRequest(nameof(ActiveCloakSystem), target))
            return;

        comp.Enabled = false;
        comp.BrokenTime = _timing.CurTime;

        // Play sound
        if (comp.BreakSound != null)
            _audio.PlayPvs(comp.BreakSound, target);

        _popup.PopupPredicted(Loc.GetString("active-cloak-broken"), target, target, PopupType.MediumCaution);
    }
}

public sealed partial class ToggleActiveCloakEvent : InstantActionEvent
{
}

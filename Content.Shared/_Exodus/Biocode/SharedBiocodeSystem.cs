using Content.Shared._Mono.ShipRepair;
using Content.Shared.Actions;
using Content.Shared.Body.Components;
using Content.Shared.Body.Systems;
using Content.Shared.Interaction;
using Content.Shared.Interaction.Events;
using Content.Shared.Inventory.Events;
using Content.Shared.Ninja.Systems;
using Content.Shared.Popups;
using Content.Shared.UserInterface;
using Content.Shared.Whitelist;
using Robust.Shared.Timing;

namespace Content.Shared._Exodus.Biocode;

/// <summary>
/// Generic biocode access gate. Blocks interaction, equipping and dashing for users that do not
/// match the configured access conditions. The trigger-on-reject reaction lives in the server-only
/// system because it relies on the trigger pipeline.
/// </summary>
public sealed class SharedBiocodeSystem : EntitySystem
{
    [Dependency] private readonly EntityWhitelistSystem _whitelist = default!;
    [Dependency] private readonly SharedBodySystem _body = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<BiocodeComponent, BeforeRangedInteractEvent>(OnBeforeRangedInteract);
        SubscribeLocalEvent<BiocodeComponent, AfterInteractEvent>(OnAfterInteract, before: [typeof(SharedShipRepairSystem)]);
        SubscribeLocalEvent<BiocodeComponent, UseInHandEvent>(OnUseInHand, before: [typeof(ActivatableUISystem)]);
        SubscribeLocalEvent<BiocodeComponent, ActivateInWorldEvent>(OnActivateInWorld, before: [typeof(ActivatableUISystem)]);
        SubscribeLocalEvent<BiocodeComponent, ActivatableUIOpenAttemptEvent>(OnActivatableUiOpenAttempt);
        // Run after action-granting systems so their actions are already in the set and can be cleared.
        SubscribeLocalEvent<BiocodeComponent, GetItemActionsEvent>(OnGetItemActions, after: [typeof(DashAbilitySystem)]);
        SubscribeLocalEvent<BiocodeComponent, BeingEquippedAttemptEvent>(OnBeingEquippedAttempt);
    }

    /// <summary>
    /// A user is authorized if there are no conditions, or they pass the user whitelist,
    /// or they carry a body organ that passes the organ whitelist.
    /// </summary>
    public bool IsAllowed(Entity<BiocodeComponent> ent, EntityUid user)
    {
        if (ent.Comp.Whitelist == null && ent.Comp.OrganWhitelist == null)
            return true;

        if (ent.Comp.Whitelist != null && _whitelist.IsValid(ent.Comp.Whitelist, user))
            return true;

        return ent.Comp.OrganWhitelist != null && HasMatchingOrgan(ent.Comp.OrganWhitelist, user);
    }

    private bool HasMatchingOrgan(EntityWhitelist organWhitelist, EntityUid user)
    {
        if (!TryComp<BodyComponent>(user, out var body))
            return false;

        foreach (var (organ, _) in _body.GetBodyOrgans(user, body))
        {
            if (_whitelist.IsValid(organWhitelist, organ))
                return true;
        }

        return false;
    }

    private bool CanInteract(Entity<BiocodeComponent> ent, EntityUid user)
    {
        if (IsAllowed(ent, user))
            return true;

        ShowReject(ent, user);
        return false;
    }

    private void ShowReject(Entity<BiocodeComponent> ent, EntityUid user)
    {
        var curTime = _timing.CurTime;
        if (curTime < ent.Comp.NextPopupAllowed)
            return;

        _popup.PopupClient(Loc.GetString(ent.Comp.RejectPopup), ent, user, PopupType.MediumCaution);
        ent.Comp.NextPopupAllowed = curTime + ent.Comp.PopupDedupeWindow;
    }

    private void OnBeforeRangedInteract(Entity<BiocodeComponent> ent, ref BeforeRangedInteractEvent args)
    {
        if (!ent.Comp.BlockInteraction || CanInteract(ent, args.User))
            return;

        args.Handled = true;
    }

    private void OnAfterInteract(Entity<BiocodeComponent> ent, ref AfterInteractEvent args)
    {
        if (!ent.Comp.BlockInteraction || args.Handled || CanInteract(ent, args.User))
            return;

        args.Handled = true;
    }

    private void OnUseInHand(Entity<BiocodeComponent> ent, ref UseInHandEvent args)
    {
        if (!ent.Comp.BlockInteraction || args.Handled || CanInteract(ent, args.User))
            return;

        args.Handled = true;
    }

    private void OnActivateInWorld(Entity<BiocodeComponent> ent, ref ActivateInWorldEvent args)
    {
        if (!ent.Comp.BlockInteraction || args.Handled || CanInteract(ent, args.User))
            return;

        args.Handled = true;
    }

    private void OnActivatableUiOpenAttempt(Entity<BiocodeComponent> ent, ref ActivatableUIOpenAttemptEvent args)
    {
        if (!ent.Comp.BlockInteraction || args.Cancelled || CanInteract(ent, args.User))
            return;

        args.Cancel();
    }

    private void OnGetItemActions(Entity<BiocodeComponent> ent, ref GetItemActionsEvent args)
    {
        if (!ent.Comp.BlockItemActions || IsAllowed(ent, args.User))
            return;

        args.Actions.Clear();
    }

    private void OnBeingEquippedAttempt(Entity<BiocodeComponent> ent, ref BeingEquippedAttemptEvent args)
    {
        if (!ent.Comp.BlockEquip || args.Cancelled || IsAllowed(ent, args.EquipTarget))
            return;

        args.Cancel();
        ShowReject(ent, args.EquipTarget);
    }
}

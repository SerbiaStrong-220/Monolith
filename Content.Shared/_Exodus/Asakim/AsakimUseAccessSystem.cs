using Content.Shared._Mono.ShipRepair;
using Content.Shared.Interaction;
using Content.Shared.Interaction.Events;
using Content.Shared.Popups;
using Content.Shared.UserInterface;
using Robust.Shared.Timing;

namespace Content.Shared._Exodus.Asakim;

public sealed class AsakimUseAccessSystem : EntitySystem
{
    [Dependency] private readonly AsakimIdentitySystem _asakim = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<AsakimUseAccessComponent, BeforeRangedInteractEvent>(OnBeforeRangedInteract);
        SubscribeLocalEvent<AsakimUseAccessComponent, AfterInteractEvent>(OnAfterInteract, before: [typeof(SharedShipRepairSystem)]);
        SubscribeLocalEvent<AsakimUseAccessComponent, UseInHandEvent>(OnUseInHand, before: [typeof(ActivatableUISystem)]);
        SubscribeLocalEvent<AsakimUseAccessComponent, ActivateInWorldEvent>(OnActivateInWorld, before: [typeof(ActivatableUISystem)]);
        SubscribeLocalEvent<AsakimUseAccessComponent, ActivatableUIOpenAttemptEvent>(OnActivatableUiOpenAttempt);
    }

    private void OnBeforeRangedInteract(Entity<AsakimUseAccessComponent> ent, ref BeforeRangedInteractEvent args)
    {
        if (CanUse(ent, args.User))
            return;

        args.Handled = true;
    }

    private void OnAfterInteract(Entity<AsakimUseAccessComponent> ent, ref AfterInteractEvent args)
    {
        if (args.Handled || CanUse(ent, args.User))
            return;

        args.Handled = true;
    }

    private void OnUseInHand(Entity<AsakimUseAccessComponent> ent, ref UseInHandEvent args)
    {
        if (args.Handled || CanUse(ent, args.User))
            return;

        args.Handled = true;
    }

    private void OnActivateInWorld(Entity<AsakimUseAccessComponent> ent, ref ActivateInWorldEvent args)
    {
        if (args.Handled || CanUse(ent, args.User))
            return;

        args.Handled = true;
    }

    private void OnActivatableUiOpenAttempt(Entity<AsakimUseAccessComponent> ent, ref ActivatableUIOpenAttemptEvent args)
    {
        if (args.Cancelled || CanUse(ent, args.User))
            return;

        args.Cancel();
    }

    private bool CanUse(Entity<AsakimUseAccessComponent> ent, EntityUid user)
    {
        if (_asakim.IsAsakim(user))
            return true;

        var curTime = _timing.CurTime;
        if (curTime >= ent.Comp.NextPopupAllowed)
        {
            _popup.PopupClient(Loc.GetString(ent.Comp.RejectedPopup), ent, user, PopupType.MediumCaution);
            ent.Comp.NextPopupAllowed = curTime + ent.Comp.PopupDedupeWindow;
        }

        return false;
    }
}

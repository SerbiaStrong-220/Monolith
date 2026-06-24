using Content.Server.GameTicking;
using Content.Shared._Exodus.Shuttles.Components;
using Content.Shared.Interaction;
using Content.Shared.Interaction.Events;
using Content.Shared.Popups;

namespace Content.Server._Exodus.Shuttles.Systems;

public sealed partial class ShuttleEventBeaconSystem : EntitySystem
{
    [Dependency] private GameTicker _gameTicker = default!;
    [Dependency] private SharedPopupSystem _popup = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ShuttleEventBeaconComponent, UseInHandEvent>(OnUseInHand);
        SubscribeLocalEvent<ShuttleEventBeaconComponent, ActivateInWorldEvent>(OnActivateInWorld);
    }

    private void OnUseInHand(Entity<ShuttleEventBeaconComponent> ent, ref UseInHandEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;
        args.ApplyDelay = false;
        TryActivate(ent, args.User);
    }

    private void OnActivateInWorld(Entity<ShuttleEventBeaconComponent> ent, ref ActivateInWorldEvent args)
    {
        if (args.Handled || !args.Complex)
            return;

        args.Handled = true;
        TryActivate(ent, args.User);
    }

    private void TryActivate(Entity<ShuttleEventBeaconComponent> ent, EntityUid user)
    {
        if (Deleted(ent))
            return;

        var rule = _gameTicker.ForceAddGameRule(ent.Comp.Rule);
        if (!rule.IsValid() || !_gameTicker.StartGameRule(rule))
        {
            _popup.PopupEntity(Loc.GetString(ent.Comp.FailurePopup), user, user);
            return;
        }

        _popup.PopupEntity(Loc.GetString(ent.Comp.SuccessPopup), user, user);

        if (ent.Comp.ConsumeOnSuccess)
            QueueDel(ent);
    }
}

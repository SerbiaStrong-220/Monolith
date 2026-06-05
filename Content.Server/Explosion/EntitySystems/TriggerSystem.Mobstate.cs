using Content.Server.Explosion.Components;
using Content.Shared.Explosion.Components;
using Content.Shared.Implants;
using Content.Shared.Inventory; // Exodus-trigger-inventory-relay
using Content.Shared.Interaction.Events;
using Content.Shared.Mobs;

namespace Content.Server.Explosion.EntitySystems;

public sealed partial class TriggerSystem
{
    private void InitializeMobstate()
    {
        SubscribeLocalEvent<TriggerOnMobstateChangeComponent, MobStateChangedEvent>(OnMobStateChanged);
        SubscribeLocalEvent<TriggerOnMobstateChangeComponent, SuicideEvent>(OnSuicide);

        SubscribeLocalEvent<TriggerOnMobstateChangeComponent, ImplantRelayEvent<SuicideEvent>>(OnSuicideRelay);
        SubscribeLocalEvent<TriggerOnMobstateChangeComponent, ImplantRelayEvent<MobStateChangedEvent>>(OnMobStateRelay);
        SubscribeLocalEvent<TriggerOnMobstateChangeComponent, InventoryRelayedEvent<MobStateChangedEvent>>(OnMobStateInventoryRelay); // Exodus-trigger-inventory-relay
    }

    private void OnMobStateChanged(EntityUid uid, TriggerOnMobstateChangeComponent component, MobStateChangedEvent args)
    {
        HandleMobStateChanged(uid, component, args, args.Origin);
    }

    private void HandleMobStateChanged(EntityUid uid, TriggerOnMobstateChangeComponent component, MobStateChangedEvent args, EntityUid? user)
    {
        if (!component.MobState.Contains(args.NewMobState))
            return;

        // Exodus-begin trigger-old-mobstate-filter
        if (component.OldMobState != null && !component.OldMobState.Contains(args.OldMobState))
            return;
        // Exodus-end trigger-old-mobstate-filter

        //This chains Mobstate Changed triggers with OnUseTimerTrigger if they have it
        //Very useful for things that require a mobstate change and a timer
        if (TryComp<OnUseTimerTriggerComponent>(uid, out var timerTrigger))
        {
            HandleTimerTrigger(
                uid,
                user,
                timerTrigger.Delay,
                timerTrigger.BeepInterval,
                timerTrigger.InitialBeepDelay,
                timerTrigger.BeepSound);
        }
        else
            Trigger(uid, user);
    }

    /// <summary>
    /// Checks if the user has any implants that prevent suicide to avoid some cheesy strategies
    /// Prevents suicide by handling the event without killing the user
    /// </summary>
    private void OnSuicide(EntityUid uid, TriggerOnMobstateChangeComponent component, SuicideEvent args)
    {
        if (args.Handled)
            return;

        if (!component.PreventSuicide)
            return;

        _popupSystem.PopupEntity(Loc.GetString("suicide-prevented"), args.Victim, args.Victim);
        args.Handled = true;
    }

    private void OnSuicideRelay(EntityUid uid, TriggerOnMobstateChangeComponent component, ImplantRelayEvent<SuicideEvent> args)
    {
        OnSuicide(uid, component, args.Event);
    }

    private void OnMobStateRelay(EntityUid uid, TriggerOnMobstateChangeComponent component, ImplantRelayEvent<MobStateChangedEvent> args)
    {
        HandleMobStateChanged(uid, component, args.Event, args.Event.Origin);
    }

    // Exodus-begin trigger-inventory-relay
    private void OnMobStateInventoryRelay(Entity<TriggerOnMobstateChangeComponent> ent, ref InventoryRelayedEvent<MobStateChangedEvent> args)
    {
        HandleMobStateChanged(ent.Owner, ent.Comp, args.Args, args.Args.Target);
    }
    // Exodus-end trigger-inventory-relay
}

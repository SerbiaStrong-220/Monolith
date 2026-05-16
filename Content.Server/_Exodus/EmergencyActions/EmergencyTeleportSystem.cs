using Content.Server.Actions;
using Content.Server.Teleportation;
using Content.Shared._Exodus.EmergencyActions;
using Content.Shared.Clothing;
using Content.Shared.Inventory;
using Content.Shared.Mobs;
using Content.Shared.Popups;
using Robust.Shared.Timing;

namespace Content.Server._Exodus.EmergencyActions;

/// <summary>
/// Teleports the wearer randomly when they transition from Alive to Critical. Grants a passive
/// action so the wearer can see the cooldown; manual activation only shows a passive popup.
/// Generic — no Asakim or other identity check is performed.
/// </summary>
public sealed class EmergencyTeleportSystem : EntitySystem
{
    [Dependency] private readonly ActionsSystem _actions = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly TeleportSystem _teleport = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<EmergencyTeleportComponent, ClothingGotEquippedEvent>(OnEquipped);
        SubscribeLocalEvent<EmergencyTeleportComponent, ClothingGotUnequippedEvent>(OnUnequipped);
        SubscribeLocalEvent<EmergencyTeleportComponent, InventoryRelayedEvent<MobStateChangedEvent>>(OnMobStateChanged);
        SubscribeLocalEvent<EmergencyTeleportComponent, EmergencyTeleportActionEvent>(OnAction);
    }

    private void OnEquipped(Entity<EmergencyTeleportComponent> ent, ref ClothingGotEquippedEvent args)
    {
        _actions.AddAction(args.Wearer, ref ent.Comp.ActionUid, ent.Comp.ActionProto, ent.Owner);
        SyncActionCooldown(ent);
    }

    private void OnUnequipped(Entity<EmergencyTeleportComponent> ent, ref ClothingGotUnequippedEvent args)
    {
        _actions.RemoveProvidedActions(args.Wearer, ent.Owner);
    }

    private void OnMobStateChanged(Entity<EmergencyTeleportComponent> ent, ref InventoryRelayedEvent<MobStateChangedEvent> args)
    {
        if (args.Args.NewMobState != MobState.Critical || args.Args.OldMobState != MobState.Alive)
            return;

        var wearer = args.Args.Target;
        if (Deleted(wearer))
            return;

        if (_timing.CurTime < ent.Comp.NextActivation)
        {
            SyncActionCooldown(ent);
            return;
        }

        Activate(ent, wearer);
    }

    private void OnAction(Entity<EmergencyTeleportComponent> ent, ref EmergencyTeleportActionEvent args)
    {
        _popup.PopupEntity(Loc.GetString("emergency-teleport-passive"), args.Performer, args.Performer);
    }

    private void Activate(Entity<EmergencyTeleportComponent> ent, EntityUid wearer)
    {
        ent.Comp.NextActivation = _timing.CurTime + ent.Comp.Cooldown;
        SyncActionCooldown(ent);

        _teleport.RandomTeleport(wearer, ent.Comp.Specifier);

        _popup.PopupEntity(Loc.GetString("emergency-teleport-activated"), wearer, wearer, PopupType.MediumCaution);
    }

    private void SyncActionCooldown(Entity<EmergencyTeleportComponent> ent)
    {
        if (ent.Comp.ActionUid == null || ent.Comp.NextActivation <= _timing.CurTime)
            return;

        _actions.SetCooldown(ent.Comp.ActionUid, _timing.CurTime, ent.Comp.NextActivation);
    }
}

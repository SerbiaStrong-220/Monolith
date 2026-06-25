using Content.Server.Actions;
using Content.Server.Explosion.EntitySystems;
using Content.Server.Teleportation;
using Content.Shared._Exodus.EmergencyActions;
using Content.Shared.Clothing;
using Content.Shared.Mind;
using Content.Shared.Popups;
using Robust.Shared.Timing;

namespace Content.Server._Exodus.EmergencyActions;

public sealed partial class RandomTeleportOnTriggerSystem : EntitySystem
{
    [Dependency] private ActionsSystem _actions = default!;
    [Dependency] private SharedMindSystem _mind = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private TeleportSystem _teleport = default!;
    [Dependency] private IGameTiming _timing = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<RandomTeleportOnTriggerComponent, ClothingGotEquippedEvent>(OnEquipped);
        SubscribeLocalEvent<RandomTeleportOnTriggerComponent, ClothingGotUnequippedEvent>(OnUnequipped);
        SubscribeLocalEvent<RandomTeleportOnTriggerComponent, TriggerEvent>(OnTriggered);
        SubscribeLocalEvent<RandomTeleportOnTriggerComponent, EmergencyTeleportActionEvent>(OnAction);
    }

    private void OnEquipped(Entity<RandomTeleportOnTriggerComponent> ent, ref ClothingGotEquippedEvent args)
    {
        _actions.AddAction(args.Wearer, ref ent.Comp.ActionUid, ent.Comp.ActionProto, ent.Owner);
        SyncActionCooldown(ent);
    }

    private void OnUnequipped(Entity<RandomTeleportOnTriggerComponent> ent, ref ClothingGotUnequippedEvent args)
    {
        _actions.RemoveProvidedActions(args.Wearer, ent.Owner);
    }

    private void OnTriggered(Entity<RandomTeleportOnTriggerComponent> ent, ref TriggerEvent args)
    {
        if (args.User is not { } target || Deleted(target))
            return;

        // Intentional: only teleport wearers with an attached mind. Mindless dummies cannot
        // control their actions, so randomly relocating them across the map only makes them
        // harder to find (and can land them on grids that auto-delete uncontrolled hostiles).
        if (!_mind.TryGetMind(target, out _, out _))
            return;

        if (_timing.CurTime < ent.Comp.NextActivation)
        {
            SyncActionCooldown(ent);
            return;
        }

        Activate(ent, target);
        args.Handled = true;
    }

    private void OnAction(Entity<RandomTeleportOnTriggerComponent> ent, ref EmergencyTeleportActionEvent args)
    {
        _popup.PopupEntity(Loc.GetString("emergency-teleport-passive"), args.Performer, args.Performer);
        args.Handled = true;
    }

    private void Activate(Entity<RandomTeleportOnTriggerComponent> ent, EntityUid target)
    {
        ent.Comp.NextActivation = _timing.CurTime + ent.Comp.Cooldown;
        SyncActionCooldown(ent);

        _teleport.RandomTeleport(target, ent.Comp.Specifier);

        _popup.PopupEntity(Loc.GetString("emergency-teleport-activated"), target, target, PopupType.MediumCaution);
    }

    private void SyncActionCooldown(Entity<RandomTeleportOnTriggerComponent> ent)
    {
        if (ent.Comp.ActionUid == null || ent.Comp.NextActivation <= _timing.CurTime)
            return;

        _actions.SetCooldown(ent.Comp.ActionUid, _timing.CurTime, ent.Comp.NextActivation);
    }
}

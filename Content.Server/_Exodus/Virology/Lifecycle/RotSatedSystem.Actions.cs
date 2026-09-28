using Content.Shared.Actions;
using Content.Shared._Exodus.Virology.Lifecycle;
using Content.Shared.Popups;

namespace Content.Server._Exodus.Virology.Lifecycle;

public sealed partial class RotSatedSystem
{
    [Dependency] private SharedActionsSystem _actions = default!;
    [Dependency] private SharedPopupSystem _popup = default!;

    private void InitializeActions()
    {
        SubscribeLocalEvent<RotSatedComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<RotSatedComponent, RotSatedConsumeActionEvent>(OnConsumeAction);
        SubscribeLocalEvent<RotSatedComponent, RotSatedStopActionEvent>(OnStopAction);
        SubscribeLocalEvent<RotSatedComponent, RotSatedStrikeActionEvent>(OnStrikeAction);
    }

    private void OnStartup(Entity<RotSatedComponent> ent, ref ComponentStartup args)
    {
        _actions.AddAction(ent.Owner, ref ent.Comp.ConsumeActionEntity, ent.Comp.ConsumeAction);
        _actions.AddAction(ent.Owner, ref ent.Comp.StopActionEntity, ent.Comp.StopAction);
        _actions.AddAction(ent.Owner, ref ent.Comp.StrikeActionEntity, ent.Comp.StrikeAction);
    }

    private void RemoveActions(Entity<RotSatedComponent> ent)
    {
        _actions.RemoveAction(ent.Owner, ent.Comp.ConsumeActionEntity);
        _actions.RemoveAction(ent.Owner, ent.Comp.StopActionEntity);
        _actions.RemoveAction(ent.Owner, ent.Comp.StrikeActionEntity);
    }

    private void OnConsumeAction(Entity<RotSatedComponent> ent, ref RotSatedConsumeActionEvent args)
    {
        if (args.Handled || _mobs.IsDead(ent))
            return;
        args.Handled = TryPrepareConsumption(ent, args.Target);
        if (!args.Handled)
            _popup.PopupEntity(Loc.GetString("rot-sated-cannot-consume"), ent, ent);
    }

    private void OnStopAction(Entity<RotSatedComponent> ent, ref RotSatedStopActionEvent args)
    {
        if (args.Handled || _mobs.IsDead(ent))
            return;
        CancelConsumption(ent);
        ent.Comp.BirthRequested = ent.Comp.PendingLarvae > 0;
        args.Handled = true;
    }

    private void OnStrikeAction(Entity<RotSatedComponent> ent, ref RotSatedStrikeActionEvent args)
    {
        if (args.Handled || _mobs.IsDead(ent) || _containers.IsEntityInContainer(ent)
            || !TryComp<RotDefenderComponent>(ent, out var defender)
            || ent.Comp.Activity != RotSatedActivity.None || ent.Comp.PreparingConsumption
            || _timing.CurTime < defender.NextStuckAttack)
            return;
        _combat.SetInCombatMode(ent, true);
        if (!_groundStrike.TryStrike(ent, defender.StuckTileRadius, defender.StuckDamage, defender.StuckSound))
            return;
        defender.NextStuckAttack = _timing.CurTime + defender.StuckAttackInterval;
        args.Handled = true;
    }
}

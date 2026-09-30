using Content.Shared._Exodus.Virology.Lifecycle;
using Content.Shared.Actions;

namespace Content.Server._Exodus.Virology.Lifecycle;

public sealed partial class RotHungrySystem
{
    [Dependency] private SharedActionsSystem _actions = default!;

    private void InitializeActions()
    {
        SubscribeLocalEvent<RotHungryComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<RotHungryComponent, RotHungryStrikeActionEvent>(OnStrikeAction);
    }

    private void OnStartup(Entity<RotHungryComponent> ent, ref ComponentStartup args)
    {
        _actions.AddAction(ent.Owner, ref ent.Comp.StrikeActionEntity, ent.Comp.StrikeAction);
    }

    private void OnStrikeAction(Entity<RotHungryComponent> ent, ref RotHungryStrikeActionEvent args)
    {
        if (args.Handled || !_mobs.IsAlive(ent) || _containers.IsEntityInContainer(ent)
            || _timing.CurTime < ent.Comp.NextStuckAttack)
            return;

        _combat.SetInCombatMode(ent, true);
        if (!_groundStrike.TryStrike(ent, ent.Comp.StuckTileRadius, ent.Comp.StuckDamage, ent.Comp.StuckSound))
            return;

        ent.Comp.NextStuckAttack = _timing.CurTime + ent.Comp.StuckAttackInterval;
        args.Handled = true;
    }
}

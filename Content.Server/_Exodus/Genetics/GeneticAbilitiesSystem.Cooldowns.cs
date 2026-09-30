using Content.Shared._Exodus.Actions;
using Content.Shared._Exodus.Genetics;
using Content.Shared.Actions;
using Content.Shared.Actions.Events;

namespace Content.Server._Exodus.Genetics;

public sealed partial class GeneticAbilitiesSystem
{
    private void InitializeCooldowns()
    {
        SubscribeLocalEvent<ActionCooldownDisplayComponent, ActionPerformedEvent>(OnGeneticActionPerformed);
    }

    private void OnGeneticActionPerformed(Entity<ActionCooldownDisplayComponent> ent, ref ActionPerformedEvent args)
    {
        // PerformAction resets the ordinary cooldown after the ability handler, so synchronize it afterwards.
        if (TryComp<GeneticAbilityStateComponent>(args.Performer, out var state))
            RefreshGeneticCooldowns((args.Performer, state));
    }

    private void RefreshGeneticCooldowns(Entity<GeneticAbilityStateComponent> ent)
    {
        if (TerminatingOrDeleted(ent) || !TryComp<GenomeComponent>(ent, out var genome))
            return;
        foreach (var actionUid in genome.Actions.Values)
        {
            if (actionUid is not { } uid || TerminatingOrDeleted(uid) || !_actions.TryGetActionData(uid, out var action))
                continue;
            TimeSpan available;
            TimeSpan duration;
            var freeReturn = false;
            switch (action.BaseEvent)
            {
                case GeneticTransformEvent:
                    if (!_transformationPrototypes.TryIndex(ent.Comp.Transformation, out var profile))
                        continue;
                    available = ent.Comp.TransformationAvailable;
                    duration = profile.Cooldown;
                    freeReturn = ent.Comp.FormAppearance != null;
                    _actions.SetToggled(uid, freeReturn);
                    break;
                case GeneticCloakEvent:
                    available = ent.Comp.CloakAvailable;
                    duration = ent.Comp.CloakCooldown;
                    _actions.SetToggled(uid, ent.Comp.Cloaked);
                    break;
                case GeneticHearingEvent:
                    available = ent.Comp.HearingAvailable;
                    duration = ent.Comp.HearingCooldown;
                    break;
                case GeneticWebEvent:
                    available = ent.Comp.WebAvailable;
                    duration = ent.Comp.WebCooldown;
                    break;
                case GeneticFireBreathEvent:
                    available = ent.Comp.FlameAvailable;
                    duration = ent.Comp.FlameCooldown;
                    break;
                default:
                    continue;
            }
            var display = EnsureComp<ActionCooldownDisplayComponent>(uid);
            var start = available - duration;
            if (display.Start != start || display.End != available)
            {
                display.Start = start;
                display.End = available;
                Dirty(uid, display);
            }
            if (freeReturn || available <= _timing.CurTime)
                _actions.ClearCooldown(uid);
            else
                _actions.SetCooldown(uid, start, available);
        }
    }
}

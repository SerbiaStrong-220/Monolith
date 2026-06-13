using Content.Server.Administration.Logs;
using Content.Server.Body.Systems;
using Content.Server.Explosion.EntitySystems;
using Content.Shared.Chemistry;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Database;
using Content.Shared.FixedPoint;
using Content.Shared.Popups;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Timing;

namespace Content.Server._Exodus.EmergencyActions;

/// <summary>
/// Injects a configured reagent pool into the trigger user.
/// </summary>
public sealed class AddReagentOnTriggerSystem : EntitySystem
{
    [Dependency] private readonly BloodstreamSystem _bloodstream = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly IAdminLogManager _adminLogger = default!;
    [Dependency] private readonly ReactiveSystem _reactive = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<AddReagentOnTriggerComponent, TriggerEvent>(OnTriggered);
    }

    private void OnTriggered(Entity<AddReagentOnTriggerComponent> ent, ref TriggerEvent args)
    {
        if (args.User is not { } wearer || Deleted(wearer))
            return;

        if (_timing.CurTime < ent.Comp.NextActivation)
            return;

        if (TryInject(ent, wearer))
            args.Handled = true;
    }

    private bool TryInject(Entity<AddReagentOnTriggerComponent> ent, EntityUid wearer)
    {
        if (ent.Comp.Reagents.Count == 0)
            return false;

        var solution = new Solution(ent.Comp.Reagents);
        if (solution.Volume <= FixedPoint2.Zero)
            return false;

        if (!_bloodstream.TryAddToChemicals(wearer, solution))
            return false;

        ent.Comp.NextActivation = _timing.CurTime + ent.Comp.Cooldown;

        _reactive.DoEntityReaction(wearer, solution, ReactionMethod.Injection);
        _audio.PlayPvs(ent.Comp.InjectSound, wearer);

        _popup.PopupEntity(Loc.GetString("emergency-reagent-injector-activated"), wearer, wearer, PopupType.MediumCaution);

        _adminLogger.Add(LogType.ForceFeed, $"{ToPrettyString(wearer):user} received an emergency injection from {ToPrettyString(ent.Owner):clothing}: {SharedSolutionContainerSystem.ToPrettyString(solution):solution}");
        return true;
    }
}

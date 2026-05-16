using Content.Server.Administration.Logs;
using Content.Server.Body.Systems;
using Content.Shared.Chemistry;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Database;
using Content.Shared.FixedPoint;
using Content.Shared.Inventory;
using Content.Shared.Mobs;
using Content.Shared.Popups;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Timing;

namespace Content.Server._Exodus.EmergencyActions;

/// <summary>
/// Injects a configured reagent pool into the wearer's bloodstream when they transition from
/// Alive to Critical. Generic — no Asakim or other identity check is performed.
/// </summary>
public sealed class EmergencyReagentInjectorSystem : EntitySystem
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
        SubscribeLocalEvent<EmergencyReagentInjectorComponent, InventoryRelayedEvent<MobStateChangedEvent>>(OnMobStateChanged);
    }

    private void OnMobStateChanged(Entity<EmergencyReagentInjectorComponent> ent, ref InventoryRelayedEvent<MobStateChangedEvent> args)
    {
        if (args.Args.NewMobState != MobState.Critical || args.Args.OldMobState != MobState.Alive)
            return;

        var wearer = args.Args.Target;
        if (Deleted(wearer))
            return;

        if (_timing.CurTime < ent.Comp.NextActivation)
            return;

        TryInject(ent, wearer);
    }

    private void TryInject(Entity<EmergencyReagentInjectorComponent> ent, EntityUid wearer)
    {
        if (ent.Comp.Reagents.Count == 0)
            return;

        var solution = new Solution(ent.Comp.Reagents);
        if (solution.Volume <= FixedPoint2.Zero)
            return;

        if (!_bloodstream.TryAddToChemicals(wearer, solution))
            return;

        ent.Comp.NextActivation = _timing.CurTime + ent.Comp.Cooldown;

        _reactive.DoEntityReaction(wearer, solution, ReactionMethod.Injection);
        _audio.PlayPvs(ent.Comp.InjectSound, wearer);

        _popup.PopupEntity(Loc.GetString("emergency-reagent-injector-activated"), wearer, wearer, PopupType.MediumCaution);

        _adminLogger.Add(LogType.ForceFeed, $"{ToPrettyString(wearer):user} received an emergency injection from {ToPrettyString(ent.Owner):clothing}: {SharedSolutionContainerSystem.ToPrettyString(solution):solution}");
    }
}

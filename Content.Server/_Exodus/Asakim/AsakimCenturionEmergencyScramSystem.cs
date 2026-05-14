using Content.Server.Actions;
using Content.Server.Administration.Logs;
using Content.Server.Body.Systems;
using Content.Server.Chemistry.EntitySystems;
using Content.Server.Teleportation;
using Content.Shared.Actions;
using Content.Shared.Chemistry;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Clothing;
using Content.Shared.Database;
using Content.Shared.FixedPoint;
using Content.Shared.Inventory;
using Content.Shared.Mobs;
using Content.Shared.Popups;
using Content.Shared._Exodus.Asakim;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Timing;

namespace Content.Server._Exodus.Asakim;

public sealed class AsakimCenturionEmergencyScramSystem : EntitySystem
{
    [Dependency] private readonly ActionsSystem _actions = default!;
    [Dependency] private readonly AsakimIdentitySystem _asakim = default!;
    [Dependency] private readonly BloodstreamSystem _bloodstream = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly IAdminLogManager _adminLogger = default!;
    [Dependency] private readonly ReactiveSystem _reactive = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly TeleportSystem _teleport = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<AsakimCenturionEmergencyScramComponent, ClothingGotEquippedEvent>(OnEquipped);
        SubscribeLocalEvent<AsakimCenturionEmergencyScramComponent, ClothingGotUnequippedEvent>(OnUnequipped);
        SubscribeLocalEvent<AsakimCenturionEmergencyScramComponent, InventoryRelayedEvent<MobStateChangedEvent>>(OnMobStateChanged);
        SubscribeLocalEvent<AsakimCenturionEmergencyScramComponent, AsakimCenturionEmergencyScramActionEvent>(OnAction);
    }

    private void OnEquipped(Entity<AsakimCenturionEmergencyScramComponent> ent, ref ClothingGotEquippedEvent args)
    {
        if (!_asakim.IsAsakim(args.Wearer))
            return;

        _actions.AddAction(args.Wearer, ref ent.Comp.ActionUid, ent.Comp.ActionProto, ent.Owner);
        SyncActionCooldown(ent);
    }

    private void OnUnequipped(Entity<AsakimCenturionEmergencyScramComponent> ent, ref ClothingGotUnequippedEvent args)
    {
        _actions.RemoveProvidedActions(args.Wearer, ent.Owner);
    }

    private void OnMobStateChanged(Entity<AsakimCenturionEmergencyScramComponent> ent, ref InventoryRelayedEvent<MobStateChangedEvent> args)
    {
        if (args.Args.NewMobState != MobState.Critical || args.Args.OldMobState != MobState.Alive)
            return;

        var wearer = args.Args.Target;
        if (Deleted(wearer) || !_asakim.IsAsakim(wearer))
            return;

        if (_timing.CurTime < ent.Comp.NextActivation)
        {
            SyncActionCooldown(ent);
            return;
        }

        Activate(ent, wearer);
    }

    private void OnAction(Entity<AsakimCenturionEmergencyScramComponent> ent, ref AsakimCenturionEmergencyScramActionEvent args)
    {
        _popup.PopupEntity(Loc.GetString("asakim-centurion-emergency-scram-passive"), args.Performer, args.Performer);
    }

    private void Activate(Entity<AsakimCenturionEmergencyScramComponent> ent, EntityUid wearer)
    {
        ent.Comp.NextActivation = _timing.CurTime + ent.Comp.Cooldown;
        SyncActionCooldown(ent);

        InjectReagents(ent, wearer);
        _teleport.RandomTeleport(wearer, ent.Comp.Specifier);

        _popup.PopupEntity(Loc.GetString("asakim-centurion-emergency-scram-activated"), wearer, wearer, PopupType.MediumCaution);
    }

    private bool InjectReagents(Entity<AsakimCenturionEmergencyScramComponent> ent, EntityUid wearer)
    {
        if (ent.Comp.Reagents.Count == 0)
            return false;

        var solution = new Solution(ent.Comp.Reagents);
        if (solution.Volume <= FixedPoint2.Zero)
            return false;

        if (!_bloodstream.TryAddToChemicals(wearer, solution))
            return false;

        _reactive.DoEntityReaction(wearer, solution, ReactionMethod.Injection);
        _audio.PlayPvs(ent.Comp.InjectSound, wearer);
        _adminLogger.Add(LogType.ForceFeed, $"{ToPrettyString(wearer):user} was injected by {ToPrettyString(ent.Owner):using} with a solution {SharedSolutionContainerSystem.ToPrettyString(solution):removedSolution}");

        return true;
    }

    private void SyncActionCooldown(Entity<AsakimCenturionEmergencyScramComponent> ent)
    {
        if (ent.Comp.ActionUid == null || ent.Comp.NextActivation <= _timing.CurTime)
            return;

        _actions.SetCooldown(ent.Comp.ActionUid, _timing.CurTime, ent.Comp.NextActivation);
    }
}

using Content.Server._Exodus.Ghost.Roles;
using Content.Server.Ghost.Roles;
using Content.Server.Ghost.Roles.Components;
using Content.Shared._CorvaxNext.Silicons.Borgs.Components;
using Content.Shared.Mind.Components;
using Content.Shared.Silicons.StationAi;

namespace Content.Server._Exodus.Silicons.StationAi;

/// <summary>
/// Keeps an AI's ghost role reserved while its player controls a remote body.
/// </summary>
public sealed class StationAiGhostRoleSystem : EntitySystem
{
    [Dependency] private GhostRoleSystem _ghostRoles = default!;
    [Dependency] private SharedStationAiSystem _stationAi = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<StationAiHeldComponent, GhostRoleReregisterAttemptEvent>(OnReregisterAttempt);
        SubscribeLocalEvent<AiRemoteControllerComponent, MindRemovedMessage>(OnRemoteMindRemoved);
    }

    private void OnReregisterAttempt(Entity<StationAiHeldComponent> ent, ref GhostRoleReregisterAttemptEvent args)
    {
        if (TryComp<AiRemoteControllerComponent>(ent.Comp.CurrentConnectedEntity, out var remote)
            && remote.AiHolder == ent.Owner
            && remote.LinkedMind != null)
        {
            args.Cancelled = true;
        }
    }

    private void OnRemoteMindRemoved(Entity<AiRemoteControllerComponent> ent, ref MindRemovedMessage args)
    {
        // A destroyed remote returns its mind through the existing shutdown handler.
        if (MetaData(ent).EntityLifeStage >= EntityLifeStage.Terminating
            || ent.Comp.LinkedMind != args.Mind.Owner
            || ent.Comp.AiHolder is not { } brain
            || Deleted(brain)
            || MetaData(brain).EntityLifeStage >= EntityLifeStage.Terminating
            || !TryComp<GhostRoleComponent>(brain, out var role)
            || !TryComp<StationAiHeldComponent>(brain, out var held)
            || held.CurrentConnectedEntity != ent.Owner)
        {
            return;
        }

        // Returning normally clears CurrentConnectedEntity before transferring the mind.
        // Any other loss of the remote mind must release the AI and its stale control link.
        held.CurrentConnectedEntity = null;
        ent.Comp.AiHolder = null;
        ent.Comp.LinkedMind = null;

        if (_stationAi.TryGetCore(brain, out var core))
            _stationAi.SwitchRemoteEntityMode(core, true);

        _ghostRoles.TryReregisterGhostRole((brain, role));
    }
}

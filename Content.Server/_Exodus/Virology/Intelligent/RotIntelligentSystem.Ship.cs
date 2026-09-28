using Content.Server._Exodus.Shuttles;
using Content.Server._NF.Shuttles.Components;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Systems;
using Content.Shared._Exodus.Virology.Intelligent;
using Content.Shared.Shuttles.BUIStates;
using Content.Shared.Shuttles.Components;
using Content.Shared.Shuttles.Events;
using Content.Shared.Shuttles.Systems;
using Robust.Server.GameObjects;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics.Components;

namespace Content.Server._Exodus.Virology.Intelligent;

public sealed partial class RotIntelligentSystem
{
    [Dependency] private ShuttleConsoleSystem _consoles = default!;
    [Dependency] private GridAnchorReleaseSystem _anchors = default!;

    private void InitializeShip()
    {
        SubscribeLocalEvent<RotIntelligentComponent, RotPilotEvent>(OnPilot);
        SubscribeLocalEvent<RotIntelligentComponent, RotUnanchorEvent>(OnUnanchor);
        SubscribeLocalEvent<RotIntelligentComponent, ShuttlePilotStoppedEvent>(OnPilotStopped);
        SubscribeLocalEvent<ShuttlePilotAttemptEvent>(OnPilotAttempt);
    }

    private void OnPilot(Entity<RotIntelligentComponent> ent, ref RotPilotEvent args)
    {
        if (!args.Handled)
            args.Handled = TryPilot(ent);
    }

    private void OnUnanchor(Entity<RotIntelligentComponent> ent, ref RotUnanchorEvent args)
    {
        if (!args.Handled)
            args.Handled = TryUnanchor(ent);
    }

    public bool TryUnanchor(Entity<RotIntelligentComponent> ent)
    {
        if (!IsActiveCore(ent) || Transform(ent).GridUid is not { } grid || !HasComp<MapGridComponent>(grid)
            || !HasComp<PhysicsComponent>(grid))
        {
            Feedback(ent, "rot-intelligent-no-grid");
            return false;
        }
        if (TryComp<FTLComponent>(grid, out var ftl) && ftl.State is not (FTLState.Available or FTLState.Cooldown))
        {
            Feedback(ent, "rot-intelligent-in-ftl");
            return false;
        }
        if (!_anchors.TryRelease(grid))
        {
            if (TryComp<ForceAnchorComponent>(grid, out var anchor) && anchor.Bedrock)
                Feedback(ent, "rot-intelligent-bedrock-anchor");
            return false;
        }
        Feedback(ent, "rot-intelligent-unanchored");
        return true;
    }

    public bool TryPilot(Entity<RotIntelligentComponent> ent)
    {
        if (!IsActiveCore(ent) || Transform(ent).GridUid is not { } grid || !Transform(ent).Anchored)
            return false;
        if (HasComp<PilotComponent>(ent))
        {
            StopPiloting(ent);
            return true;
        }
        // A user-facing attempt must not evict somebody already flying the ship.
        var pilots = EntityQueryEnumerator<PilotComponent>();
        while (pilots.MoveNext(out var uid, out var pilot))
        {
            if (uid != ent.Owner && pilot.Console is { } console && !TerminatingOrDeleted(console)
                && Transform(console).GridUid == grid)
            {
                Feedback(ent, "rot-intelligent-pilot-busy");
                return false;
            }
        }
        if (!_consoles.TryStartPilot(ent, ent))
        {
            Feedback(ent, "rot-intelligent-cannot-pilot");
            return false;
        }
        var reservation = EnsureComp<RotPilotLockComponent>(grid);
        reservation.Core = ent;
        EnsureComp<RotColonyStateComponent>(ent).PilotGrid = grid;
        _eyes.SetTarget(ent, null);
        if (!_ui.TryOpenUi(ent.Owner, ShuttleConsoleUiKey.Key, ent.Owner))
        {
            StopPiloting(ent);
            return false;
        }
        return true;
    }

    private void OnPilotAttempt(ref ShuttlePilotAttemptEvent args)
    {
        if (HasComp<RotIntelligentComponent>(args.User) && !IsActiveCore(args.User))
        {
            args.Cancelled = true;
            return;
        }
        if (args.Grid is not { } grid || !TryComp<RotPilotLockComponent>(grid, out var reservation))
            return;
        if (!IsLivingCore(reservation.Core) || !HasComp<PilotComponent>(reservation.Core))
        {
            RemCompDeferred<RotPilotLockComponent>(grid);
            return;
        }
        args.Cancelled |= args.User != reservation.Core;
        if (args.Cancelled)
            _popup.PopupEntity(Loc.GetString("rot-intelligent-pilot-busy"), args.User, args.User);
    }

    private void StopPiloting(EntityUid core)
    {
        if (HasComp<PilotComponent>(core))
            _consoles.RemovePilot(core);
        ReleasePilotLock(core);
        _ui.CloseUi(core, ShuttleConsoleUiKey.Key);
    }

    private void ReleasePilotLock(EntityUid core)
    {
        if (!TryComp<RotColonyStateComponent>(core, out var state))
            return;
        if (state.PilotGrid is { } grid && TryComp<RotPilotLockComponent>(grid, out var reservation) && reservation.Core == core)
            RemCompDeferred<RotPilotLockComponent>(grid);
        state.PilotGrid = null;
    }

    private void OnPilotStopped(Entity<RotIntelligentComponent> ent, ref ShuttlePilotStoppedEvent args)
    {
        ReleasePilotLock(ent);
        if (ent.Comp.Alive)
            AttachEye(ent);
    }
}

using System.Numerics;
using Content.Server._Exodus.Territory;
using Content.Shared._Exodus.Virology.Intelligent;
using Content.Shared.Actions;
using Content.Shared.DoAfter;
using Content.Shared.Mobs.Components;
using Content.Shared.Movement.Components;
using Content.Shared.Physics;
using Content.Shared.UserInterface;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics.Components;
using Robust.Shared.Player;

namespace Content.Server._Exodus.Virology.Intelligent;

public sealed partial class RotIntelligentSystem
{
    [Dependency] private TerritoryOverrideSystem _territoryOverrides = default!;

    private void InitializeRooting()
    {
        SubscribeLocalEvent<RotIntelligentComponent, RotToggleRootEvent>(OnToggleRoot);
        SubscribeLocalEvent<RotIntelligentComponent, RotRootingEvent>(OnRootingDone);
        SubscribeLocalEvent<RotIntelligentComponent, ActivatableUIOpenAttemptEvent>(OnColonyUiAttempt);
    }

    private void OnColonyUiAttempt(Entity<RotIntelligentComponent> ent, ref ActivatableUIOpenAttemptEvent args)
    {
        if (!IsActiveCore(ent))
            args.Cancel();
    }

    public bool IsActiveCore(EntityUid core) => IsLivingCore(core)
        && _coreQuery.TryComp(core, out var brain) && brain.Rooted
        && !brain.ChangingForm && Transform(core).Anchored;

    private void OnToggleRoot(Entity<RotIntelligentComponent> ent, ref RotToggleRootEvent args)
    {
        if (!args.Handled)
            args.Handled = TryToggleRoot(ent);
    }

    public bool TryToggleRoot(Entity<RotIntelligentComponent> ent)
    {
        if (!IsLivingCore(ent) || ent.Comp.ChangingForm || _containers.IsEntityInContainer(ent)
            || !TryComp<RotColonyStateComponent>(ent, out var state))
            return false;
        if (!ent.Comp.Rooted && !CanRoot(ent, out var reason))
        {
            Feedback(ent, reason);
            return false;
        }
        var args = new DoAfterArgs(EntityManager, ent, ent.Comp.RootDuration, new RotRootingEvent(), ent)
        {
            NeedHand = false,
            BreakOnMove = true,
            BreakOnDamage = true,
            MultiplyDelay = false,
        };
        if (!_doAfter.TryStartDoAfter(args, out state.Rooting))
            return false;
        ent.Comp.ChangingForm = true;
        StopPiloting(ent);
        _ui.CloseUi(ent.Owner, RotIntelligentUiKey.Key);
        RefreshRootActions(ent);
        Dirty(ent);
        return true;
    }

    private bool CanRoot(EntityUid core, out string reason)
    {
        reason = "rot-intelligent-needs-floor";
        var xform = Transform(core);
        if (xform.GridUid is not { } grid || !TryComp<MapGridComponent>(grid, out var mapGrid)
            || _containers.IsEntityInContainer(core))
            return false;
        var tile = _maps.TileIndicesFor(grid, mapGrid, xform.Coordinates);
        if (_turf.IsSpace(_maps.GetTileRef(grid, mapGrid, tile)))
            return false;
        reason = "rot-intelligent-root-blocked";
        var point = _maps.GridTileToLocal(grid, mapGrid, tile);
        _placement.Clear();
        _lookup.GetLocalEntitiesIntersecting(grid, Box2.CenteredAround(point.Position, new Vector2(0.9f)), _placement,
            flags: LookupFlags.Uncontained);
        foreach (var uid in _placement)
        {
            if (uid == core || TerminatingOrDeleted(uid) || _containers.IsEntityInContainer(uid))
                continue;
            if (_members.TryComp(uid, out var member))
            {
                if (member.Size != Vector2i.Zero && member.Core != core)
                {
                    reason = "rot-intelligent-other-colony";
                    return false;
                }
                if (member.Tissue)
                    continue;
                if (member.Size != Vector2i.Zero)
                    return false;
            }
            // Camera eyes and observers have mob state but no solid body.
            if (!TryComp<PhysicsComponent>(uid, out var physics) || !physics.CanCollide || !physics.Hard)
                continue;
            if (HasComp<MobStateComponent>(uid) && !_mobs.IsDead(uid)
                || (physics.CollisionLayer & (int)CollisionGroup.FullTileMask) != 0)
                return false;
        }
        return true;
    }

    private void OnRootingDone(Entity<RotIntelligentComponent> ent, ref RotRootingEvent args)
    {
        if (args.Handled || !TryComp<RotColonyStateComponent>(ent, out var state) || state.Rooting != args.DoAfter.Id)
            return;
        args.Handled = true;
        state.Rooting = null;
        ent.Comp.ChangingForm = false;
        var reason = "rot-intelligent-root-blocked";
        if (!args.Cancelled && IsLivingCore(ent) && !_containers.IsEntityInContainer(ent))
        {
            if (ent.Comp.Rooted)
                Uproot(ent, state);
            else if (CanRoot(ent, out reason) && _transform.AnchorEntity((ent.Owner, Transform(ent))))
            {
                ent.Comp.Rooted = true;
                MarkNetworkDirty(ent);
                if (HasComp<ActorComponent>(ent))
                    RestoreEye(ent);
                Feedback(ent, "rot-intelligent-rooted");
            }
            else
                Feedback(ent, reason);
        }
        RefreshRootActions(ent);
        Dirty(ent);
    }

    private void Uproot(Entity<RotIntelligentComponent> ent, RotColonyStateComponent state)
    {
        ent.Comp.Rooted = false;
        ent.Comp.ConstructionAvailable = false;
        ent.Comp.NetworkReady = false;
        ent.Comp.Income = 0;
        ClearEye(ent);
        _transform.Unanchor(ent);
        state.Scratch.Clear();
        state.Scratch.AddRange(state.Projects);
        foreach (var job in state.Scratch)
            CancelProject(job, refund: true);
        foreach (var uid in state.Members)
        {
            if (_members.TryComp(uid, out var member))
                SetConnected((uid, member), false);
        }
        state.Rally = null;
        if (state.RallyMarker is { } marker)
            QueueDel(marker);
        state.RallyMarker = null;
        state.NetworkEnumerator.Dispose();
        state.NetworkPhase = RotNetworkPhase.Idle;
        state.DirtyNetwork = true;
        state.Connected.Clear();
        Feedback(ent, "rot-intelligent-mobile");
    }

    private void RefreshRootActions(Entity<RotIntelligentComponent> ent)
    {
        // A running uprooting do-after does not release control until the core actually detaches.
        _territoryOverrides.SetEnabled(ent.Owner, ent.Comp.Alive && ent.Comp.Rooted);
        var enabled = IsActiveCore(ent);
        if (TryComp<ActionGrantComponent>(ent, out var granted))
        {
            foreach (var action in granted.ActionEntities)
                _actions.SetEnabled(action, enabled);
        }
        _actions.SetEnabled(ent.Comp.BuildAction, enabled);
        _actions.SetEnabled(ent.Comp.RootAction, ent.Comp.Alive && !ent.Comp.ChangingForm);
        _actions.SetToggled(ent.Comp.RootAction, !ent.Comp.Rooted);
    }
}

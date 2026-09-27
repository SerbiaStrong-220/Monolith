using System.Numerics;
using Content.Shared._Exodus.Genetics;
using Content.Shared.IdentityManagement;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Robust.Server.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Player;

namespace Content.Server._Exodus.Genetics;

public sealed partial class GeneticAbilitiesSystem
{
    [Dependency] private readonly UserInterfaceSystem _ui = default!;
    [Dependency] private readonly ViewSubscriberSystem _views = default!;

    private void InitializeViewing()
    {
        SubscribeLocalEvent<GeneticEffectsComponent, GeneticRemoteViewingEvent>(OnViewAction);
        SubscribeLocalEvent<GeneticEffectsComponent, GeneticViewMessage>(OnViewMessage);
        SubscribeLocalEvent<GeneticAbilityStateComponent, BoundUIClosedEvent>(OnViewClosed);
        SubscribeLocalEvent<GeneticAbilityStateComponent, PlayerDetachedEvent>(OnDetached);
    }

    private bool CanObserve(EntityUid observer, EntityUid target)
    {
        return observer != target && CanUse(observer, GeneticAbility.RemoteViewing) &&
               HasAbility(target, GeneticAbility.RemoteViewing) &&
               TryComp<MobStateComponent>(target, out var state) && state.CurrentState != MobState.Dead &&
               Transform(target).MapUid != null;
    }

    private void OnViewAction(Entity<GeneticEffectsComponent> ent, ref GeneticRemoteViewingEvent args)
    {
        if (args.Handled || !CanUse(ent, GeneticAbility.RemoteViewing))
            return;
        EnsureComp<GeneticAbilityStateComponent>(ent);
        if (!_ui.HasUi(ent.Owner, GeneticsUiKey.RemoteViewing))
            _ui.SetUi(ent.Owner, GeneticsUiKey.RemoteViewing, new InterfaceData("GeneticViewBoundUserInterface"));
        if (!_ui.TryOpenUi(ent.Owner, GeneticsUiKey.RemoteViewing, ent.Owner))
            return;
        UpdateViewUi(ent);
        args.Handled = true;
    }

    private void OnViewMessage(Entity<GeneticEffectsComponent> ent, ref GeneticViewMessage args)
    {
        if (args.Actor != ent.Owner || !CanUse(ent, GeneticAbility.RemoteViewing) ||
            !TryComp<GeneticAbilityStateComponent>(ent, out var state))
            return;
        if (args.Refresh)
        {
            UpdateViewUi(ent);
            return;
        }
        if (args.Target == null)
        {
            StopViewing((ent.Owner, state), false);
            UpdateViewUi(ent);
            return;
        }
        var target = GetEntity(args.Target.Value);
        if (!CanObserve(ent, target) || !TryComp<ActorComponent>(ent, out var actor))
            return;

        StopViewing((ent.Owner, state), false);
        // A dedicated eye prevents disconnecting this ability from removing another system's subscription.
        // Like body cameras, the eye follows its parent; it does not transfer the observer's mind or controls.
        var eye = Spawn(state.ObservationEye, new EntityCoordinates(target, Vector2.Zero));
        _views.AddViewSubscriber(eye, actor.PlayerSession);
        state.ViewTarget = target;
        state.ViewEye = eye;
        state.ViewSession = actor.PlayerSession;
        state.NextViewCheck = _timing.CurTime + state.ViewCheckInterval;
        UpdateViewUi(ent);
    }

    private void UpdateViewUi(EntityUid uid)
    {
        if (TerminatingOrDeleted(uid))
            return;
        var targets = new Dictionary<NetEntity, string>();
        var query = EntityQueryEnumerator<GeneticEffectsComponent>();
        while (query.MoveNext(out var target, out _))
        {
            if (CanObserve(uid, target))
                targets.Add(GetNetEntity(target), Identity.Name(target, EntityManager));
        }
        NetEntity? eye = null;
        if (TryComp<GeneticAbilityStateComponent>(uid, out var state) && state.ViewEye is { } view && !TerminatingOrDeleted(view))
            eye = GetNetEntity(view);
        _ui.SetUiState(uid, GeneticsUiKey.RemoteViewing, new GeneticViewState(targets, eye));
    }

    private void StopViewing(Entity<GeneticAbilityStateComponent> ent, bool close)
    {
        if (ent.Comp.ViewEye is { } eye && !TerminatingOrDeleted(eye))
        {
            if (ent.Comp.ViewSession is { } session)
                _views.RemoveViewSubscriber(eye, session);
            QueueDel(eye);
        }
        ent.Comp.ViewEye = null;
        ent.Comp.ViewTarget = null;
        ent.Comp.ViewSession = null;
        if (close && !TerminatingOrDeleted(ent))
            _ui.CloseUi(ent.Owner, GeneticsUiKey.RemoteViewing);
    }

    private void OnViewClosed(Entity<GeneticAbilityStateComponent> ent, ref BoundUIClosedEvent args)
    {
        if (Equals(args.UiKey, GeneticsUiKey.RemoteViewing))
            StopViewing(ent, false);
    }

    private void OnDetached(Entity<GeneticAbilityStateComponent> ent, ref PlayerDetachedEvent args)
    {
        StopViewing(ent, true);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        var query = EntityQueryEnumerator<GeneticAbilityStateComponent>();
        while (query.MoveNext(out var uid, out var state))
        {
            if (state.HearingUntil != TimeSpan.Zero && state.HearingUntil <= _timing.CurTime)
            {
                state.HearingUntil = TimeSpan.Zero;
                StopHearing(uid);
            }
            if (state.ViewTarget is not { } target || state.NextViewCheck > _timing.CurTime)
                continue;
            state.NextViewCheck += state.ViewCheckInterval;
            if (!CanObserve(uid, target) || state.ViewEye is not { } eye || TerminatingOrDeleted(eye) ||
                !TryComp<ActorComponent>(uid, out var actor) || actor.PlayerSession != state.ViewSession)
            {
                StopViewing((uid, state), false);
                UpdateViewUi(uid);
            }
        }
    }
}

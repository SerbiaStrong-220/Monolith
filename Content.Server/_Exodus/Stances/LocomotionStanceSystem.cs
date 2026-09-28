using Content.Server.Shuttles.Systems;
using Content.Shared._Exodus.Stances;
using Content.Shared.ActionBlocker;
using Content.Shared.Actions;
using Content.Shared.Body.Components;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.Buckle;
using Content.Shared.Buckle.Components;
using Content.Shared.DoAfter;
using Content.Shared.Hands;
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Content.Shared.Movement.Pulling.Components;
using Content.Shared.Movement.Pulling.Systems;
using Content.Shared.Movement.Systems;
using Content.Shared.Weapons.Melee;
using Content.Shared.Weapons.Melee.Events;
using Robust.Shared.Timing;

namespace Content.Server._Exodus.Stances;

public sealed partial class LocomotionStanceSystem : SharedLocomotionStanceSystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SharedActionsSystem _actions = default!;
    [Dependency] private ActionBlockerSystem _blocker = default!;
    [Dependency] private SharedBodySystem _body = default!;
    [Dependency] private SharedBuckleSystem _buckle = default!;
    [Dependency] private SharedDoAfterSystem _doAfter = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private MovementSpeedModifierSystem _movement = default!;
    [Dependency] private PullingSystem _pulling = default!;
    [Dependency] private ShuttleConsoleSystem _shuttle = default!;
    [Dependency] private SharedUserInterfaceSystem _ui = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<LocomotionStanceComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<LocomotionStanceComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<LocomotionStanceComponent, ToggleLocomotionStanceEvent>(OnToggleStance);
        SubscribeLocalEvent<LocomotionStanceComponent, ToggleCurledRestEvent>(OnToggleRest);
        SubscribeLocalEvent<LocomotionStanceComponent, MeleeAttackEvent>(OnMeleeAttack);
        SubscribeLocalEvent<LocomotionStanceComponent, MobStateChangedEvent>(OnMobStateChanged);
    }

    private void OnMapInit(Entity<LocomotionStanceComponent> ent, ref MapInitEvent args)
    {
        _actions.AddAction(ent, ref ent.Comp.StanceAction, ent.Comp.ToggleStanceAction);
        _actions.AddAction(ent, ref ent.Comp.RestAction, ent.Comp.ToggleRestAction);
        ApplyStance(ent);
    }

    private void OnShutdown(Entity<LocomotionStanceComponent> ent, ref ComponentShutdown args)
    {
        _actions.RemoveAction(ent.Comp.StanceAction);
        _actions.RemoveAction(ent.Comp.RestAction);
    }

    private void OnToggleStance(Entity<LocomotionStanceComponent> ent, ref ToggleLocomotionStanceEvent args)
    {
        if (args.Handled || ent.Comp.Stance == LocomotionStance.Curled)
            return;

        args.Handled = TryChangeStance(ent, ent.Comp.Stance == LocomotionStance.Upright
            ? LocomotionStance.Quadruped
            : LocomotionStance.Upright);
    }

    private void OnToggleRest(Entity<LocomotionStanceComponent> ent, ref ToggleCurledRestEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = TryChangeStance(ent, ent.Comp.Stance == LocomotionStance.Curled
            ? ent.Comp.UncurledStance
            : LocomotionStance.Curled);
    }

    /// <summary>Starts a timed anatomical transition and releases all ongoing manual activity.</summary>
    public bool TryChangeStance(Entity<LocomotionStanceComponent> ent, LocomotionStance stance)
    {
        if (ent.Comp.Stance == stance || ent.Comp.TransitionEnd != TimeSpan.Zero || !_mobState.IsAlive(ent))
            return false;

        // A restrained passenger must first free itself using the normal buckle rules.
        if (TryComp<BuckleComponent>(ent, out var buckle) && buckle.Buckled &&
            !_buckle.TryUnbuckle(ent, ent, buckle))
            return false;

        if (stance == LocomotionStance.Curled)
            ent.Comp.UncurledStance = ent.Comp.Stance;

        ent.Comp.PreviousStance = ent.Comp.Stance;
        ent.Comp.Stance = stance;
        ent.Comp.TransitionEnd = _timing.CurTime + ent.Comp.TransitionDuration;
        ent.Comp.AttackAnimationEnd = TimeSpan.Zero;

        InterruptManualActivity(ent);
        ApplyStance(ent);
        return true;
    }

    private void InterruptManualActivity(Entity<LocomotionStanceComponent> ent)
    {
        _shuttle.RemovePilot(ent);
        _ui.CloseUserUis((ent.Owner, null));

        if (TryComp<DoAfterComponent>(ent, out var doAfters))
        {
            // Cancellation can invoke callbacks; do not enumerate a live dictionary across them.
            foreach (var id in new List<ushort>(doAfters.DoAfters.Keys))
                _doAfter.Cancel(ent, id, doAfters);
        }

        if (TryComp<PullerComponent>(ent, out var puller) &&
            TryComp<PullableComponent>(puller.Pulling, out var pulled))
            _pulling.TryStopPull(puller.Pulling!.Value, pulled);

        if (TryComp<HandsComponent>(ent, out var hands))
            _hands.RemoveHands(ent, hands);
    }

    private void ApplyStance(Entity<LocomotionStanceComponent> ent)
    {
        if (TryComp<MeleeWeaponComponent>(ent, out var melee))
        {
            var upright = ent.Comp.Stance == LocomotionStance.Upright;
            melee.Damage = upright ? ent.Comp.UprightDamage : ent.Comp.QuadrupedDamage;
            melee.Animation = upright ? ent.Comp.UprightAttackAnimation : ent.Comp.QuadrupedAttackAnimation;
            melee.WideAnimation = melee.Animation;
            melee.Attacking = false;
            Dirty(ent.Owner, melee);
        }

        if (CanUseHands(ent.Comp))
            RestoreAnatomicalHands(ent);
        else if (TryComp<HandsComponent>(ent, out var hands))
            _hands.RemoveHands(ent, hands);

        _actions.SetToggled(ent.Comp.StanceAction, ent.Comp.Stance == LocomotionStance.Upright);
        _actions.SetToggled(ent.Comp.RestAction, ent.Comp.Stance == LocomotionStance.Curled);
        _actions.SetEnabled(ent.Comp.StanceAction, ent.Comp.Stance != LocomotionStance.Curled);
        _blocker.UpdateCanMove(ent);
        _movement.RefreshMovementSpeedModifiers(ent);
        Dirty(ent);
    }

    private void RestoreAnatomicalHands(Entity<LocomotionStanceComponent> ent)
    {
        if (!TryComp<BodyComponent>(ent, out var body) || !TryComp<HandsComponent>(ent, out var hands))
            return;

        foreach (var (uid, part) in _body.GetBodyChildrenOfType(ent, BodyPartType.Hand, body))
        {
            if (!part.Enabled || part.ParentSlot == null ||
                !_body.TryGetParentBodyPart(uid, out _, out var parent) || !parent.Enabled)
                continue;

            var location = part.Symmetry switch
            {
                BodyPartSymmetry.Left => HandLocation.Left,
                BodyPartSymmetry.Right => HandLocation.Right,
                _ => HandLocation.Middle,
            };
            _hands.AddHand(ent, SharedBodySystem.GetPartSlotContainerId(part.ParentSlot.Value.Id),
                location, part.InhandVisualOffset, hands);
        }
    }

    private void OnMeleeAttack(Entity<LocomotionStanceComponent> ent, ref MeleeAttackEvent args)
    {
        if (args.Weapon != ent.Owner)
            return;

        ent.Comp.AttackAnimationEnd = _timing.CurTime + ent.Comp.AttackAnimationDuration;
        Dirty(ent);
    }

    private void OnMobStateChanged(Entity<LocomotionStanceComponent> ent, ref MobStateChangedEvent args)
    {
        if (args.NewMobState == MobState.Alive)
            return;

        ent.Comp.AttackAnimationEnd = TimeSpan.Zero;
        Dirty(ent);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        var query = EntityQueryEnumerator<LocomotionStanceComponent>();
        while (query.MoveNext(out var uid, out var stance))
        {
            if (stance.TransitionEnd == TimeSpan.Zero || _timing.CurTime < stance.TransitionEnd)
                continue;

            stance.TransitionEnd = TimeSpan.Zero;
            ApplyStance((uid, stance));
        }
    }
}
